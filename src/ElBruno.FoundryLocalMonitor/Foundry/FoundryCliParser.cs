using ElBruno.FoundryLocalMonitor.Models;
using System.Text.RegularExpressions;

namespace ElBruno.FoundryLocalMonitor.Foundry;

public static partial class FoundryCliParser
{
    /// <summary>Parses output of: foundry service status</summary>
    public static FoundryServiceStatus ParseServiceStatus(string? output)
    {
        if (string.IsNullOrWhiteSpace(output))
            return new FoundryServiceStatus(false, null, null);

        var isRunning = output.Contains("running", StringComparison.OrdinalIgnoreCase)
                     || output.Contains("started", StringComparison.OrdinalIgnoreCase);

        var endpointMatch = EndpointRegex().Match(output);
        var endpoint = endpointMatch.Success ? endpointMatch.Value : null;

        var versionMatch = VersionRegex().Match(output);
        var version = versionMatch.Success ? versionMatch.Groups[1].Value : null;

        return new FoundryServiceStatus(isRunning, endpoint, version);
    }

    /// <summary>Parses output of: foundry service ps</summary>
    public static IReadOnlyList<FoundryModel> ParseLoadedModels(string? output)
    {
        if (string.IsNullOrWhiteSpace(output))
            return [];

        if (output.Contains("No models", StringComparison.OrdinalIgnoreCase))
            return [];

        var lines = output.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        var models = new List<FoundryModel>();

        foreach (var line in lines.Skip(1)) // skip header
        {
            var trimmed = line.Trim();
            if (string.IsNullOrEmpty(trimmed) || trimmed.StartsWith('-')) continue;

            // Columns: ModelId  Alias  Device  Status
            var parts = trimmed.Split(Array.Empty<char>(), StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length >= 1)
            {
                var modelId = parts[0];
                var alias = parts.Length >= 2 ? parts[1] : modelId;
                var device = parts.Length >= 3 ? parts[2] : null;
                models.Add(new FoundryModel(modelId, alias, device, true));
            }
        }

        return models;
    }

    [GeneratedRegex(@"https?://[^\s]+")]
    private static partial Regex EndpointRegex();

    [GeneratedRegex(@"version[:\s]+(\S+)", RegexOptions.IgnoreCase)]
    private static partial Regex VersionRegex();
}
