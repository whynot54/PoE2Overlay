using System.Collections.Immutable;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;
using PoE2Overlay.Models;

namespace PoE2Overlay.Services;

public sealed class StatDefinitionService
{
    private const string LocalPath = "Data/stat-definitions.json";
    private const string RemoteUrl = "https://raw.githubusercontent.com/whynot54/PoE2Overlay/main/PoE2Overlay/Data/stat-definitions.json";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private StatDefinitions _definitions = new();
    private ImmutableDictionary<string, List<(Regex Pattern, string Type)>> _compiledPatterns
        = ImmutableDictionary<string, List<(Regex, string)>>.Empty;

    public StatDefinitions Definitions => _definitions;
    public string Version => _definitions.Version;
    public string LastUpdated => _definitions.LastUpdated;

    public void Load()
    {
        var path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, LocalPath);
        if (!File.Exists(path))
            return;

        var json = File.ReadAllText(path);
        var defs = JsonSerializer.Deserialize<StatDefinitions>(json, JsonOptions);
        if (defs != null)
        {
            _definitions = defs;
            CompilePatterns();
        }
    }

    public async Task<bool> UpdateFromRemoteAsync(CancellationToken ct = default)
    {
        using var http = new HttpClient();
        http.DefaultRequestHeaders.UserAgent.ParseAdd("PoE2Overlay/1.0");

        var json = await http.GetStringAsync(RemoteUrl, ct);
        var defs = JsonSerializer.Deserialize<StatDefinitions>(json, JsonOptions);
        if (defs == null)
            return false;

        var path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, LocalPath);
        await File.WriteAllTextAsync(path, json, ct);

        _definitions = defs;
        CompilePatterns();
        return true;
    }

    public (StatCategory Category, double Value)? CategorizeAndExtract(string modLine)
    {
        foreach (var (categoryKey, patterns) in _compiledPatterns)
        {
            foreach (var (pattern, type) in patterns)
            {
                var match = pattern.Match(modLine);
                if (!match.Success) continue;

                var category = Enum.Parse<StatCategory>(categoryKey);
                var value = ExtractNumericValue(modLine);
                return (category, value);
            }
        }

        return null;
    }

    private void CompilePatterns()
    {
        var builder = ImmutableDictionary.CreateBuilder<string, List<(Regex, string)>>();

        foreach (var (key, categoryDef) in _definitions.Categories)
        {
            var list = new List<(Regex, string)>();
            foreach (var p in categoryDef.Patterns)
            {
                list.Add((new Regex(p.Regex, RegexOptions.Compiled | RegexOptions.IgnoreCase), p.Type));
            }
            builder[key] = list;
        }

        _compiledPatterns = builder.ToImmutable();
    }

    private static double ExtractNumericValue(string modLine)
    {
        var rangeMatch = Regex.Match(modLine, @"(\d+\.?\d*)\s+to\s+(\d+\.?\d*)");
        if (rangeMatch.Success)
        {
            var low = double.Parse(rangeMatch.Groups[1].Value);
            var high = double.Parse(rangeMatch.Groups[2].Value);
            return (low + high) / 2.0;
        }

        var singleMatch = Regex.Match(modLine, @"[+-]?(\d+\.?\d*)");
        if (singleMatch.Success)
        {
            var val = double.Parse(singleMatch.Groups[1].Value);
            if (modLine.Contains('-') && !modLine.Contains("to"))
                val = -val;
            return val;
        }

        return 0;
    }
}
