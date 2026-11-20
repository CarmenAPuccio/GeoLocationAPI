using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace GeoLocationAPI.Tests;

public class GeoLocationApiFactory : WebApplicationFactory<Program>
{
    /// <summary>
    /// The in-memory test server has no network peer. A request can set the connection's remote address
    /// with this header, so the caller-IP and forwarded-header behavior can be tested.
    /// </summary>
    public const string RemoteIpHeader = "X-Test-Remote-Ip";

    protected virtual string EnvironmentName => "Development";

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment(EnvironmentName);

        // The database is copied next to the test assembly via the project reference.
        builder.UseSetting("DBSettings:GeoLite2CityDB", Path.Combine(AppContext.BaseDirectory, "GeoLite2-City.mmdb"));

        builder.ConfigureServices(services => services.AddSingleton<IStartupFilter, RemoteIpStartupFilter>());
    }

    private sealed class RemoteIpStartupFilter : IStartupFilter
    {
        public Action<IApplicationBuilder> Configure(
            Action<IApplicationBuilder> next) => app =>
        {
            app.Use(async (context, nextMiddleware) =>
            {
                if (context.Request.Headers.TryGetValue(RemoteIpHeader, out var value)
                    && System.Net.IPAddress.TryParse(value.ToString(), out var address))
                {
                    context.Connection.RemoteIpAddress = address;
                }

                await nextMiddleware();
            });

            next(app);
        };
    }
}

/// <summary>Runs the API as it runs in ECS.</summary>
public sealed class ProductionApiFactory : GeoLocationApiFactory
{
    protected override string EnvironmentName => "Production";
}
