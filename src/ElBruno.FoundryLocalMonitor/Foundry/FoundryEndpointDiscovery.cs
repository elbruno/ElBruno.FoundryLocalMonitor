using System.Diagnostics;
using System.Net;
using System.Net.Http;
using System.Net.NetworkInformation;
using System.Text.Json;
using ElBruno.FoundryLocalMonitor.Models;
using Microsoft.Extensions.Logging;

namespace ElBruno.FoundryLocalMonitor.Foundry;

/// <summary>
/// Discovers all Foundry Local API endpoints on localhost by scanning active TCP listeners
/// in parallel. Works for any app using the Foundry Local SDK — the foundry CLI alone
/// is not sufficient because SDK-managed apps use dynamic, unconfigured ports.
///
/// Discovery strategy:
///  1. Find the foundry daemon via the Inference.Service.Agent process (O(1)).
///  2. Probe all localhost TCP listeners in parallel (800 ms timeout each).
///  3. Any port that returns {"object":"list"} from GET /v1/models is a Foundry endpoint.
/// </summary>
public class FoundryEndpointDiscovery
{
    private readonly HttpClient _http;
    private readonly ILogger<FoundryEndpointDiscovery>? _logger;

    // Known daemon process name — Foundry Local's inference backend
    private const string DaemonProcessName = "Inference.Service.Agent";

    // Timeout per port probe — short enough to not block, long enough for local loopback
    private static readonly TimeSpan ProbeTimeout = TimeSpan.FromMilliseconds(800);

    public FoundryEndpointDiscovery(HttpClient http, ILogger<FoundryEndpointDiscovery>? logger = null)
    {
        _http = http;
        _logger = logger;
    }

    /// <summary>
    /// Scans all 127.0.0.1 listeners in parallel and returns every endpoint
    /// that responds to GET /v1/models with a valid Foundry API response.
    ///
    /// Typical results include:
    ///  - The foundry daemon (dynamic port, e.g. 62652)
    ///  - SDK internal server (default 55588, configurable)
    ///  - Aspire DCP proxied services (e.g. 5099, 5100, 5101)
    /// </summary>
    public async Task<IReadOnlyList<FoundryEndpoint>> DiscoverAsync(CancellationToken ct = default)
    {
        var ports = GetLocalListeningPorts();
        if (ports.Count == 0) return [];

        _logger?.LogDebug("Probing {Count} localhost listeners for Foundry API", ports.Count);

        // Fan out all probes in parallel
        var tasks = ports.Select(p => ProbePortAsync(p, ct)).ToArray();
        var results = await Task.WhenAll(tasks);

        var endpoints = results.Where(r => r != null).Cast<FoundryEndpoint>().ToList();
        _logger?.LogDebug("Discovered {Count} Foundry endpoint(s): {Ports}",
            endpoints.Count, string.Join(", ", endpoints.Select(e => e.Port)));

        return endpoints;
    }

    /// <summary>
    /// Fast-path: returns the port the Inference.Service.Agent daemon is listening on,
    /// or null if the daemon is not running. Uses process TCP table — no HTTP needed.
    /// </summary>
    public static int? GetDaemonPort()
    {
        try
        {
            var daemon = Process.GetProcessesByName(DaemonProcessName).FirstOrDefault();
            if (daemon == null) return null;

            var props = IPGlobalProperties.GetIPGlobalProperties();
            var listeners = props.GetActiveTcpListeners();
            // The daemon listens on 127.0.0.1; find the first port owned by its PID
            // via TcpConnectionInformation (can't filter by PID from managed API alone).
            // Fall back to full TCP table lookup.
            return GetListeningPortForPid(daemon.Id);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>Returns true if the foundry daemon process is alive.</summary>
    public static bool IsDaemonRunning()
        => Process.GetProcessesByName(DaemonProcessName).Length > 0;

    // --- private helpers ---

    private async Task<FoundryEndpoint?> ProbePortAsync(int port, CancellationToken ct)
    {
        var url = $"http://127.0.0.1:{port}/v1/models";
        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(ProbeTimeout);

            var json = await _http.GetStringAsync(url, cts.Token);
            var resp = JsonSerializer.Deserialize<V1ModelsResponse>(json, JsonOptions);
            if (resp?.Object != "list") return null;

            var models = resp.Data?.Select(ParseModel).ToList() ?? [];
            var processName = GetProcessNameForPort(port);

            _logger?.LogDebug("Foundry API at :{Port} ({Process}) — {Count} model(s)",
                port, processName, models.Count);

            return new FoundryEndpoint(
                BaseUrl: $"http://127.0.0.1:{port}",
                Port: port,
                ProcessName: processName,
                Models: models,
                IsDaemon: port == (GetDaemonPort() ?? -1));
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Returns all ports that have a TCP listener on 127.0.0.1.
    /// Uses System.Net.NetworkInformation which is fast (kernel TCP table read).
    /// </summary>
    private static List<int> GetLocalListeningPorts()
    {
        try
        {
            var props = IPGlobalProperties.GetIPGlobalProperties();
            return props.GetActiveTcpListeners()
                .Where(ep => ep.Address.Equals(IPAddress.Loopback)
                          || ep.Address.Equals(IPAddress.IPv6Loopback)
                          || ep.Address.Equals(IPAddress.Any))
                .Select(ep => ep.Port)
                .Distinct()
                .OrderBy(p => p)
                .ToList();
        }
        catch
        {
            return [];
        }
    }

    private static FoundryModel ParseModel(V1ModelEntry m)
    {
        var fullId = m.Id ?? "";
        var noVersion = fullId.Contains(':') ? fullId[..fullId.LastIndexOf(':')] : fullId;

        string[] deviceSuffixes = ["-trtrtx-gpu", "-cuda-gpu", "-generic-gpu", "-generic-cpu",
                                    "-winml-directml", "-winml-cpu", "-directml-gpu", "-cpu", "-gpu"];
        string alias = noVersion;
        string? device = null;
        foreach (var suffix in deviceSuffixes)
        {
            if (noVersion.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
            {
                alias = noVersion[..^suffix.Length];
                device = suffix.Contains("cpu", StringComparison.OrdinalIgnoreCase) ? "CPU" : "GPU";
                break;
            }
        }
        return new FoundryModel(fullId, alias, device, true);
    }

    private static string? GetProcessNameForPort(int port)
    {
        try
        {
            // Use netstat via WMI TcpConnection isn't available; read TCP table via P/Invoke-free approach
            using var proc = new Process
            {
                StartInfo = new ProcessStartInfo("netstat", "-ano")
                {
                    RedirectStandardOutput = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                }
            };
            proc.Start();
            var output = proc.StandardOutput.ReadToEnd();
            proc.WaitForExit(2000);

            foreach (var line in output.Split('\n'))
            {
                if (!line.Contains($":{port} ") && !line.Contains($":{port}\t")) continue;
                if (!line.Contains("LISTENING")) continue;
                var parts = line.Trim().Split([' ', '\t'], StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length >= 5 && int.TryParse(parts[^1], out var pid))
                {
                    return Process.GetProcessById(pid).ProcessName;
                }
            }
        }
        catch { }
        return null;
    }

    private static int? GetListeningPortForPid(int pid)
    {
        try
        {
            using var proc = new Process
            {
                StartInfo = new ProcessStartInfo("netstat", "-ano")
                {
                    RedirectStandardOutput = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                }
            };
            proc.Start();
            var output = proc.StandardOutput.ReadToEnd();
            proc.WaitForExit(2000);

            foreach (var line in output.Split('\n'))
            {
                if (!line.Contains("LISTENING")) continue;
                if (!line.Contains($" {pid}") && !line.EndsWith($"\t{pid}")) continue;
                var parts = line.Trim().Split([' ', '\t'], StringSplitOptions.RemoveEmptyEntries);
                // Format: TCP  127.0.0.1:PORT  0.0.0.0:0  LISTENING  PID
                if (parts.Length >= 5 && parts[^1].Trim() == pid.ToString())
                {
                    var localAddr = parts[1]; // "127.0.0.1:62652"
                    var colonIdx = localAddr.LastIndexOf(':');
                    if (colonIdx >= 0 && int.TryParse(localAddr[(colonIdx + 1)..], out var port))
                        return port;
                }
            }
        }
        catch { }
        return null;
    }

    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    private record V1ModelsResponse(
        [property: System.Text.Json.Serialization.JsonPropertyName("object")] string? Object,
        [property: System.Text.Json.Serialization.JsonPropertyName("data")] List<V1ModelEntry>? Data);

    private record V1ModelEntry(
        [property: System.Text.Json.Serialization.JsonPropertyName("id")] string? Id,
        [property: System.Text.Json.Serialization.JsonPropertyName("owned_by")] string? OwnedBy);
}

/// <summary>A discovered Foundry Local API endpoint on localhost.</summary>
public record FoundryEndpoint(
    string BaseUrl,
    int Port,
    string? ProcessName,
    IReadOnlyList<FoundryModel> Models,
    bool IsDaemon);
