using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Reflection;

namespace GeoLocationAPI.V1.Helpers;

/// <summary>
/// Holds the application's ActivitySource and Meter. A custom type avoids collisions
/// with other components in the DI container and gives one place to dispose them.
/// </summary>
public sealed class Instrumentation : IDisposable
{
    internal const string ActivitySourceName = "GeoLocationAPI";
    internal const string MeterName = "GeoLocationAPI";

    /// <summary>
    /// The product version, plus the commit when the build knows it (for example "1.0.0+3f9a2c1"). A loaded assembly
    /// always has this attribute.
    /// </summary>
    internal static readonly string Version = typeof(Instrumentation).Assembly
        .GetCustomAttribute<AssemblyInformationalVersionAttribute>()!.InformationalVersion;

    private readonly Meter _meter;

    public Instrumentation()
    {
        ActivitySource = new ActivitySource(ActivitySourceName, Version);
        _meter = new Meter(MeterName, Version);
        LookupCounter = _meter.CreateCounter<long>(
            "geolocation.lookups",
            unit: "{lookup}",
            description: "Number of IP address lookups, tagged with whether the address was found in the database.");
    }

    public ActivitySource ActivitySource { get; }

    public Counter<long> LookupCounter { get; }

    public void Dispose()
    {
        ActivitySource.Dispose();
        _meter.Dispose();
    }
}
