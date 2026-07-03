using ElBruno.FoundryLocalMonitor.Models;
using ElBruno.FoundryLocalMonitor.Services;
using ElBruno.FoundryLocalMonitor.ViewModels;
using FluentAssertions;
using Moq;
using Xunit;

namespace ElBruno.FoundryLocalMonitor.Tests;

public class MainWindowViewModelTests
{
    private readonly Mock<IFoundryService> _serviceMock;
    private readonly MainWindowViewModel _viewModel;

    public MainWindowViewModelTests()
    {
        _serviceMock = new Mock<IFoundryService>();
        _serviceMock.SetupGet(s => s.IsServiceRunning).Returns(false);
        _serviceMock.SetupGet(s => s.LoadedModels).Returns([]);
        _viewModel = new MainWindowViewModel(_serviceMock.Object);
    }

    [Fact]
    public void Constructor_DefaultStatusText_IsChecking()
    {
        _viewModel.StatusText.Should().Be("Checking\u2026");
    }

    [Fact]
    public void Constructor_DefaultIsServiceRunning_IsFalse()
    {
        _viewModel.IsServiceRunning.Should().BeFalse();
    }

    [Fact]
    public void ServiceStatusChanged_ServiceStarts_UpdatesRunningAndStatus()
    {
        _serviceMock.Raise(s => s.ServiceStatusChanged += null, _serviceMock.Object, true);

        _viewModel.IsServiceRunning.Should().BeTrue();
        _viewModel.StatusText.Should().Be("Running");
    }

    [Fact]
    public void ServiceStatusChanged_ServiceStops_UpdatesRunningAndStatus()
    {
        _serviceMock.Raise(s => s.ServiceStatusChanged += null, _serviceMock.Object, true);
        _serviceMock.Raise(s => s.ServiceStatusChanged += null, _serviceMock.Object, false);

        _viewModel.IsServiceRunning.Should().BeFalse();
        _viewModel.StatusText.Should().Be("Stopped");
    }

    [Fact]
    public void ModelStateChanged_ModelLoaded_AddsToLoadedModels()
    {
        var model = new FoundryModel("phi-3-mini-4k", "phi-3-mini", "CPU", true);
        var change = new ModelStateChange(model, ModelChangeType.Loaded, DateTime.Now);

        _serviceMock.Raise(s => s.ModelStateChanged += null, _serviceMock.Object, change);

        _viewModel.LoadedModels.Should().ContainSingle()
            .Which.ModelId.Should().Be("phi-3-mini-4k");
    }

    [Fact]
    public void ModelStateChanged_ModelUnloaded_RemovesFromLoadedModels()
    {
        var model = new FoundryModel("phi-3-mini-4k", "phi-3-mini", "CPU", true);

        _serviceMock.Raise(s => s.ModelStateChanged += null, _serviceMock.Object,
            new ModelStateChange(model, ModelChangeType.Loaded, DateTime.Now));
        _serviceMock.Raise(s => s.ModelStateChanged += null, _serviceMock.Object,
            new ModelStateChange(model, ModelChangeType.Unloaded, DateTime.Now));

        _viewModel.LoadedModels.Should().BeEmpty();
    }

    [Fact]
    public void LoadedModels_InitiallyEmpty()
    {
        _viewModel.LoadedModels.Should().BeEmpty();
    }

    [Fact]
    public void AvailableModels_InitiallyEmpty()
    {
        _viewModel.AvailableModels.Should().BeEmpty();
    }
}
