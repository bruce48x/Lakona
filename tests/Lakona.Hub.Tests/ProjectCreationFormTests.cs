using Lakona.ProjectSystem;
using Xunit;

namespace Lakona.Hub.Tests;

public sealed class ProjectCreationFormTests
{
    [Fact]
    public void Defaults_MatchGeneratorDefaults()
    {
        var form = new ProjectCreationForm();

        Assert.Equal("unity", form.SelectedClient.Id);
        Assert.Equal("2022", form.SelectedClientVersion?.Id);
        Assert.Equal("kcp", form.SelectedTransport.Id);
        Assert.Equal("memorypack", form.SelectedSerializer.Id);
        Assert.Equal("none", form.SelectedPersistence.Id);
        Assert.Equal("embedded", form.SelectedNuGetForUnitySource.Id);
        Assert.Equal("none", form.SelectedDeploymentProfile.Id);
        Assert.True(form.CanCreate);
    }

    [Fact]
    public void SelectingConsole_RemovesVersionAndDisablesNuGetForUnity()
    {
        var form = new ProjectCreationForm
        {
            SelectedClient = ProjectCreationForm.Console
        };

        Assert.Empty(form.ClientVersionOptions);
        Assert.Null(form.SelectedClientVersion);
        Assert.False(form.HasClientVersion);
        Assert.False(form.UsesNuGetForUnity);
        Assert.True(form.CanCreate);
    }

    [Fact]
    public void CreateRequest_MapsHubFormToSharedProjectSystemContract()
    {
        var form = new ProjectCreationForm
        {
            ProjectName = " HubGame ",
            OutputDirectory = Path.GetTempPath(),
            SelectedClient = ProjectCreationForm.Console,
            SelectedTransport = new ProjectCreationChoice("websocket", "WebSocket"),
            SelectedSerializer = new ProjectCreationChoice("json", "JSON"),
            SelectedPersistence = new ProjectCreationChoice("postgres", "PostgreSQL"),
            SelectedDeploymentProfile = new ProjectCreationChoice("compose", "Docker Compose")
        };

        var request = form.CreateRequest();

        Assert.Equal("HubGame", request.ProjectName);
        Assert.Equal(Path.GetTempPath().Trim(), request.OutputPath);
        Assert.Equal(LakonaClientEngine.Console, request.ClientEngine);
        Assert.Null(request.ClientEngineVersion);
        Assert.Equal(LakonaTransport.WebSocket, request.Transport);
        Assert.Equal(LakonaSerializer.Json, request.Serializer);
        Assert.Equal(LakonaPersistence.Postgres, request.Persistence);
        Assert.Equal(LakonaDeploymentProfile.Compose, request.DeploymentProfile);
    }

    [Fact]
    public void SelectingEngine_UsesItsSupportedVersion()
    {
        var form = new ProjectCreationForm
        {
            SelectedClient = ProjectCreationForm.Tuanjie
        };

        Assert.Equal("1.6.7", Assert.Single(form.ClientVersionOptions).Id);

        form.SelectedClient = ProjectCreationForm.Godot;

        Assert.Equal("4.6", Assert.Single(form.ClientVersionOptions).Id);
    }

    [Theory]
    [InlineData("")]
    [InlineData("..")]
    [InlineData("bad/name")]
    public void InvalidProjectName_PreventsCreation(string projectName)
    {
        var form = new ProjectCreationForm
        {
            ProjectName = projectName
        };

        Assert.False(form.CanCreate);
    }

    [Fact]
    public void CreatingState_PreventsDuplicateSubmission()
    {
        var form = new ProjectCreationForm
        {
            IsCreating = true
        };

        Assert.False(form.CanCreate);
    }
}
