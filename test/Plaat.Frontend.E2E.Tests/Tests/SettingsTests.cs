using Microsoft.Playwright;
using Xunit;

namespace Plaat.Frontend.E2E.Tests.Tests;

[Collection(PlaatE2ECollection.Name)]
public sealed class SettingsTests(PlaatE2EFixture fixture)
{
    [Fact]
    public async Task LoudnessNormalizationSwitch_CanBeToggled()
    {
        await using var page = await fixture.NewAuthenticatedPageAsync();

        await page.GotoAsync("/settings");

        // MudSwitch renders <input type="checkbox" role="switch" ...> - the explicit role
        // overrides the implicit checkbox role a plain type="checkbox" would otherwise get.
        var toggle = page.GetByRole(AriaRole.Switch, new() { Name = "Loudness normalization" });
        var wasChecked = await toggle.IsCheckedAsync();

        await toggle.ClickAsync();

        await Assertions.Expect(toggle).ToBeCheckedAsync(new() { Checked = !wasChecked });

        // restore it, since this fixture (and its admin login) is shared across the whole assembly
        await toggle.ClickAsync();
        await Assertions.Expect(toggle).ToBeCheckedAsync(new() { Checked = wasChecked });
    }

    [Fact]
    public async Task ChangePassword_WithCorrectCurrentPassword_SucceedsAndNewPasswordLogsIn()
    {
        // Its own dedicated user, separate from SecondaryUser: this test mutates the account's
        // password, and SecondaryUser's original credentials need to stay stable for every other
        // test in the run (e.g. AdminTests logs in as it just to check an Admin-only redirect).
        await using var page = await fixture.NewAuthenticatedPageAsync("password-change-user", SeededCatalog.PasswordChangeUserPassword);
        const string newPassword = "RotatedPass456!";

        await page.GotoAsync("/settings");
        var formGroups = page.Locator(".form-group");
        await formGroups.Nth(0).Locator("input").FillAsync(SeededCatalog.PasswordChangeUserPassword);
        await formGroups.Nth(1).Locator("input").FillAsync(newPassword);
        await formGroups.Nth(2).Locator("input").FillAsync(newPassword);
        await page.Locator("button", new() { HasText = "Change Password" }).ClickAsync();

        await Assertions.Expect(page.Locator(".alert-success")).ToBeVisibleAsync();

        await page.ClickAsync("text=Sign out");
        await page.WaitForURLAsync(url => url.Contains("/login"));
        await page.FillAsync("#username", "password-change-user");
        await page.FillAsync("#password", newPassword);
        await page.ClickAsync(".login-btn");

        await page.WaitForURLAsync(url => !url.Contains("/login"));
    }
}
