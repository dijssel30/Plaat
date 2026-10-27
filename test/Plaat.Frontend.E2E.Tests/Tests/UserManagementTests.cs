using Microsoft.Playwright;
using Xunit;

namespace Plaat.Frontend.E2E.Tests.Tests;

/// <summary>
/// Admin user management. Every test creates its own user, so none of them can affect the seeded
/// logins other tests rely on.
/// </summary>
[Collection(PlaatE2ECollection.Name)]
public sealed class UserManagementTests(PlaatE2EFixture fixture)
{
    private const string InitialPassword = "InitialPass123!";

    private static async Task<IPage> OpenManageUsers(PlaatE2EFixture fixture)
    {
        var page = await fixture.NewAuthenticatedPageAsync();
        await page.GotoAsync("/admin");
        await page.ClickAsync("text=Manage Users");

        return page;
    }

    private static async Task<string> CreateUser(IPage page, bool isAdmin = false)
    {
        var username = $"e2e-user-{Guid.NewGuid():N}"[..20];

        await page.FillAsync("input[placeholder='Username']", username);
        await page.FillAsync(".settings-card input[type=password]", InitialPassword);
        if (isAdmin)
        {
            await page.CheckAsync("#newUserIsAdminCheck");
        }

        await page.GetByRole(AriaRole.Button, new() { Name = "Create User" }).ClickAsync();
        await Assertions.Expect(page.Locator(".alert-success")).ToHaveTextAsync($"User '{username}' created successfully.");

        return username;
    }

    private async Task<bool> CanLogIn(string username, string password)
    {
        await using var page = await fixture.NewPageAsync();
        await page.GotoAsync("/login");
        await page.FillAsync("#username", username);
        await page.FillAsync("#password", password);
        await page.ClickAsync(".login-btn");

        try
        {
            await page.WaitForURLAsync(url => !url.Contains("/login"), new() { Timeout = 5000 });
            return true;
        }
        catch (TimeoutException)
        {
            return false;
        }
    }

    [Fact]
    public async Task CreatedUser_IsListedAndCanLogIn()
    {
        await using var page = await OpenManageUsers(fixture);

        var username = await CreateUser(page);

        var row = page.Locator("tbody tr", new() { HasText = username });
        await Assertions.Expect(row).ToContainTextAsync("No");
        Assert.True(await CanLogIn(username, InitialPassword));
    }

    [Fact]
    public async Task ResettingAPassword_ReplacesTheOldOne()
    {
        await using var page = await OpenManageUsers(fixture);
        var username = await CreateUser(page);
        const string newPassword = "ResetPass456!";

        var row = page.Locator("tbody tr", new() { HasText = username });
        await row.Locator("input[placeholder='New password']").FillAsync(newPassword);
        // The field is bound on change, which fires when it loses focus.
        await row.Locator("input[placeholder='New password']").PressAsync("Tab");
        await row.GetByRole(AriaRole.Button, new() { Name = "Reset" }).ClickAsync();

        await Assertions.Expect(page.Locator(".alert-success")).ToHaveTextAsync($"Password for '{username}' updated.");
        Assert.False(await CanLogIn(username, InitialPassword));
        Assert.True(await CanLogIn(username, newPassword));
    }

    [Fact]
    public async Task MakingAUserAdmin_GivesThemTheAdminScreen()
    {
        await using var page = await OpenManageUsers(fixture);
        var username = await CreateUser(page);
        var row = page.Locator("tbody tr", new() { HasText = username });

        await row.GetByRole(AriaRole.Button, new() { Name = "Make admin" }).ClickAsync();

        await Assertions.Expect(row.GetByRole(AriaRole.Button, new() { Name = "Revoke admin" })).ToBeVisibleAsync();
        await Assertions.Expect(row).ToContainTextAsync("Yes");

        await using var userPage = await fixture.NewAuthenticatedPageAsync(username, InitialPassword);
        await Assertions.Expect(userPage.Locator("a.nav-link[href='admin']")).ToBeVisibleAsync();
        await userPage.GotoAsync("/admin");
        await Assertions.Expect(userPage.Locator("h1", new() { HasText = "Admin" })).ToBeVisibleAsync();
    }

    [Fact]
    public async Task RevokingAdmin_TakesTheAdminScreenAway()
    {
        await using var page = await OpenManageUsers(fixture);
        var username = await CreateUser(page, isAdmin: true);
        var row = page.Locator("tbody tr", new() { HasText = username });
        await Assertions.Expect(row).ToContainTextAsync("Yes");

        await row.GetByRole(AriaRole.Button, new() { Name = "Revoke admin" }).ClickAsync();
        await Assertions.Expect(row.GetByRole(AriaRole.Button, new() { Name = "Make admin" })).ToBeVisibleAsync();

        await using var userPage = await fixture.NewAuthenticatedPageAsync(username, InitialPassword);
        await Assertions.Expect(userPage.Locator("a.nav-link[href='settings']")).ToBeVisibleAsync();
        await Assertions.Expect(userPage.Locator("a.nav-link[href='admin']")).Not.ToBeVisibleAsync();
    }

    [Fact]
    public async Task CreatingAUserWithoutAPassword_IsRejected()
    {
        await using var page = await OpenManageUsers(fixture);

        await page.FillAsync("input[placeholder='Username']", "no-password-user");
        await page.GetByRole(AriaRole.Button, new() { Name = "Create User" }).ClickAsync();

        await Assertions.Expect(page.Locator(".alert-danger")).ToHaveTextAsync("Username and password are required.");
    }
}
