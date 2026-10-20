using System.Text.Json;

namespace AirTemperature.Tests.Api;

/// <summary>
/// Contract tests for the data.gov.sg endpoint the sync depends on. They pin the parts of the
/// response the OutSystems integration relies on, so an upstream change fails here first.
/// Kept to 4 requests: the anonymous rate limit is 6 requests per 10 seconds.
/// </summary>
[Category("Api")]
public class AirTemperatureApiContractTests : PlaywrightTest
{
    private IAPIRequestContext _api = null!;

    [SetUp]
    public async Task CreateClient()
    {
        var headers = new Dictionary<string, string>();
        if (!string.IsNullOrWhiteSpace(TestSettings.DataGovApiKey))
            headers["x-api-key"] = TestSettings.DataGovApiKey!;
        _api = await Playwright.APIRequest.NewContextAsync(new() { ExtraHTTPHeaders = headers });
    }

    [TearDown]
    public async Task DisposeClient() => await _api.DisposeAsync();

    private async Task<(int Status, JsonElement Body)> GetAsync(string query = "")
    {
        var response = await _api.GetAsync(TestSettings.ApiUrl + query);
        var json = await response.JsonAsync();
        return (response.Status, json!.Value);
    }

    [Test]
    public async Task Latest_reading_has_the_shape_the_sync_relies_on()
    {
        var (status, body) = await GetAsync();

        Assert.That(status, Is.EqualTo(200));
        Assert.That(body.GetProperty("code").GetInt32(), Is.EqualTo(0));

        var data = body.GetProperty("data");
        Assert.That(data.GetProperty("readingUnit").GetString(), Is.EqualTo("deg C"));

        var stations = data.GetProperty("stations").EnumerateArray().ToList();
        Assert.That(stations, Is.Not.Empty);
        foreach (var s in stations)
        {
            Assert.That(s.GetProperty("id").GetString(), Is.Not.Empty);
            Assert.That(s.GetProperty("name").GetString(), Is.Not.Empty);
            // The OpenAPI spec says "labelLocation"; the live API (and our mapping) uses "location".
            var location = s.GetProperty("location");
            Assert.That(location.TryGetProperty("latitude", out _), Is.True);
            Assert.That(location.TryGetProperty("longitude", out _), Is.True);
        }

        var reading = data.GetProperty("readings")[0];
        Assert.That(DateTimeOffset.Parse(reading.GetProperty("timestamp").GetString()!).Offset,
            Is.EqualTo(TimeSpan.FromHours(8)), "Timestamps are expected in SGT (+08:00)");
        var values = reading.GetProperty("data").EnumerateArray().ToList();
        Assert.That(values, Is.Not.Empty);
        Assert.That(values[0].GetProperty("stationId").GetString(), Is.Not.Empty);
        Assert.That(values[0].GetProperty("value").ValueKind, Is.EqualTo(JsonValueKind.Number));
    }

    [Test]
    public async Task Date_query_is_paginated_newest_first()
    {
        // Yesterday in Singapore time: a full day, so pagination is guaranteed.
        var sgtYesterday = DateTimeOffset.UtcNow.ToOffset(TimeSpan.FromHours(8)).AddDays(-1).ToString("yyyy-MM-dd");

        var (status, page1) = await GetAsync($"?date={sgtYesterday}");
        Assert.That(status, Is.EqualTo(200));
        var data1 = page1.GetProperty("data");
        var times1 = Timestamps(data1);
        Assert.That(times1, Is.Ordered.Descending, "Readings must be newest first; the checkpoint stop depends on it");
        Assert.That(data1.TryGetProperty("paginationToken", out var token) && token.GetString() is { Length: > 0 },
            Is.True, "A full day must have a next page");

        var (status2, page2) = await GetAsync($"?date={sgtYesterday}&paginationToken={Uri.EscapeDataString(token.GetString()!)}");
        Assert.That(status2, Is.EqualTo(200));
        var times2 = Timestamps(page2.GetProperty("data"));
        Assert.That(times2.First(), Is.LessThan(times1.Last()), "Page 2 continues with older readings");
    }

    [Test]
    public async Task Invalid_date_is_rejected_with_400()
    {
        var (status, _) = await GetAsync("?date=not-a-date");
        Assert.That(status, Is.EqualTo(400));
    }

    private static List<DateTimeOffset> Timestamps(JsonElement data) =>
        data.GetProperty("readings").EnumerateArray()
            .Select(r => DateTimeOffset.Parse(r.GetProperty("timestamp").GetString()!))
            .ToList();
}
