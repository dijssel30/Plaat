using Microsoft.Playwright;
using Shouldly;
using Xunit;

namespace Plaat.Frontend.E2E.Tests.Tests;

[Collection(PlaatE2ECollection.Name)]
public sealed class AuthTests(PlaatE2EFixture fixture)
{
    [Fact]
    public async Task VisitingRoot_WhenNotLoggedIn_RedirectsToLogin()
    {
        await using var page = await fixture.NewPageAsync();

        await page.GotoAsync("/");

        await page.WaitForURLAsync(url => url.Contains("/login"));
    }

    [Fact]
    public async Task Login_WithValidCredentials_ReachesHomePage()
    {
        await using var page = await fixture.NewAuthenticatedPageAsync();

        page.Url.ShouldNotContain("/login");
        await Assertions.Expect(page.GetByText("Jump back in")).ToBeVisibleAsync();
    }

    [Fact]
    public async Task Login_WithInvalidCredentials_ShowsError()
    {
        await using var page = await fixture.NewPageAsync();

        await page.GotoAsync("/login");
        await page.FillAsync("#username", "admin");
        await page.FillAsync("#password", "not-the-password");
        await page.ClickAsync(".login-btn");

        await Assertions.Expect(page.Locator(".login-error")).ToContainTextAsync("Invalid username or password");
        page.Url.ShouldContain("/login");
    }

    [Fact]
    public async Task SignOut_ReturnsToLogin()
    {
        await using var page = await fixture.NewAuthenticatedPageAsync();

        await page.ClickAsync("text=Sign out");

        await page.WaitForURLAsync(url => url.Contains("/login"));
    }
}
