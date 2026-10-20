using System.Text.RegularExpressions;

namespace AirTemperature.Tests.E2E;

/// <summary>
/// UI smoke tests against the deployed OutSystems app. Credentials come only from
/// APP_USERNAME / APP_PASSWORD; without them the logged-in tests are skipped (not failed).
/// </summary>
[Category("E2E")]
public class UiSmokeTests : PageTest
{
    private static string Url(string screen) => $"{TestSettings.AppBaseUrl}/{screen}";

    // A Reactive app downloads its scripts before it renders or redirects; a cold
    // Personal Environment can take ~10 s, so the 5 s Playwright default is too tight.
    [SetUp]
    public void UseOutSystemsFriendlyTimeouts() => SetDefaultExpectTimeout(20_000);

    private async Task LoginAsync()
    {
        if (!TestSettings.HasAppCredentials)
            Assert.Ignore("Set APP_USERNAME and APP_PASSWORD to run the logged-in UI tests.");

        await Page.GotoAsync(Url("Login"));
        await Page.Locator("input[id$='Input_UsernameVal']").FillAsync(TestSettings.AppUsername!);
        await Page.Locator("input[id$='Input_PasswordVal']").FillAsync(TestSettings.AppPassword!);
        await Page.GetByRole(AriaRole.Button, new() { Name = "Login" }).ClickAsync();

        // Reactive apps navigate client-side (no page "load" event), so poll the URL instead
        // of WaitForURLAsync. Still on /Login after this means the credentials were rejected.
        try
        {
            await Expect(Page).Not.ToHaveURLAsync(new Regex("/Login"), new() { Timeout = 30_000 });
        }
        catch (PlaywrightException)
        {
            // Never rethrow Playwright's message here: its aria snapshot includes the login
            // form's field values, which would print the password into test output / CI logs.
            await Page.Locator("input[id$='Input_PasswordVal']").FillAsync(string.Empty);
            var appMessage = await Page.GetByRole(AriaRole.Alert).AllInnerTextsAsync();
            Assert.Fail("Login failed, still on /Login. App said: '" + string.Join(" ", appMessage) +
                        "'. Check APP_USERNAME / APP_PASSWORD (an app user from the Users app).");
        }
    }

    [Test]
    public async Task Anonymous_visitor_is_sent_to_login()
    {
        await Page.GotoAsync(Url("Dashboard"));
        await Expect(Page).ToHaveURLAsync(new Regex("/Login"));
    }

    [Test]
    public async Task Dashboard_shows_live_island_overview()
    {
        await LoginAsync();
        await Page.GotoAsync(Url("Dashboard"));

        await Expect(Page.GetByText("Island average now")).ToBeVisibleAsync();
        await Expect(Page.GetByText(new Regex(@"^\d+ / \d+$"))).ToBeVisibleAsync(); // stations reporting
        await Expect(Page.Locator(".at-row").First).ToBeVisibleAsync();             // ranking rows
    }

    [Test]
    public async Task Station_detail_opens_from_ranking_and_switches_range()
    {
        await LoginAsync();
        await Page.GotoAsync(Url("Dashboard"));

        await Page.Locator(".at-row").First.ClickAsync();
        await Expect(Page).ToHaveURLAsync(new Regex("StationDetail\\?StationId=\\d+"));
        await Expect(Page.GetByText("Latest reading")).ToBeVisibleAsync();

        await Page.GetByText("7 days", new() { Exact = true }).ClickAsync();
        await Expect(Page.GetByText(new Regex(", daily")).First).ToBeVisibleAsync();
    }
}
