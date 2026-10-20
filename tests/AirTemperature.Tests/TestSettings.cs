namespace AirTemperature.Tests;

/// <summary>
/// All configuration comes from environment variables so nothing secret is committed.
/// Set them in your shell locally, or as GitHub Actions secrets in CI.
/// </summary>
public static class TestSettings
{
    /// <summary>OutSystems app root, e.g. https://&lt;env&gt;.outsystemscloud.com/AirTemprature</summary>
    public static string AppBaseUrl =>
        (NullIfEmpty(Environment.GetEnvironmentVariable("APP_BASE_URL"))
         ?? "https://personal-qjaqfoeg.outsystemscloud.com/AirTemprature").TrimEnd('/');

    // CI passes unset variables as empty strings.
    private static string? NullIfEmpty(string? value) => string.IsNullOrWhiteSpace(value) ? null : value;

    public static string? AppUsername => Environment.GetEnvironmentVariable("APP_USERNAME");
    public static string? AppPassword => Environment.GetEnvironmentVariable("APP_PASSWORD");

    public const string ApiUrl = "https://api-open.data.gov.sg/v2/real-time/api/air-temperature";

    /// <summary>Optional data.gov.sg key; raises the rate limit from 6 to 12+ requests per 10 s.</summary>
    public static string? DataGovApiKey => Environment.GetEnvironmentVariable("DATAGOV_API_KEY");

    public static bool HasAppCredentials =>
        !string.IsNullOrWhiteSpace(AppUsername) && !string.IsNullOrWhiteSpace(AppPassword);
}
