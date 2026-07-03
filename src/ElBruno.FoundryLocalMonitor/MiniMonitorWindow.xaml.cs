using System.Windows;
using ElBruno.FoundryLocalMonitor.ViewModels;

namespace ElBruno.FoundryLocalMonitor;

public partial class MiniMonitorWindow : Window
{
    public MiniMonitorWindow(MiniMonitorViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
        PositionToBottomRight();
    }

    private void PositionToBottomRight()
    {
        var workArea = SystemParameters.WorkArea;
        Left = workArea.Right - Width - 16;
        Top = workArea.Bottom - Height - 16;
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e) => Hide();

    protected override void OnMouseLeftButtonDown(System.Windows.Input.MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);
        DragMove();
    }
}
