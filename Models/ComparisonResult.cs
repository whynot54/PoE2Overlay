using System.Collections.Immutable;

namespace PoE2Overlay.Models;

public enum ComparisonVerdict
{
    Upgrade,
    Downgrade,
    Sidegrade
}

public sealed record CategoryScore
{
    public StatCategory Category { get; init; }
    public string DisplayName { get; init; } = string.Empty;
    public double DroppedScore { get; init; }
    public double EquippedScore { get; init; }
    public double Difference { get; init; }
}

public sealed record StatDiff
{
    public string ModLine { get; init; } = string.Empty;
    public double Value { get; init; }
    public StatCategory? Category { get; init; }
    public bool IsUnscored { get; init; }
    public bool IsGain { get; init; }
}

public sealed record ComparisonResult
{
    public double DroppedTotalScore { get; init; }
    public double EquippedTotalScore { get; init; }
    public ComparisonVerdict Verdict { get; init; }
    public double PercentChange { get; init; }
    public ImmutableList<CategoryScore> CategoryScores { get; init; } = ImmutableList<CategoryScore>.Empty;
    public ImmutableList<StatDiff> StatDiffs { get; init; } = ImmutableList<StatDiff>.Empty;
    public ImmutableList<string> UnscoredMods { get; init; } = ImmutableList<string>.Empty;
    public double? DreamTargetScore { get; init; }
    public double? DreamPercentOfTarget { get; init; }
}
