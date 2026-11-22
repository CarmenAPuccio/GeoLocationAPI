using OpenTelemetry.Metrics;
using System.Diagnostics.Metrics;

namespace GeoLocationAPI.V1.Helpers;

/// <summary>
/// Decides which metrics leave the process. Everything is dropped unless it is listed here, so a new
/// instrument from a library never turns into a new billed CloudWatch metric by accident.
/// </summary>
public static class TelemetryViews
{
    // The request metrics are the "RED" signals (rate, errors, duration). Route and status code show which endpoint
    // is failing, and error.type (set only when an exception occurred) says why. Scheme and protocol version only
    // multiply the number of metrics.
    private static readonly string[] RequestTags = ["http.request.method", "http.route", "http.response.status_code", "error.type"];

    private const int RequestCardinalityLimit = 100;

    private static readonly HashSet<string> RuntimeMetrics = new(StringComparer.Ordinal)
    {
        "dotnet.gc.collections",
        "dotnet.gc.pause.time",
        "dotnet.gc.heap.total_allocated",
        "dotnet.gc.last_collection.heap.size",
        "dotnet.process.cpu.time",
        "dotnet.process.memory.working_set",
        "dotnet.thread_pool.queue.length",
        "dotnet.thread_pool.thread.count",
        "dotnet.exceptions",
    };

    /// <summary>The path health checks are served on. ALB and ECS call it every few seconds.</summary>
    public const string HealthCheckPath = "/hc";

    /// <summary>
    /// A view configuration for the meter provider (AddView). Drops everything that is not listed above.
    /// Returns null to keep an instrument as is.
    /// </summary>
    public static MetricStreamConfiguration? Configure(Instrument instrument)
    {
        ArgumentNullException.ThrowIfNull(instrument);

        return instrument.Name switch
        {
            // A limit well under the default of 2000 bounds what a burst of odd routes or status codes can cost.
            "http.server.request.duration" => new MetricStreamConfiguration { TagKeys = RequestTags, CardinalityLimit = RequestCardinalityLimit },
            "http.server.active_requests" => new MetricStreamConfiguration { TagKeys = [] },
            "geolocation.lookups" => null,
            var name when RuntimeMetrics.Contains(name) => null,
            _ => MetricStreamConfiguration.Drop,
        };
    }
}
