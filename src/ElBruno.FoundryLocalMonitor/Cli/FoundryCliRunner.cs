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
                CreateNoWindow = true,
                // Ensure emoji / Unicode characters in CLI output are read correctly
                StandardOutputEncoding = System.Text.Encoding.UTF8,
                StandardErrorEncoding = System.Text.Encoding.UTF8,
            };

            using var process = Process.Start(psi);
            if (process == null) return null;

            // Read both streams concurrently — foundry may write to either stdout or stderr
            var stdoutTask = process.StandardOutput.ReadToEndAsync(ct);
            var stderrTask = process.StandardError.ReadToEndAsync(ct);
            await process.WaitForExitAsync(ct);
            var stdout = await stdoutTask;
            var stderr = await stderrTask;

            // Merge; return empty string (not null) when CLI runs but produces no output
            return stdout + stderr;
        }
        catch (Exception ex) when (ex is FileNotFoundException or Win32Exception)
        {
            _logger?.LogWarning("foundry CLI not found on PATH");
            return null;   // null == CLI not on PATH
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
