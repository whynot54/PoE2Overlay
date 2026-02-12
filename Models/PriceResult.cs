using System.Collections.Immutable;

namespace PoE2Overlay.Models;

public sealed record PriceListing
{
    public double Price { get; init; }
    public string Currency { get; init; } = string.Empty;
    public string AccountName { get; init; } = string.Empty;
    public string TimeAgo { get; init; } = string.Empty;
}

public sealed record PriceResult
{
    public double Min { get; init; }
    public double Max { get; init; }
    public double Median { get; init; }
    public double Average { get; init; }
    public string Currency { get; init; } = "chaos";
    public int TotalListings { get; init; }
    public ImmutableList<PriceListing> Listings { get; init; } = ImmutableList<PriceListing>.Empty;
    public string QueryId { get; init; } = string.Empty;
    public string League { get; init; } = string.Empty;
}
