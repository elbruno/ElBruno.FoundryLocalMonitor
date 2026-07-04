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

        if (output.Contains("No models", StringComparison.OrdinalIgnoreCase)
         || output.Contains("no model", StringComparison.OrdinalIgnoreCase))
            return [];

        var lines = output.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        var models = new List<FoundryModel>();

        // Detect header line to find column positions (handles variable column layouts)
        // Known layouts:
        //   Name  Alias  Provider  Generator  IsLoaded  Port
        //   ModelId  Alias  Device  Status
        int nameCol = 0, aliasCol = 1, deviceCol = -1;

        var headerLine = lines.FirstOrDefault(l =>
            l.Contains("Name", StringComparison.OrdinalIgnoreCase) ||
            l.Contains("ModelId", StringComparison.OrdinalIgnoreCase) ||
            l.Contains("Model", StringComparison.OrdinalIgnoreCase));

        // Determine alias column index from header
        if (headerLine != null)
        {
            var headers = headerLine.Split(Array.Empty<char>(), StringSplitOptions.RemoveEmptyEntries);
            for (int i = 0; i < headers.Length; i++)
            {
                if (headers[i].Equals("Alias", StringComparison.OrdinalIgnoreCase)) aliasCol = i;
                if (headers[i].Equals("Device", StringComparison.OrdinalIgnoreCase) ||
                    headers[i].Equals("Provider", StringComparison.OrdinalIgnoreCase)) deviceCol = i;
            }
        }

        foreach (var line in lines.Skip(1))
        {
            var trimmed = line.Trim();
            // Skip header/separator lines
            if (string.IsNullOrEmpty(trimmed) || trimmed.StartsWith('-') || trimmed.StartsWith('=')) continue;
            // Skip the header row itself if it reappears
            if (trimmed.StartsWith("Name", StringComparison.OrdinalIgnoreCase) ||
                trimmed.StartsWith("ModelId", StringComparison.OrdinalIgnoreCase)) continue;

            var parts = trimmed.Split(Array.Empty<char>(), StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 1) continue;

            var modelId = parts[nameCol];
            var alias = parts.Length > aliasCol ? parts[aliasCol] : modelId;
            var device = deviceCol >= 0 && parts.Length > deviceCol ? parts[deviceCol] : null;

            models.Add(new FoundryModel(modelId, alias, device, true));
        }

        return models;
    }

    [GeneratedRegex(@"https?://[^\s]+")]
    private static partial Regex EndpointRegex();

    [GeneratedRegex(@"version[:\s]+(\S+)", RegexOptions.IgnoreCase)]
    private static partial Regex VersionRegex();
}
