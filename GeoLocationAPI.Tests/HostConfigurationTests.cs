using System.Net;
using Microsoft.AspNetCore.Hosting;
using System.Text.Json;

namespace GeoLocationAPI.Tests;

public class HostConfigurationTests
{
    [Fact]
    public async Task Production_DoesNotExposeApiReference()
    {
        using var factory = new ProductionApiFactory();
        var client = factory.CreateClient();

        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/scalar/v1")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/openapi/v1.json")).StatusCode);
    }

    [Fact]
    public async Task Production_ServesTheApi()
    {
        using var factory = new ProductionApiFactory();
        var client = factory.CreateClient();

        var response = await client.GetAsync("/api/v1/geolocation/8.8.8.8");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task HealthCheck_ReturnsServiceUnavailable_WhenTestAddressIsNotInDatabase()
    {
        using var factory = new GeoLocationApiFactory().WithWebHostBuilder(b => b.UseSetting("HealthCheck:IpToTest", "10.0.0.1"));
        var client = factory.CreateClient();

        var response = await client.GetAsync("/hc");

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("Unhealthy", body.RootElement.GetProperty("Status").GetString());
        Assert.Equal("10.0.0.1", body.RootElement.GetProperty("Results").GetProperty("GeoLocationHealthCheck").GetProperty("Data").GetProperty("ipAddress").GetString());
    }

    [Theory]
    [InlineData("DBSettings:GeoLite2CityDB", "/does/not/exist.mmdb", "GeoLite2CityDB")]
    [InlineData("DBSettings:GeoLite2CityDB", "", "GeoLite2CityDB")]
    [InlineData("ProxyInformation:ProxyKnownNetwork", "not-an-address", "ProxyKnownNetwork")]
    [InlineData("ProxyInformation:ProxyKnownNetworkCIDR", "999", "ProxyKnownNetworkCIDR")]
    [InlineData("HealthCheck:IpToTest", "not-an-address", "HealthCheck:IpToTest")]
    public void InvalidConfiguration_StopsTheAppAtStartup(string key, string value, string expectedInMessage)
    {
        using var factory = new GeoLocationApiFactory().WithWebHostBuilder(b => b.UseSetting(key, value));

        var failure = Assert.ThrowsAny<Exception>(() => factory.CreateClient());

        Assert.Contains(expectedInMessage, Flatten(failure), StringComparison.Ordinal);
    }

    [Fact]
    public void MissingRequiredConfiguration_StopsTheAppAtStartup()
    {
        using var factory = new GeoLocationApiFactory().WithWebHostBuilder(b => b.UseSetting("HealthCheck:IpToTest", null));

        var failure = Assert.ThrowsAny<Exception>(() => factory.CreateClient());

        Assert.Contains("IpToTest", Flatten(failure), StringComparison.Ordinal);
    }

    private static string Flatten(Exception exception)
    {
        var messages = new List<string>();
        for (var current = exception; current is not null; current = current.InnerException!)
        {
            messages.Add(current.Message);
            if (current.InnerException is null)
            {
                break;
            }
        }

        return string.Join(" | ", messages);
    }
}
