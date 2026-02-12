namespace PoE2Overlay.Models;

public sealed class AppConfig
{
    public string PoeSessId { get; set; } = string.Empty;
    public string LeagueOverride { get; set; } = string.Empty;
    public int AutoDismissSeconds { get; set; } = 15;
    public double OverlayOpacity { get; set; } = 0.93;
}
