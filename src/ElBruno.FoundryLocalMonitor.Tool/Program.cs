#if WINDOWS
using System.Threading;

// WPF requires an STA thread.
var thread = new Thread(() =>
{
    var app = new ElBruno.FoundryLocalMonitor.App();
    app.InitializeComponent();
    app.Run();
});
thread.SetApartmentState(ApartmentState.STA);
thread.Start();
thread.Join();
#else
Console.Error.WriteLine("foundry-monitor: This tool only runs on Windows.");
Console.Error.WriteLine("See: https://github.com/elbruno/ElBruno.FoundryLocalMonitor");
Environment.Exit(1);
#endif
