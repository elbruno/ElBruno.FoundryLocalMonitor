using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using Microsoft.Extensions.Logging;

namespace ElBruno.FoundryLocalMonitor.Cli;

public class FoundryCliRunner
{
    private readonly ILogger<FoundryCliRunner>? _logger;

    public FoundryCliRunner(ILogger<FoundryCliRunner>? logger = null)
    {
        _logger = logger;
    }

    public async Task<string?> RunAsync(string arguments, CancellationToken ct = default)
    {
        try
        {
            var psi = new ProcessStartInfo("foundry", arguments)
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var process = Process.Start(psi);
            if (process == null) return null;

            var output = await process.StandardOutput.ReadToEndAsync(ct);
            await process.WaitForExitAsync(ct);
            return output;
        }
        catch (Exception ex) when (ex is FileNotFoundException or Win32Exception)
        {
            _logger?.LogWarning("foundry CLI not found on PATH");
            return null;
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Error running foundry {Arguments}", arguments);
            return null;
        }
    }

    public async Task<bool> IsFoundryInstalledAsync()
    {
        var result = await RunAsync("--version");
        return result != null;
    }
}
