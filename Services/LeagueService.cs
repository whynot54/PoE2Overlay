using System.Net.Http;
using System.Text.Json;

namespace PoE2Overlay.Services;

public sealed class LeagueService
{
    private static readonly HttpClient Http = new();
    private const string LeaguesUrl = "https://www.pathofexile.com/api/trade2/data/leagues";

    private List<string>? _cachedLeagues;
    private string _selectedLeague = string.Empty;
    private string _overrideLeague = string.Empty;

    public string CurrentLeague => !string.IsNullOrEmpty(_overrideLeague)
        ? _overrideLeague
        : _selectedLeague;

    public IReadOnlyList<string> AvailableLeagues => _cachedLeagues?.AsReadOnly() ?? (IReadOnlyList<string>)Array.Empty<string>();

    public void SetOverride(string league)
    {
        _overrideLeague = league;
    }

    public async Task InitializeAsync(CancellationToken ct = default)
    {
        try
        {
            var response = await Http.GetStringAsync(LeaguesUrl, ct);
            var doc = JsonDocument.Parse(response);
            var leagues = new List<string>();

            if (doc.RootElement.TryGetProperty("result", out var result))
            {
                foreach (var league in result.EnumerateArray())
                {
                    if (league.TryGetProperty("id", out var id))
                    {
                        leagues.Add(id.GetString() ?? string.Empty);
                    }
                }
            }

            _cachedLeagues = leagues;

            // Auto-select: prefer the first non-Standard, non-Hardcore league (challenge league)
            _selectedLeague = leagues.FirstOrDefault(l =>
                !l.Equals("Standard", StringComparison.OrdinalIgnoreCase)
                && !l.StartsWith("Hardcore", StringComparison.OrdinalIgnoreCase)
                && !l.StartsWith("SSF", StringComparison.OrdinalIgnoreCase))
                ?? leagues.FirstOrDefault()
                ?? "Standard";
        }
        catch
        {
            _cachedLeagues = ["Standard"];
            _selectedLeague = "Standard";
        }
    }
}
