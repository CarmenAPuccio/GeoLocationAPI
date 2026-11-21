using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using GeoLocationAPI.V1.Models;

namespace GeoLocationAPI.Tests;

public class GeoLocationEndpointTests : IClassFixture<GeoLocationApiFactory>
{
    private readonly HttpClient _client;

    public GeoLocationEndpointTests(GeoLocationApiFactory factory) => _client = factory.CreateClient();

    [Fact]
    public async Task KnownIp_ReturnsLocation()
    {
        var response = await _client.GetAsync("/api/v1/geolocation/8.8.8.8");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<GeoLocation>();
        Assert.NotNull(body);
        Assert.True(body.IPFoundInGeoDB);
        Assert.Equal("8.8.8.8", body.IPAddress);
        Assert.Equal("United States", body.Country);
        Assert.Equal("North America", body.Continent);
        Assert.Equal("8.8.8.8 found in the GeoDB", body.Message);
    }

    [Fact]
    public async Task PrivateIp_IsNotFoundInDatabase()
    {
        var body = await _client.GetFromJsonAsync<GeoLocation>("/api/v1/geolocation/10.0.0.1");

        Assert.NotNull(body);
        Assert.False(body.IPFoundInGeoDB);
        Assert.Equal("10.0.0.1 not found in the GeoDB", body.Message);
    }

    [Fact]
    public async Task ApiVersionHeaders_AreReported()
    {
        var response = await _client.GetAsync("/api/v1/geolocation/8.8.8.8");

        Assert.True(response.Headers.Contains("api-supported-versions"));
    }

    [Fact]
    public async Task OpenApiDocument_DescribesTheVersionedApi()
    {
        var response = await _client.GetAsync("/openapi/v1.json");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = body.RootElement;

        Assert.Equal("GeoLocationAPI", root.GetProperty("info").GetProperty("title").GetString());
        Assert.Equal("1.0", root.GetProperty("info").GetProperty("version").GetString());

        var operation = root.GetProperty("paths").GetProperty("/api/v1/GeoLocation/{ipAddress}").GetProperty("get");
        Assert.True(operation.GetProperty("responses").TryGetProperty("200", out _));
        Assert.True(operation.GetProperty("responses").GetProperty("400").GetProperty("content").TryGetProperty("application/problem+json", out _));
    }

    [Fact]
    public async Task ScalarReference_IsServedInDevelopment()
    {
        var response = await _client.GetAsync("/scalar/v1");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Ipv6_IsAccepted()
    {
        var response = await _client.GetAsync("/api/v1/geolocation/2001:4860:4860::8888");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Theory]
    [InlineData("abc")]
    [InlineData("999.1.1.1")]
    [InlineData("1.2.3")]
    public async Task InvalidIp_ReturnsProblemDetails(string value)
    {
        var response = await _client.GetAsync($"/api/v1/geolocation/{value}");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);

        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = body.RootElement;
        Assert.Equal(400, root.GetProperty("status").GetInt32());
        Assert.Equal("Invalid IP address", root.GetProperty("title").GetString());
        Assert.Equal(value, root.GetProperty("ipAddress").GetString());
        Assert.True(root.TryGetProperty("traceId", out _));
    }

    [Fact]
    public async Task UnknownRoute_ReturnsProblemDetails()
    {
        var response = await _client.GetAsync("/api/v1/nothing-here");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task HealthCheck_ReportsHealthy()
    {
        var response = await _client.GetAsync("/hc");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("Healthy", body.RootElement.GetProperty("Status").GetString());
    }
}
