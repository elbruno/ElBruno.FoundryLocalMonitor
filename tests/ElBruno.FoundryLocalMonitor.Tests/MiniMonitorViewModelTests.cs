using ElBruno.FoundryLocalMonitor.Configuration;
using ElBruno.FoundryLocalMonitor.Models;
using ElBruno.FoundryLocalMonitor.Services;
using ElBruno.FoundryLocalMonitor.ViewModels;
using FluentAssertions;
using Moq;
using Xunit;

namespace ElBruno.FoundryLocalMonitor.Tests;

public class MiniMonitorViewModelTests
{
    private readonly Mock<IFoundryService> _serviceMock;
    private readonly MiniMonitorViewModel _viewModel;

    public MiniMonitorViewModelTests()
    {
        _serviceMock = new Mock<IFoundryService>();
        _serviceMock.SetupGet(s => s.IsServiceRunning).Returns(false);
        _serviceMock.SetupGet(s => s.IsCliInstalled).Returns(true);
        _serviceMock.SetupGet(s => s.LoadedModels).Returns([]);
        _viewModel = new MiniMonitorViewModel(_serviceMock.Object, new AppSettings());
    }

    [Fact]
    public void Constructor_DefaultStatusText_IsChecking()
    {
        _viewModel.StatusText.Should().Be("Checking\u2026");
    }

    [Fact]
    public void Constructor_DefaultCurrentModel_IsNoModelLoaded()
    {
        _viewModel.CurrentModel.Should().Be("No model loaded");
    }

    [Fact]
    public void Constructor_DefaultIsRunning_IsFalse()
    {
        _viewModel.IsRunning.Should().BeFalse();
    }

    [Fact]
    public void Constructor_CliInstalled_ShowCliWarningIsFalse()
    {
        _viewModel.ShowCliWarning.Should().BeFalse();
    }

    [Fact]
    public void CliAvailabilityChanged_NotInstalled_ShowsWarning()
    {
        _serviceMock.Raise(s => s.CliAvailabilityChanged += null, _serviceMock.Object, false);

        _viewModel.ShowCliWarning.Should().BeTrue();
        _viewModel.StatusText.Should().Be("CLI required");
    }

    [Fact]
    public void ServiceStatusChanged_Running_UpdatesIsRunningAndStatusText()
    {
        _serviceMock.Raise(s => s.ServiceStatusChanged += null, _serviceMock.Object, true);

        _viewModel.IsRunning.Should().BeTrue();
        _viewModel.StatusText.Should().Be("Running");
    }

    [Fact]
    public void ServiceStatusChanged_Stopped_UpdatesIsRunningAndStatusText()
    {
        _serviceMock.Raise(s => s.ServiceStatusChanged += null, _serviceMock.Object, true);
        _serviceMock.Raise(s => s.ServiceStatusChanged += null, _serviceMock.Object, false);

        _viewModel.IsRunning.Should().BeFalse();
        _viewModel.StatusText.Should().Be("Stopped");
    }

    [Fact]
    public void ModelStateChanged_ModelLoaded_UpdatesCurrentModel()
    {
        var model = new FoundryModel("phi-3-mini-4k", "phi-3-mini", "CPU", true);
        _serviceMock.SetupGet(s => s.LoadedModels).Returns([model]);
        _serviceMock.Raise(s => s.ModelStateChanged += null, _serviceMock.Object,
            new ModelStateChange(model, ModelChangeType.Loaded, DateTime.Now));

        _viewModel.CurrentModel.Should().Be("phi-3-mini [CPU]");
    }

    [Fact]
    public void ModelStateChanged_ModelUnloaded_ResetsCurrentModel()
    {
        var model = new FoundryModel("phi-3-mini-4k", "phi-3-mini", "CPU", true);

        _serviceMock.SetupGet(s => s.LoadedModels).Returns([model]);
        _serviceMock.Raise(s => s.ModelStateChanged += null, _serviceMock.Object,
            new ModelStateChange(model, ModelChangeType.Loaded, DateTime.Now));

        _serviceMock.SetupGet(s => s.LoadedModels).Returns([]);
        _serviceMock.Raise(s => s.ModelStateChanged += null, _serviceMock.Object,
            new ModelStateChange(model, ModelChangeType.Unloaded, DateTime.Now));

        _viewModel.CurrentModel.Should().Be("No model loaded");
    }
}
