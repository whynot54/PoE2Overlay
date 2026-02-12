using System.Collections.Immutable;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using PoE2Overlay.Models;

namespace PoE2Overlay.Services;

public sealed class TradeApiClient
{
    private const string BaseUrl = "https://www.pathofexile.com";
    private const int MaxFetchIds = 10;

    private readonly HttpClient _http;
    private readonly RateLimiter _searchLimiter = new();
    private readonly RateLimiter _fetchLimiter = new();

    public TradeApiClient(string poeSessId = "")
    {
        var handler = new HttpClientHandler();
        if (!string.IsNullOrEmpty(poeSessId))
        {
            handler.CookieContainer = new CookieContainer();
            handler.CookieContainer.Add(new Uri(BaseUrl), new Cookie("POESESSID", poeSessId));
        }

        _http = new HttpClient(handler);
        _http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        _http.DefaultRequestHeaders.UserAgent.ParseAdd("PoE2Overlay/1.0 (contact: overlay@poe2helper.dev)");
    }

    public async Task<PriceResult?> PriceCheckAsync(ItemData item, string league, CancellationToken ct = default)
    {
        var searchResult = await SearchAsync(item, league, ct);
        if (searchResult == null || searchResult.ResultIds.Count == 0)
            return null;

        var listings = await FetchAsync(searchResult.QueryId, searchResult.ResultIds, ct);

        return AggregatePrices(listings, searchResult.QueryId, searchResult.TotalCount, league);
    }

    private async Task<SearchResult?> SearchAsync(ItemData item, string league, CancellationToken ct)
    {
        await _searchLimiter.WaitForSlotAsync(ct);

        var query = TradeQueryBuilder.Build(item);
        var json = query.ToJsonString();
        var content = new StringContent(json, Encoding.UTF8, "application/json");

        var url = $"{BaseUrl}/api/trade2/search/poe2/{Uri.EscapeDataString(league)}";
        var response = await _http.PostAsync(url, content, ct);

        UpdateRateLimits(response.Headers, _searchLimiter);

        var body = await response.Content.ReadAsStringAsync(ct);

        if (!response.IsSuccessStatusCode)
        {
            // Parse error message from API for better diagnostics
            try
            {
                var errDoc = JsonDocument.Parse(body);
                if (errDoc.RootElement.TryGetProperty("error", out var err)
                    && err.TryGetProperty("message", out var msg))
                {
                    throw new Exception($"Trade API: {msg.GetString()}");
                }
            }
            catch (JsonException) { }

            throw new Exception($"Trade API returned {(int)response.StatusCode}");
        }

        var doc = JsonDocument.Parse(body);
        var root = doc.RootElement;

        var queryId = root.GetProperty("id").GetString() ?? string.Empty;
        var total = root.TryGetProperty("total", out var totalProp) ? totalProp.GetInt32() : 0;

        var resultIds = new List<string>();
        if (root.TryGetProperty("result", out var resultArray))
        {
            foreach (var id in resultArray.EnumerateArray().Take(MaxFetchIds))
            {
                resultIds.Add(id.GetString() ?? string.Empty);
            }
        }

        return new SearchResult(queryId, resultIds, total);
    }

    private async Task<List<PriceListing>> FetchAsync(string queryId, List<string> ids, CancellationToken ct)
    {
        if (ids.Count == 0) return [];

        await _fetchLimiter.WaitForSlotAsync(ct);

        var idsParam = string.Join(",", ids.Take(MaxFetchIds));
        var url = $"{BaseUrl}/api/trade2/fetch/{idsParam}?query={queryId}";
        var response = await _http.GetAsync(url, ct);

        UpdateRateLimits(response.Headers, _fetchLimiter);

        if (!response.IsSuccessStatusCode)
            return [];

        var body = await response.Content.ReadAsStringAsync(ct);
        var doc = JsonDocument.Parse(body);
        var listings = new List<PriceListing>();

        if (!doc.RootElement.TryGetProperty("result", out var results))
            return listings;

        foreach (var result in results.EnumerateArray())
        {
            if (!result.TryGetProperty("listing", out var listing))
                continue;

            var price = ParsePrice(listing);
            if (price == null)
                continue;

            var accountName = string.Empty;
            if (listing.TryGetProperty("account", out var account)
                && account.TryGetProperty("name", out var name))
            {
                accountName = name.GetString() ?? string.Empty;
            }

            var timeAgo = string.Empty;
            if (listing.TryGetProperty("indexed", out var indexed))
            {
                timeAgo = FormatTimeAgo(indexed.GetString());
            }

            listings.Add(new PriceListing
            {
                Price = price.Value.Amount,
                Currency = price.Value.CurrencyType,
                AccountName = accountName,
                TimeAgo = timeAgo
            });
        }

        return listings;
    }

    private static (double Amount, string CurrencyType)? ParsePrice(JsonElement listing)
    {
        if (!listing.TryGetProperty("price", out var price))
            return null;

        var amount = 0.0;
        if (price.TryGetProperty("amount", out var amountProp))
            amount = amountProp.GetDouble();

        var currency = "chaos";
        if (price.TryGetProperty("currency", out var currencyProp))
            currency = currencyProp.GetString() ?? "chaos";

        return (amount, currency);
    }

    private static PriceResult AggregatePrices(List<PriceListing> listings, string queryId, int totalCount, string league)
    {
        if (listings.Count == 0)
        {
            return new PriceResult
            {
                QueryId = queryId,
                TotalListings = totalCount,
                League = league
            };
        }

        var prices = listings.Select(l => l.Price).OrderBy(p => p).ToList();
        var min = prices[0];
        var max = prices[^1];
        var median = prices[prices.Count / 2];
        var average = Math.Round(prices.Average(), 1);
        var primaryCurrency = listings
            .GroupBy(l => l.Currency)
            .OrderByDescending(g => g.Count())
            .First().Key;

        return new PriceResult
        {
            Min = min,
            Max = max,
            Median = median,
            Average = average,
            Currency = primaryCurrency,
            TotalListings = totalCount,
            Listings = listings.ToImmutableList(),
            QueryId = queryId,
            League = league
        };
    }

    private static void UpdateRateLimits(HttpResponseHeaders headers, RateLimiter limiter)
    {
        if (headers.TryGetValues("x-rate-limit-ip", out var limitValues))
        {
            limiter.ParseHeaders(limitValues.FirstOrDefault(), null);
        }
    }

    private static string FormatTimeAgo(string? isoDate)
    {
        if (string.IsNullOrEmpty(isoDate) || !DateTime.TryParse(isoDate, out var date))
            return string.Empty;

        var diff = DateTime.UtcNow - date.ToUniversalTime();
        return diff.TotalMinutes switch
        {
            < 60 => $"{(int)diff.TotalMinutes}m ago",
            < 1440 => $"{(int)diff.TotalHours}h ago",
            _ => $"{(int)diff.TotalDays}d ago"
        };
    }

    private sealed record SearchResult(string QueryId, List<string> ResultIds, int TotalCount);
}
