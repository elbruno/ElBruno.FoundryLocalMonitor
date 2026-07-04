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
