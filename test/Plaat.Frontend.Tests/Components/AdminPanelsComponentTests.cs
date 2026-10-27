using System.Security.Claims;
using Bunit;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Plaat.Contract.Client;
using Plaat.Contract.Models;
using Plaat.Contract.Models.Authorization;
using Plaat.Frontend.Components.Pages.AdminPanels;
using Shouldly;
using Xunit;

namespace Plaat.Frontend.Tests.Components;

/// <summary>
/// The admin screen's user management, monitored folders and background task panels.
/// </summary>
public sealed class AdminPanelsComponentTests : BunitContext
{
    private readonly IPlaatClient _client = Substitute.For<IPlaatClient>();

    public AdminPanelsComponentTests()
    {
        Services.AddSingleton(_client);
    }

    // ---- users ----

    private IRenderedComponent<ManageUsersPanel> RenderUsers(string currentUser = "admin")
    {
        var principal = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, currentUser)], "test"));

        return Render<ManageUsersPanel>(p => p.AddCascadingValue(Task.FromResult(new AuthenticationState(principal))));
    }

    private void GivenUsers(params UserSummaryDto[] users)
    {
        _client.GetUsers(Arg.Any<CancellationToken>()).Returns(users);
    }

    private static AngleSharp.Dom.IElement Row(IRenderedComponent<ManageUsersPanel> cut, string username)
    {
        return cut.FindAll("tbody tr").Single(r => r.QuerySelector("td")!.TextContent == username);
    }

    [Fact]
    public void Users_AreListedAndTheCurrentAdminCannotRevokeThemselves()
    {
        GivenUsers(new UserSummaryDto(1, "admin", true), new UserSummaryDto(2, "bob", false));

        var cut = RenderUsers();

        var admin = Row(cut, "admin");
        admin.TextContent.ShouldContain("Yes");
        admin.QuerySelectorAll("button")[1].HasAttribute("disabled").ShouldBeTrue();
        var bob = Row(cut, "bob");
        bob.TextContent.ShouldContain("Make admin");
        bob.QuerySelectorAll("button")[1].HasAttribute("disabled").ShouldBeFalse();
    }

    [Fact]
    public void CreateUser_RequiresUsernameAndPassword()
    {
        GivenUsers();
        var cut = RenderUsers();

        cut.Find(".settings-card button").Click();

        cut.Find(".alert-danger").TextContent.ShouldBe("Username and password are required.");
        _client.DidNotReceiveWithAnyArgs().CreateUser(null!, CancellationToken.None);
    }

    [Fact]
    public void CreateUser_SendsTheRequestClearsTheFormAndReloads()
    {
        GivenUsers();
        var cut = RenderUsers();

        cut.FindAll(".settings-card input")[0].Change("carol");
        cut.FindAll(".settings-card input")[1].Change("password123");
        cut.FindAll(".settings-card input")[2].Change(true);
        cut.Find(".settings-card button").Click();

        cut.Find(".alert-success").TextContent.ShouldBe("User 'carol' created successfully.");
        _client.Received(1).CreateUser(
            Arg.Is<CreateUserRequest>(r => r!.Username == "carol" && r.Password == "password123" && r.IsAdmin),
            Arg.Any<CancellationToken>());
        _client.Received(2).GetUsers(Arg.Any<CancellationToken>());
        cut.FindAll(".settings-card input")[0].GetAttribute("value").ShouldBeNullOrEmpty();
    }

    [Fact]
    public void CreateUser_Failure_ShowsAnError()
    {
        GivenUsers();
        _client.CreateUser(Arg.Any<CreateUserRequest>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromException(new HttpRequestException("conflict")));
        var cut = RenderUsers();

        cut.FindAll(".settings-card input")[0].Change("carol");
        cut.FindAll(".settings-card input")[1].Change("password123");
        cut.Find(".settings-card button").Click();

        cut.Find(".alert-danger").TextContent.ShouldBe("Failed to create user.");
    }

    [Fact]
    public void ResetPassword_WithoutAPassword_DoesNothing()
    {
        GivenUsers(new UserSummaryDto(2, "bob", false));
        var cut = RenderUsers();

        Row(cut, "bob").QuerySelectorAll("button")[0].Click();

        _client.DidNotReceiveWithAnyArgs().SetUserPassword(null!, null!, CancellationToken.None);
    }

    [Fact]
    public void ResetPassword_SetsTheNewPassword()
    {
        GivenUsers(new UserSummaryDto(2, "bob", false));
        var cut = RenderUsers();

        cut.Find("tbody input").Change("new-password-1");
        Row(cut, "bob").QuerySelectorAll("button")[0].Click();

        cut.Find(".alert-success").TextContent.ShouldBe("Password for 'bob' updated.");
        _client.Received(1).SetUserPassword("bob", new SetPasswordRequest("new-password-1"), Arg.Any<CancellationToken>());
    }

    [Fact]
    public void ResetPassword_Failure_ShowsAnError()
    {
        GivenUsers(new UserSummaryDto(2, "bob", false));
        _client.SetUserPassword("bob", Arg.Any<SetPasswordRequest>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromException(new HttpRequestException()));
        var cut = RenderUsers();

        cut.Find("tbody input").Change("new-password-1");
        Row(cut, "bob").QuerySelectorAll("button")[0].Click();

        cut.Find(".alert-danger").TextContent.ShouldBe("Failed to reset password for 'bob'.");
    }

    [Fact]
    public void ToggleAdmin_FlipsTheRoleAndReloads()
    {
        GivenUsers(new UserSummaryDto(2, "bob", false));
        var cut = RenderUsers();

        Row(cut, "bob").QuerySelectorAll("button")[1].Click();

        _client.Received(1).SetUserAdmin("bob", true, Arg.Any<CancellationToken>());
        _client.Received(2).GetUsers(Arg.Any<CancellationToken>());
    }

    [Fact]
    public void ToggleAdmin_Failure_ShowsAnError()
    {
        GivenUsers(new UserSummaryDto(2, "bob", true));
        _client.SetUserAdmin("bob", false, Arg.Any<CancellationToken>()).Returns(Task.FromException(new HttpRequestException()));
        var cut = RenderUsers();

        Row(cut, "bob").QuerySelectorAll("button")[1].Click();

        cut.Find(".alert-danger").TextContent.ShouldBe("Failed to update admin role for 'bob'.");
    }

    // ---- monitored folders ----

    [Fact]
    public void Folders_ShowPathAndLastScanOrNever()
    {
        _client.GetMonitoredFolders(Arg.Any<CancellationToken>()).Returns([
            new MonitoredFolderDto(1, "/music/a", null),
            new MonitoredFolderDto(2, "/music/b", DateTimeOffset.UtcNow)
        ]);

        var cut = Render<MonitoredFoldersPanel>();

        var rows = cut.FindAll("tbody tr");
        rows[0].TextContent.ShouldContain("Never");
        rows[1].TextContent.ShouldNotContain("Never");
    }

    [Fact]
    public void AddFolder_RequiresAPath()
    {
        var cut = Render<MonitoredFoldersPanel>();

        cut.Find(".settings-card button").Click();

        cut.Find(".alert-danger").TextContent.ShouldBe("Folder path is required.");
    }

    [Theory]
    [InlineData(true, "alert-success", "Folder '/new' added.")]
    [InlineData(false, "alert-danger", "That folder is already monitored.")]
    public void AddFolder_ReportsTheOutcome(bool added, string alertClass, string message)
    {
        _client.AddMonitoredFolder("/new", Arg.Any<CancellationToken>()).Returns(added);
        var cut = Render<MonitoredFoldersPanel>();

        cut.Find(".settings-card input").Change("/new");
        cut.Find(".settings-card button").Click();

        cut.Find($".{alertClass}").TextContent.ShouldBe(message);
    }

    [Fact]
    public void AddFolder_Failure_ShowsAnError()
    {
        _client.AddMonitoredFolder("/new", Arg.Any<CancellationToken>()).Returns(Task.FromException<bool>(new HttpRequestException()));
        var cut = Render<MonitoredFoldersPanel>();

        cut.Find(".settings-card input").Change("/new");
        cut.Find(".settings-card button").Click();

        cut.Find(".alert-danger").TextContent.ShouldBe("Failed to add folder.");
    }

    [Fact]
    public void RemoveFolder_RemovesAndReloadsOrShowsAnError()
    {
        _client.GetMonitoredFolders(Arg.Any<CancellationToken>()).Returns([new MonitoredFolderDto(7, "/music/a", null)]);
        var cut = Render<MonitoredFoldersPanel>();

        cut.Find("tbody button").Click();
        _client.Received(1).RemoveMonitoredFolder(7, Arg.Any<CancellationToken>());

        _client.RemoveMonitoredFolder(7, Arg.Any<CancellationToken>()).Returns(Task.FromException(new HttpRequestException()));
        cut.Find("tbody button").Click();
        cut.Find(".alert-danger").TextContent.ShouldBe("Failed to remove folder.");
    }

    [Theory]
    [InlineData(true, "Rescan started.")]
    [InlineData(false, "A scan is already in progress.")]
    public void ForceRescan_ReportsWhetherAScanStarted(bool started, string message)
    {
        _client.TriggerFolderScan(Arg.Any<CancellationToken>()).Returns(started);
        var cut = Render<MonitoredFoldersPanel>();

        cut.Find("button.mt-2").Click();

        cut.Find(".alert-success").TextContent.ShouldBe(message);
    }

    [Fact]
    public void ForceRescan_Failure_ShowsAnError()
    {
        _client.TriggerFolderScan(Arg.Any<CancellationToken>()).Returns(Task.FromException<bool>(new HttpRequestException()));
        var cut = Render<MonitoredFoldersPanel>();

        cut.Find("button.mt-2").Click();

        cut.Find(".alert-danger").TextContent.ShouldBe("Failed to start rescan.");
    }

    // ---- tasks ----

    [Fact]
    public void Tasks_IdleStatus_ShowsIdleAndEnablesEveryButton()
    {
        _client.GetTasksStatus(Arg.Any<CancellationToken>()).Returns(new TasksStatusDto(false, null, false, [], false, 0, 0));

        var cut = Render<TasksPanel>();

        cut.FindAll(".badge.bg-success").Count.ShouldBe(3);
        cut.FindAll("button").ShouldAllBe(b => !b.HasAttribute("disabled"));
    }

    [Fact]
    public void Tasks_BusyStatus_ShowsWhatEachTaskIsWorkingOn()
    {
        _client.GetTasksStatus(Arg.Any<CancellationToken>())
            .Returns(new TasksStatusDto(true, "/music/x", true, ["Song A", "Song B"], true, 3, 10));

        var cut = Render<TasksPanel>();

        cut.FindAll(".badge.bg-warning").Count.ShouldBe(3);
        cut.Markup.ShouldContain("/music/x");
        cut.Markup.ShouldContain("Song A, Song B");
        cut.Markup.ShouldContain("3 / 10");
        cut.FindAll("button").ShouldAllBe(b => b.HasAttribute("disabled"));
    }

    [Fact]
    public void Tasks_StatusUnavailable_KeepsShowingIdle()
    {
        _client.GetTasksStatus(Arg.Any<CancellationToken>()).Returns(Task.FromException<TasksStatusDto>(new HttpRequestException()));

        var cut = Render<TasksPanel>();

        cut.FindAll(".badge.bg-success").Count.ShouldBe(3);
    }

    public static TheoryData<int, bool, string> TaskButtonOutcomes()
    {
        return new TheoryData<int, bool, string>
        {
            { 0, true, "Scan started." },
            { 0, false, "A scan is already in progress." },
            { 1, true, "Convert run started." },
            { 1, false, "A convert run is already in progress." },
            { 2, true, "Loudness analysis started." },
            { 2, false, "Loudness analysis is already in progress." }
        };
    }

    [Theory]
    [MemberData(nameof(TaskButtonOutcomes))]
    public void TaskButtons_ReportWhetherTheRunStarted(int button, bool started, string message)
    {
        _client.GetTasksStatus(Arg.Any<CancellationToken>()).Returns(new TasksStatusDto(false, null, false, [], false, 0, 0));
        _client.TriggerFolderScan(Arg.Any<CancellationToken>()).Returns(started);
        _client.TriggerConvert(Arg.Any<CancellationToken>()).Returns(started);
        _client.TriggerLoudnessAnalysis(Arg.Any<CancellationToken>()).Returns(started);
        var cut = Render<TasksPanel>();

        cut.FindAll("button")[button].Click();

        cut.Find(".alert-success").TextContent.ShouldBe(message);
        _client.Received(2).GetTasksStatus(Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData(0, "Failed to start scan: nope")]
    [InlineData(1, "Failed to start convert run: nope")]
    [InlineData(2, "Failed to start loudness analysis: nope")]
    public void TaskButtons_Failure_ShowsTheError(int button, string message)
    {
        _client.GetTasksStatus(Arg.Any<CancellationToken>()).Returns(new TasksStatusDto(false, null, false, [], false, 0, 0));
        _client.TriggerFolderScan(Arg.Any<CancellationToken>()).Returns(Task.FromException<bool>(new HttpRequestException("nope")));
        _client.TriggerConvert(Arg.Any<CancellationToken>()).Returns(Task.FromException<bool>(new HttpRequestException("nope")));
        _client.TriggerLoudnessAnalysis(Arg.Any<CancellationToken>()).Returns(Task.FromException<bool>(new HttpRequestException("nope")));
        var cut = Render<TasksPanel>();

        cut.FindAll("button")[button].Click();

        cut.Find(".alert-danger").TextContent.ShouldBe(message);
    }

    [Fact]
    public async Task Tasks_PollsForStatusUntilDisposed()
    {
        _client.GetTasksStatus(Arg.Any<CancellationToken>()).Returns(
            new TasksStatusDto(false, null, false, [], false, 0, 0),
            new TasksStatusDto(true, "/polled", false, [], false, 0, 0));
        var cut = Render<TasksPanel>();

        cut.WaitForAssertion(() => cut.Markup.ShouldContain("/polled"), TimeSpan.FromSeconds(10));

        await cut.Instance.DisposeAsync();
    }
}
