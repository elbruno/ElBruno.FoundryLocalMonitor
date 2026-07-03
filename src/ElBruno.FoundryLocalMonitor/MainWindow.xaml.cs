using System.Windows;
using ElBruno.FoundryLocalMonitor.ViewModels;

namespace ElBruno.FoundryLocalMonitor;

public partial class MainWindow : Window
{
    public MainWindow(MainWindowViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
    }
}
