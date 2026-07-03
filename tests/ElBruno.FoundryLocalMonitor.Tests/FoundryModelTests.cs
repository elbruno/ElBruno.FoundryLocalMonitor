using ElBruno.FoundryLocalMonitor.Models;
using FluentAssertions;
using Xunit;

namespace ElBruno.FoundryLocalMonitor.Tests;

public class FoundryModelTests
{
    [Fact]
    public void FoundryModel_Record_EqualityByValue()
    {
        var m1 = new FoundryModel("phi-3-mini-4k", "phi-3-mini", "CPU", true);
        var m2 = new FoundryModel("phi-3-mini-4k", "phi-3-mini", "CPU", true);
        m1.Should().Be(m2);
    }

    [Fact]
    public void FoundryModel_DifferentModelId_NotEqual()
    {
        var m1 = new FoundryModel("phi-3-mini-4k", "phi-3-mini", "CPU", true);
        var m2 = new FoundryModel("mistral-7b", "mistral", "GPU", true);
        m1.Should().NotBe(m2);
    }

    [Fact]
    public void FoundryServiceStatus_IsRunning_CanBeCreated()
    {
        var status = new FoundryServiceStatus(true, "http://localhost:5273", "1.0.0");
        status.IsRunning.Should().BeTrue();
        status.Endpoint.Should().Be("http://localhost:5273");
        status.Version.Should().Be("1.0.0");
    }

    [Fact]
    public void ModelStateChange_LoadedChangeType_IsCorrect()
    {
        var model = new FoundryModel("phi-3-mini-4k", "phi-3-mini", "CPU", true);
        var change = new ModelStateChange(model, ModelChangeType.Loaded, DateTime.Now);
        change.ChangeType.Should().Be(ModelChangeType.Loaded);
        change.Model.Should().Be(model);
    }

    [Fact]
    public void ModelChangeType_HasLoadedAndUnloaded()
    {
        var values = Enum.GetValues<ModelChangeType>();
        values.Should().Contain(ModelChangeType.Loaded);
        values.Should().Contain(ModelChangeType.Unloaded);
    }
}
