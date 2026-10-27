using Bunit;
using Bunit.TestDoubles;
using Microsoft.AspNetCore.Components.Server.ProtectedBrowserStorage;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Plaat.Contract;
using Plaat.Contract.Client;
using Plaat.Contract.Models;
using Plaat.Contract.Models.Authorization;
using Plaat.Frontend.Auth;
using Plaat.Frontend.Components.Layout;
using Plaat.Frontend.Components.Pages;
using Plaat.Frontend.Components.Pages.AdminPanels;
using Plaat.Frontend.Models;
using Shouldly;
using Xunit;

namespace Plaat.Frontend.Tests.Components;

/// <summary>
/// The sidebar (pinned playlists as drop targets for dragged tracks, admin link, sign out) and the
/// admin page's tab switching.
/// </summary>
public sealed class NavMenuAndAdminComponentTests : BunitContext
{
    private readonly IPlaatClient _client = Substitute.For<IPlaatClient>();
    private readonly IProxySessionSync _proxy = Substitute.For<IProxySessionSync>();
    private readonly TokenStorageService _tokens = new() { AccessToken = "a", RefreshToken = "r", ExpiresAt = DateTime.UtcNow.AddHours(1) };

    public NavMenuAndAdminComponentTests()
    {
        Services.AddSingleton(_client);
        Services.AddSingleton(_tokens);
        Services.AddSingleton(_proxy);
        Services.AddSingleton(Substitute.For<ITokenSessionStore>());
        Services.AddSingleton<JwtAuthStateProvider>();
        Services.AddDataProtection();
        Services.AddScoped<ProtectedLocalStorage>();
        Services.AddScoped<PersistentTokenStore>();
        Services.AddScoped<AuthService>();
        Services.AddSingleton<NavPlaylistsState>();
        Services.AddLogging();
        JSInterop.Mode = JSRuntimeMode.Loose;
    }

    private static PlaylistOnlyDto Playlist(uint id, string name, bool pinned = true, bool smart = false)
    {
        return new PlaylistOnlyDto { Id = id, PlaylistName = name, IsPinned = pinned, IsSmart = smart, TrackCount = 0 };
    }

    private static TrackDto Track()
    {
        return new TrackDto
        {
            Id = 42,
            Title = "Dragged",
            Artists = [],
            Album = "A",
            AlbumId = 1,
            PlayCount = 0,
            TrackNumber = 1,
            DiscNumber = 1,
            Year = 2000,
            Duration = TimeSpan.FromMinutes(3)
        };
    }

    private IRenderedComponent<NavMenu> RenderMenu(bool admin = false, params PlaylistOnlyDto[] playlists)
    {
        _client.GetPlaylists(Arg.Any<CancellationToken>()).Returns(playlists);
        var auth = AddAuthorization();
        auth.SetAuthorized("me");
        if (admin)
        {
            auth.SetRoles("Admin");
        }

        return Render<NavMenu>();
    }

    private static AngleSharp.Dom.IElement Item(IRenderedComponent<NavMenu> cut, string name)
    {
        return cut.FindAll(".playlist-nav-item").Single(e => e.TextContent.Trim() == name);
    }

    [Fact]
    public void ShowsOnlyPinnedPlaylistsSortedByName()
    {
        var cut = RenderMenu(false, Playlist(1, "zulu"), Playlist(2, "Alpha"), Playlist(3, "hidden", pinned: false));

        cut.FindAll(".playlist-link").Select(l => l.TextContent.Trim()).ShouldBe(["Alpha", "zulu"]);
        cut.FindAll(".playlist-link")[0].GetAttribute("href").ShouldBe("playlists/Alpha");
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void AdminLink_OnlyForAdmins(bool admin)
    {
        var cut = RenderMenu(admin);

        cut.FindAll("a[href='admin']").Count.ShouldBe(admin ? 1 : 0);
    }

    [Fact]
    public async Task DraggingATrack_ArmsRegularPlaylistsAndHighlightsTheHoveredOne()
    {
        var cut = RenderMenu(false, Playlist(1, "Regular"), Playlist(2, "Smart", smart: true));
        var state = Services.GetRequiredService<NavPlaylistsState>();

        Item(cut, "Regular").DragOver();
        Item(cut, "Regular").ClassList.ShouldNotContain("drop-target");

        await cut.InvokeAsync(() => state.BeginDrag(Track()));
        cut.WaitForAssertion(() => Item(cut, "Regular").ClassList.ShouldContain("drop-armed"));
        Item(cut, "Smart").ClassList.ShouldNotContain("drop-armed");

        Item(cut, "Smart").DragOver();
        Item(cut, "Smart").ClassList.ShouldNotContain("drop-target");
        Item(cut, "Regular").DragOver();
        Item(cut, "Regular").ClassList.ShouldContain("drop-target");
        Item(cut, "Smart").DragLeave();
        Item(cut, "Regular").ClassList.ShouldContain("drop-target");
        Item(cut, "Regular").DragLeave();
        Item(cut, "Regular").ClassList.ShouldNotContain("drop-target");

        Item(cut, "Regular").DragOver();
        await cut.InvokeAsync(() => state.EndDrag());
        cut.WaitForAssertion(() => Item(cut, "Regular").ClassList.ShouldNotContain("drop-armed"));
        Item(cut, "Regular").ClassList.ShouldNotContain("drop-target");
    }

    [Fact]
    public async Task DroppingATrack_AddsItToThatPlaylist()
    {
        var cut = RenderMenu(false, Playlist(1, "Regular"));
        var state = Services.GetRequiredService<NavPlaylistsState>();
        await cut.InvokeAsync(() => state.BeginDrag(Track()));

        Item(cut, "Regular").Drop();

        await _client.Received(1).AddToPlaylist(1, 42, Arg.Any<CancellationToken>());
        cut.WaitForAssertion(() => Item(cut, "Regular").ClassList.ShouldNotContain("drop-armed"));
    }

    [Fact]
    public async Task PlaylistChangesElsewhere_Rerender()
    {
        var cut = RenderMenu(false, Playlist(1, "One"));
        _client.GetPlaylists(Arg.Any<CancellationToken>()).Returns([Playlist(1, "One"), Playlist(2, "Two")]);

        await cut.InvokeAsync(() => Services.GetRequiredService<NavPlaylistsState>().RefreshAsync());

        cut.WaitForAssertion(() => cut.FindAll(".playlist-link").Count.ShouldBe(2));
        cut.Instance.Dispose();
    }

    [Fact]
    public void SignOut_ClearsTheSession()
    {
        var cut = RenderMenu();

        cut.Find(".sign-out button").Click();

        _tokens.AccessToken.ShouldBeNull();
        _proxy.Received(1).Clear();
    }

    // ---- admin page ----

    [Theory]
    [InlineData(0, typeof(TasksPanel))]
    [InlineData(1, typeof(LogsPanel))]
    [InlineData(2, typeof(CorruptTracksPanel))]
    [InlineData(3, typeof(ManageUsersPanel))]
    [InlineData(4, typeof(MonitoredFoldersPanel))]
    public void AdminTabs_ShowTheirPanel(int tab, Type panel)
    {
        ComponentFactories.AddStub<TasksPanel>();
        ComponentFactories.AddStub<LogsPanel>();
        ComponentFactories.AddStub<CorruptTracksPanel>();
        ComponentFactories.AddStub<ManageUsersPanel>();
        ComponentFactories.AddStub<MonitoredFoldersPanel>();
        var cut = Render<Admin>();

        cut.FindAll(".nav-link")[tab].Click();

        cut.FindAll(".nav-link")[tab].ClassList.ShouldContain("active");
        cut.FindAll(".nav-link.active").ShouldHaveSingleItem();
        cut.FindComponents<Stub<TasksPanel>>().Count.ShouldBe(panel == typeof(TasksPanel) ? 1 : 0);
        cut.FindComponents<Stub<LogsPanel>>().Count.ShouldBe(panel == typeof(LogsPanel) ? 1 : 0);
        cut.FindComponents<Stub<CorruptTracksPanel>>().Count.ShouldBe(panel == typeof(CorruptTracksPanel) ? 1 : 0);
        cut.FindComponents<Stub<ManageUsersPanel>>().Count.ShouldBe(panel == typeof(ManageUsersPanel) ? 1 : 0);
        cut.FindComponents<Stub<MonitoredFoldersPanel>>().Count.ShouldBe(panel == typeof(MonitoredFoldersPanel) ? 1 : 0);
    }
}
