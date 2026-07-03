using ElBruno.FoundryLocalMonitor.Foundry;
using ElBruno.FoundryLocalMonitor.Models;
using FluentAssertions;
using Xunit;

namespace ElBruno.FoundryLocalMonitor.Tests;

public class FoundryCliParserTests
{
    #region ParseServiceStatus

    [Fact]
    public void ParseServiceStatus_NullInput_ReturnsNotRunning()
    {
        var result = FoundryCliParser.ParseServiceStatus(null);
        result.IsRunning.Should().BeFalse();
        result.Endpoint.Should().BeNull();
    }

    [Fact]
    public void ParseServiceStatus_EmptyString_ReturnsNotRunning()
    {
        var result = FoundryCliParser.ParseServiceStatus("");
        result.IsRunning.Should().BeFalse();
    }

    [Fact]
    public void ParseServiceStatus_RunningOutput_ReturnsRunningTrue()
    {
        var output = "Foundry Local service is running at http://localhost:5273";
        var result = FoundryCliParser.ParseServiceStatus(output);
        result.IsRunning.Should().BeTrue();
    }

    [Fact]
    public void ParseServiceStatus_StartedOutput_ReturnsRunningTrue()
    {
        var output = "Service started successfully";
        var result = FoundryCliParser.ParseServiceStatus(output);
        result.IsRunning.Should().BeTrue();
    }

    [Fact]
    public void ParseServiceStatus_StoppedOutput_ReturnsNotRunning()
    {
        var output = "Foundry Local service is not installed or stopped.";
        var result = FoundryCliParser.ParseServiceStatus(output);
        result.IsRunning.Should().BeFalse();
    }

    [Fact]
    public void ParseServiceStatus_ExtractsEndpointUrl()
    {
        var output = "Service running at http://localhost:5273";
        var result = FoundryCliParser.ParseServiceStatus(output);
        result.Endpoint.Should().Be("http://localhost:5273");
    }

    [Fact]
    public void ParseServiceStatus_NoUrlInOutput_EndpointIsNull()
    {
        var output = "Service is running";
        var result = FoundryCliParser.ParseServiceStatus(output);
        result.Endpoint.Should().BeNull();
    }

    #endregion

    #region ParseLoadedModels

    [Fact]
    public void ParseLoadedModels_NullInput_ReturnsEmpty()
    {
        var result = FoundryCliParser.ParseLoadedModels(null);
        result.Should().BeEmpty();
    }

    [Fact]
    public void ParseLoadedModels_EmptyInput_ReturnsEmpty()
    {
        var result = FoundryCliParser.ParseLoadedModels("");
        result.Should().BeEmpty();
    }

    [Fact]
    public void ParseLoadedModels_NoModelsOutput_ReturnsEmpty()
    {
        var output = "No models currently loaded.";
        var result = FoundryCliParser.ParseLoadedModels(output);
        result.Should().BeEmpty();
    }

    [Fact]
    public void ParseLoadedModels_SingleModel_ReturnsSingleModel()
    {
        var output = """
            ModelId          Alias       Device
            phi-3-mini-4k    phi-3-mini  CPU
            """;
        var result = FoundryCliParser.ParseLoadedModels(output);
        result.Should().HaveCount(1);
        result[0].ModelId.Should().Be("phi-3-mini-4k");
        result[0].Alias.Should().Be("phi-3-mini");
        result[0].Device.Should().Be("CPU");
        result[0].IsLoaded.Should().BeTrue();
    }

    [Fact]
    public void ParseLoadedModels_MultipleModels_ReturnsAll()
    {
        var output = """
            ModelId              Alias         Device
            phi-3-mini-4k        phi-3-mini    CPU
            mistral-7b-instruct  mistral-7b    GPU
            """;
        var result = FoundryCliParser.ParseLoadedModels(output);
        result.Should().HaveCount(2);
    }

    [Fact]
    public void ParseLoadedModels_HeaderOnlyOutput_ReturnsEmpty()
    {
        var output = "ModelId   Alias   Device";
        var result = FoundryCliParser.ParseLoadedModels(output);
        result.Should().BeEmpty();
    }

    #endregion
}
