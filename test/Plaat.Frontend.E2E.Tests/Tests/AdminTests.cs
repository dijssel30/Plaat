using Microsoft.Playwright;
using Xunit;

namespace Plaat.Frontend.E2E.Tests.Tests;

[Collection(PlaatE2ECollection.Name)]
public sealed class AdminTests(PlaatE2EFixture fixture)
{
    [Fact]
    public async Task AdminPage_AsNonAdmin_RedirectsToLogin()
    {
        // Non-admin, already-authenticated users hitting an Admin-only route render Blazor's
        // <NotAuthorized> branch, which (see Routes.razor/RedirectToLogin.razor) bounces to /login
        // with a returnUrl - there is no separate "403 forbidden" page in this app.
        await using var page = await fixture.NewAuthenticatedPageAsync("secondary", SeededCatalog.SecondaryUserPassword);

        await page.GotoAsync("/admin");

        await page.WaitForURLAsync(url => url.Contains("/login"));
    }

    [Fact]
    public async Task AdminPage_MonitoredFoldersTab_ListsSeededFolderAndCanAddOne()
    {
        await using var page = await fixture.NewAuthenticatedPageAsync();

        await page.GotoAsync("/admin");
        await page.ClickAsync("text=Monitored Folders");

        await Assertions.Expect(page.Locator("td", new() { HasText = fixture.Catalog.MonitoredFolder.Path })).ToBeVisibleAsync();

        var newFolderPath = $"/music/e2e-{Guid.NewGuid():N}";
        await page.FillAsync("input[placeholder='/path/to/music']", newFolderPath);
        // Not "text=Add Folder": that also matches the <h3>Add Folder</h3> heading above the
        // button, and the legacy ClickAsync(selector) overload silently clicks the first match
        // (the heading) instead of erroring on the ambiguity the way locator-based strict mode would.
        await page.GetByRole(AriaRole.Button, new() { Name = "Add Folder" }).ClickAsync();

        await Assertions.Expect(page.Locator("td", new() { HasText = newFolderPath })).ToBeVisibleAsync();
    }

    [Fact]
    public async Task AdminPage_TabSwitching_ShowsDifferentPanels()
    {
        await using var page = await fixture.NewAuthenticatedPageAsync();

        await page.GotoAsync("/admin");

        await page.ClickAsync("text=Manage Users");
        await Assertions.Expect(page.Locator(".nav-link.active", new() { HasText = "Manage Users" })).ToBeVisibleAsync();

        await page.ClickAsync("text=Logs");
        await Assertions.Expect(page.Locator(".nav-link.active", new() { HasText = "Logs" })).ToBeVisibleAsync();
    }
}
