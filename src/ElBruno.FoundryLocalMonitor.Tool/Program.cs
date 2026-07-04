using System.Diagnostics;

if (!OperatingSystem.IsWindows())
{
    Console.Error.WriteLine("foundrylocalmon: This tool only runs on Windows.");
    Console.Error.WriteLine("See: https://github.com/elbruno/ElBruno.FoundryLocalMonitor");
    return 1;
}

var desktopDir = Path.Combine(AppContext.BaseDirectory, "desktop");
var desktopExe = Path.Combine(desktopDir, "ElBruno.FoundryLocalMonitor.exe");

if (!File.Exists(desktopExe))
{
    Console.Error.WriteLine("Desktop app payload is missing from the tool installation.");
    return 1;
}

using var process = Process.Start(new ProcessStartInfo
{
    FileName = desktopExe,
    WorkingDirectory = desktopDir,
    UseShellExecute = true
});

return process is null ? 1 : 0;
