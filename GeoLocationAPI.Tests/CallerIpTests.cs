using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using GeoLocationAPI.V1.Models;

namespace GeoLocationAPI.Tests;

public class CallerIpTests : IClassFixture<GeoLocationApiFactory>
{
    private readonly HttpClient _client;

    public CallerIpTests(GeoLocationApiFactory factory) => _client = factory.CreateClient();

    private async Task<HttpResponseMessage> GetAsCaller(string? remoteIp, string? forwardedFor = null)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/geolocation");
        if (remoteIp is not null)
        {
            request.Headers.Add(GeoLocationApiFactory.RemoteIpHeader, remoteIp);
        }

        if (forwardedFor is not null)
        {
            request.Headers.Add("X-Forwarded-For", forwardedFor);
        }

        return await _client.SendAsync(request);
    }

    [Fact]
    public async Task CallerIp_IsLookedUp()
    {
        var response = await GetAsCaller("8.8.8.8");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<GeoLocation>();
        Assert.NotNull(body);
        Assert.Equal("8.8.8.8", body.IPAddress);
        Assert.True(body.IPFoundInGeoDB);
    }

    [Fact]
    public async Task CallerIp_IPv4MappedIPv6_IsLookedUpAsIPv4()
    {
        var body = await (await GetAsCaller("::ffff:8.8.8.8")).Content.ReadFromJsonAsync<GeoLocation>();

        Assert.NotNull(body);
        Assert.Equal("8.8.8.8", body.IPAddress);
        Assert.True(body.IPFoundInGeoDB);
    }

    [Fact]
    public async Task CallerIp_IPv6_IsLookedUpAsIPv6()
    {
        var body = await (await GetAsCaller("2001:4860:4860::8888")).Content.ReadFromJsonAsync<GeoLocation>();

        Assert.NotNull(body);
        Assert.Equal("2001:4860:4860::8888", body.IPAddress);
    }

    [Fact]
    public async Task CallerIp_Unknown_ReturnsProblemDetails()
    {
        // The in-memory server has no remote address unless the test sets one.
        var response = await GetAsCaller(remoteIp: null);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("Invalid IP address", body.RootElement.GetProperty("title").GetString());
        Assert.False(body.RootElement.TryGetProperty("ipAddress", out _));
    }

    [Fact]
    public async Task ForwardedFor_FromTrustedProxy_IsUsed()
    {
        // 172.25.0.0/16 is the load balancer network configured in appsettings.
        var body = await (await GetAsCaller("172.25.1.10", forwardedFor: "8.8.8.8")).Content.ReadFromJsonAsync<GeoLocation>();

        Assert.NotNull(body);
        Assert.Equal("8.8.8.8", body.IPAddress);
    }

    [Fact]
    public async Task ForwardedFor_FromUntrustedClient_IsIgnored()
    {
        var body = await (await GetAsCaller("203.0.113.5", forwardedFor: "8.8.8.8")).Content.ReadFromJsonAsync<GeoLocation>();

        Assert.NotNull(body);
        Assert.Equal("203.0.113.5", body.IPAddress);
    }
}
