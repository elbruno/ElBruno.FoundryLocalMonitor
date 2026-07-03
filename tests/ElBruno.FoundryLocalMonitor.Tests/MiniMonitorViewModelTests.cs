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
        _serviceMock.SetupGet(s => s.LoadedModels).Returns([]);
        _viewModel = new MiniMonitorViewModel(_serviceMock.Object);
    }

    [Fact]
    public void Constructor_DefaultStatusText_IsStopped()
    {
        _viewModel.StatusText.Should().Be("Stopped");
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
        _serviceMock.Raise(s => s.ModelStateChanged += null, _serviceMock.Object,
            new ModelStateChange(model, ModelChangeType.Loaded, DateTime.Now));

        _viewModel.CurrentModel.Should().Be("phi-3-mini");
    }

    [Fact]
    public void ModelStateChanged_ModelUnloaded_ResetsCurrentModel()
    {
        var model = new FoundryModel("phi-3-mini-4k", "phi-3-mini", "CPU", true);

        _serviceMock.Raise(s => s.ModelStateChanged += null, _serviceMock.Object,
            new ModelStateChange(model, ModelChangeType.Loaded, DateTime.Now));
        _serviceMock.Raise(s => s.ModelStateChanged += null, _serviceMock.Object,
            new ModelStateChange(model, ModelChangeType.Unloaded, DateTime.Now));

        _viewModel.CurrentModel.Should().Be("No model loaded");
    }
}
