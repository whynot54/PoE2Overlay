using System.Collections.Immutable;
using PoE2Overlay.Models;

namespace PoE2Overlay.Services;

public sealed class ItemScoringService
{
    private readonly StatDefinitionService _statDefs;
    private const double SidegradeThreshold = 5.0;

    public ItemScoringService(StatDefinitionService statDefs)
    {
        _statDefs = statDefs;
    }

    public ComparisonResult Compare(ItemData dropped, ItemData equipped, ItemData? dreamTarget, Dictionary<string, int> weights)
    {
        var droppedStats = ExtractCategoryValues(dropped);
        var equippedStats = ExtractCategoryValues(equipped);

        var droppedTotal = 0.0;
        var equippedTotal = 0.0;
        var categoryScores = new List<CategoryScore>();

        foreach (var category in Enum.GetValues<StatCategory>())
        {
            var key = category.ToString();
            var weight = weights.TryGetValue(key, out var w) ? w : 0;

            var droppedVal = droppedStats.TryGetValue(category, out var dv) ? dv : 0;
            var equippedVal = equippedStats.TryGetValue(category, out var ev) ? ev : 0;

            var droppedWeighted = droppedVal * weight;
            var equippedWeighted = equippedVal * weight;

            droppedTotal += droppedWeighted;
            equippedTotal += equippedWeighted;

            var displayName = _statDefs.Definitions.Categories.TryGetValue(key, out var catDef)
                ? catDef.DisplayName
                : key;

            categoryScores.Add(new CategoryScore
            {
                Category = category,
                DisplayName = displayName,
                DroppedScore = droppedWeighted,
                EquippedScore = equippedWeighted,
                Difference = droppedWeighted - equippedWeighted
            });
        }

        var statDiffs = BuildStatDiffs(dropped, equipped);
        var unscoredMods = statDiffs
            .Where(d => d.IsUnscored)
            .Select(d => d.ModLine)
            .ToImmutableList();

        var percentChange = equippedTotal > 0
            ? ((droppedTotal - equippedTotal) / equippedTotal) * 100.0
            : (droppedTotal > 0 ? 100.0 : 0.0);

        var verdict = percentChange switch
        {
            > SidegradeThreshold => ComparisonVerdict.Upgrade,
            < -SidegradeThreshold => ComparisonVerdict.Downgrade,
            _ => ComparisonVerdict.Sidegrade
        };

        double? dreamScore = null;
        double? dreamPercent = null;
        if (dreamTarget != null)
        {
            var dreamStats = ExtractCategoryValues(dreamTarget);
            var dreamTotal = 0.0;
            foreach (var category in Enum.GetValues<StatCategory>())
            {
                var key = category.ToString();
                var weight = weights.TryGetValue(key, out var w) ? w : 0;
                var val = dreamStats.TryGetValue(category, out var v) ? v : 0;
                dreamTotal += val * weight;
            }
            dreamScore = dreamTotal;
            dreamPercent = dreamTotal > 0 ? (droppedTotal / dreamTotal) * 100.0 : null;
        }

        return new ComparisonResult
        {
            DroppedTotalScore = Math.Round(droppedTotal, 1),
            EquippedTotalScore = Math.Round(equippedTotal, 1),
            Verdict = verdict,
            PercentChange = Math.Round(percentChange, 1),
            CategoryScores = categoryScores.ToImmutableList(),
            StatDiffs = statDiffs,
            UnscoredMods = unscoredMods,
            DreamTargetScore = dreamScore.HasValue ? Math.Round(dreamScore.Value, 1) : null,
            DreamPercentOfTarget = dreamPercent.HasValue ? Math.Round(dreamPercent.Value, 1) : null
        };
    }

    private Dictionary<StatCategory, double> ExtractCategoryValues(ItemData item)
    {
        var values = new Dictionary<StatCategory, double>();

        var allMods = item.ImplicitMods.AddRange(item.ExplicitMods);
        foreach (var mod in allMods)
        {
            var result = _statDefs.CategorizeAndExtract(mod);
            if (result == null) continue;

            var (category, value) = result.Value;
            values[category] = values.TryGetValue(category, out var existing) ? existing + value : value;
        }

        if (item.Armour > 0)
            values[StatCategory.PhysicalDefense] = values.GetValueOrDefault(StatCategory.PhysicalDefense) + item.Armour;
        if (item.Evasion > 0)
            values[StatCategory.PhysicalDefense] = values.GetValueOrDefault(StatCategory.PhysicalDefense) + item.Evasion;
        if (item.EnergyShield > 0)
            values[StatCategory.LifeAndES] = values.GetValueOrDefault(StatCategory.LifeAndES) + item.EnergyShield;

        return values;
    }

    private ImmutableList<StatDiff> BuildStatDiffs(ItemData dropped, ItemData equipped)
    {
        var diffs = new List<StatDiff>();

        var allDroppedMods = dropped.ImplicitMods.AddRange(dropped.ExplicitMods);
        foreach (var mod in allDroppedMods)
        {
            var result = _statDefs.CategorizeAndExtract(mod);
            diffs.Add(new StatDiff
            {
                ModLine = mod,
                Value = result?.Value ?? 0,
                Category = result?.Category,
                IsUnscored = result == null,
                IsGain = true
            });
        }

        var allEquippedMods = equipped.ImplicitMods.AddRange(equipped.ExplicitMods);
        foreach (var mod in allEquippedMods)
        {
            var result = _statDefs.CategorizeAndExtract(mod);
            if (result != null)
            {
                var hasDroppedMod = allDroppedMods.Any(dm =>
                {
                    var dr = _statDefs.CategorizeAndExtract(dm);
                    return dr?.Category == result.Value.Category;
                });
                if (!hasDroppedMod)
                {
                    diffs.Add(new StatDiff
                    {
                        ModLine = mod,
                        Value = -(result.Value.Value),
                        Category = result.Value.Category,
                        IsUnscored = false,
                        IsGain = false
                    });
                }
            }
        }

        return diffs.ToImmutableList();
    }
}
