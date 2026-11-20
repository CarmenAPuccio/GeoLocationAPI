using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace GeoLocationAPI.Tests;

public class GeoLocationApiFactory : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");

        // The database is copied next to the test assembly via the project reference.
        builder.UseSetting("DBSettings:GeoLite2CityDB", Path.Combine(AppContext.BaseDirectory, "GeoLite2-City.mmdb"));
    }
}
