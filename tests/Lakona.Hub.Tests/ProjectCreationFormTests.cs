using Lakona.ProjectSystem;
using Xunit;

namespace Lakona.Hub.Tests;

public sealed class ProjectCreationFormTests
{
    [Fact]
    public void Defaults_FavorTheHubCreationExperience()
    {
        var form = Form(HubLanguage.SimplifiedChinese);

        Assert.Equal("unity", form.SelectedClient.Id);
        Assert.Equal("2022", form.SelectedClientVersion?.Id);
        Assert.Equal("websocket", form.SelectedTransport.Id);
        Assert.Equal("memorypack", form.SelectedSerializer.Id);
        Assert.Equal("openupm", form.SelectedNuGetForUnitySource.Id);
        Assert.Equal("memory", form.SelectedMembershipProvider.Id);
        Assert.True(form.CanCreate);
    }

    [Fact]
    public void ConfigurationHints_DescribeTheCurrentSelection()
    {
        var form = Form(HubLanguage.English);

        form.SelectedTransport = form.TransportOptions.Single(option => option.Id == "kcp");
        form.SelectedSerializer = form.SerializerOptions.Single(option => option.Id == "json");

        Assert.Contains("low-latency", form.TransportHint, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Human-readable", form.SerializerHint, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void SelectingConsole_RemovesVersionAndDisablesNuGetForUnity()
    {
        var form = Form(HubLanguage.SimplifiedChinese);
        form.SelectedClient = ProjectCreationForm.Console;

        Assert.Empty(form.ClientVersionOptions);
        Assert.Null(form.SelectedClientVersion);
        Assert.False(form.HasClientVersion);
        Assert.False(form.UsesNuGetForUnity);
        Assert.True(form.CanCreate);
    }

    [Fact]
    public void CreateRequest_MapsHubFormToSharedProjectSystemContract()
    {
        var form = Form(HubLanguage.SimplifiedChinese);
        form.ProjectName = " HubGame ";
        form.OutputDirectory = Path.GetTempPath();
        form.SelectedClient = ProjectCreationForm.Console;
        form.SelectedTransport = new ProjectCreationChoice("websocket", "WebSocket");
        form.SelectedSerializer = new ProjectCreationChoice("json", "JSON");
        form.SelectedMembershipProvider = form.MembershipProviderOptions.Single(option => option.Id == "redis");

        var request = form.CreateRequest();

        Assert.Equal("HubGame", request.ProjectName);
        Assert.Equal(Path.GetTempPath().Trim(), request.OutputPath);
        Assert.Equal(LakonaClientEngine.Console, request.ClientEngine);
        Assert.Null(request.ClientEngineVersion);
        Assert.Equal(LakonaTransport.WebSocket, request.Transport);
        Assert.Equal(LakonaSerializer.Json, request.Serializer);
        Assert.Equal(LakonaDeploymentProfile.None, request.DeploymentProfile);
        Assert.Equal(LakonaMembershipProvider.Redis, request.MembershipProvider);
    }

    [Fact]
    public void SelectingEngine_UsesItsSupportedVersion()
    {
        var form = Form(HubLanguage.SimplifiedChinese);
        form.SelectedClient = ProjectCreationForm.Tuanjie;

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
        var form = Form(HubLanguage.SimplifiedChinese);
        form.ProjectName = projectName;

        Assert.False(form.CanCreate);
    }

    [Fact]
    public void CreatingState_PreventsDuplicateSubmission()
    {
        var form = Form(HubLanguage.SimplifiedChinese);
        form.IsCreating = true;

        Assert.False(form.CanCreate);
    }

    [Fact]
    public void CreateRequest_AllowsValidConfigurationAfterSubmissionStarts()
    {
        var form = Form(HubLanguage.English);
        form.IsCreating = true;

        var request = form.CreateRequest();

        Assert.Equal("MyGame", request.ProjectName);
    }

    [Fact]
    public void ManualLanguageSwitch_UpdatesValidationHintsAndChoices()
    {
        var localization = new HubLocalization(HubLanguage.SimplifiedChinese);
        var form = new ProjectCreationForm(localization)
        {
            ProjectName = ""
        };

        Assert.Equal("请输入项目名称", form.ValidationMessage);

        localization.SetLanguage(HubLanguage.English);

        Assert.Equal("Enter a project name", form.ValidationMessage);
        Assert.Equal("Choose the editor version used by the client", form.ClientVersionHint);
        Assert.Equal("unity", form.SelectedClient.Id);
        Assert.Equal("websocket", form.SelectedTransport.Id);
    }

    [Fact]
    public void TransientEmptyComboBoxSelections_DoNotCorruptTheDraft()
    {
        var form = Form(HubLanguage.English);
        var expected = form.CaptureDraft();

        form.SelectedClient = null!;
        form.SelectedTransport = null!;
        form.SelectedSerializer = null!;
        form.SelectedNuGetForUnitySource = null!;
        form.SelectedMembershipProvider = null!;

        Assert.Equal(expected, form.CaptureDraft());
    }

    [Fact]
    public void Draft_RestoresProjectNameOutputLocationAndEveryEditableChoice()
    {
        var form = Form(HubLanguage.English);
        var draft = new HubCreationDraft(
            "SavedGame",
            Path.GetTempPath(),
            "godot",
            "4.6",
            "tcp",
            "json",
            "embedded",
            "mysql");

        form.ApplyDraft(draft);

        Assert.Equal("SavedGame", form.ProjectName);
        Assert.Equal(Path.GetTempPath(), form.OutputDirectory);
        Assert.Equal(draft, form.CaptureDraft());
    }

    private static ProjectCreationForm Form(HubLanguage language) =>
        new(new HubLocalization(language));
}
