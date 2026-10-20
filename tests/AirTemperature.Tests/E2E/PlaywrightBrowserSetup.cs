namespace AirTemperature.Tests.E2E;

/// <summary>
/// Installs the Playwright Chromium build once, before the UI tests in this namespace run, so nobody has to run
/// an install script by hand. Re-running is a no-op when the browser is already present.
/// On CI (CI=true) it also installs the OS libraries Chromium needs.
/// </summary>
[SetUpFixture]
public class PlaywrightBrowserSetup
{
    [OneTimeSetUp]
    public void InstallChromium()
    {
        var args = Environment.GetEnvironmentVariable("CI") == "true"
            ? new[] { "install", "--with-deps", "chromium" }
            : new[] { "install", "chromium" };

        var exitCode = Microsoft.Playwright.Program.Main(args);
        if (exitCode != 0)
            throw new InvalidOperationException($"Playwright browser install failed (exit code {exitCode}).");
    }
}
