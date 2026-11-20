using Asp.Versioning;
using GeoLocationAPI.Configuration;
using GeoLocationAPI.V1.HealthChecks;
using GeoLocationAPI.V1.Services;
using GeoLocationAPI.V1.Helpers;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;
using Microsoft.OpenApi;
using OpenTelemetry;
using OpenTelemetry.Extensions.AWS.Trace;
using OpenTelemetry.Logs;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using Scalar.AspNetCore;
using System.Net;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;


// This is required if the collector doesn't expose an https endpoint as .NET by default
// only allow http2 (required for gRPC) to secure endpoints
AppContext.SetSwitch("System.Net.Http.SocketsHttpHandler.Http2UnencryptedSupport", true);

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddControllers();

builder.Services.AddApiVersioning(options =>
    {
        // Adds the "api-supported-versions" and "api-deprecated-versions" response headers.
        options.ReportApiVersions = true;

        // Every route carries the version in the URL, for example /api/v1/geolocation.
        options.ApiVersionReader = new UrlSegmentApiVersionReader();
    })
    .AddMvc()
    .AddApiExplorer(options =>
    {
        // Formats the version as "'v'major[.minor][-status]", for example "v1". This is also the OpenAPI document name.
        options.GroupNameFormat = "'v'VVV";

        // Replaces {version:apiVersion} in route templates with the actual version.
        options.SubstituteApiVersionInUrl = true;
    })
    // One OpenAPI document per API version. XML doc comments are picked up at build time.
    .AddOpenApi(options =>
    {
        options.Document.AddDocumentTransformer((document, context, cancellationToken) =>
        {
            document.Info = new OpenApiInfo
            {
                Title = "GeoLocationAPI",
                Version = options.Description.ApiVersion.ToString(),
                Description = ".NET Web API for retrieving geolocations. Geolocation data provided by MaxMind GeoLite2."
                    + (options.Description.IsDeprecated ? " This API version has been deprecated." : string.Empty),
                Contact = new OpenApiContact { Name = "Carmen Puccio", Email = "CarmenAPuccio@gmail.com" },
                License = new OpenApiLicense { Name = "Apache 2.0", Url = new Uri("https://www.apache.org/licenses/LICENSE-2.0") }
            };
            return Task.CompletedTask;
        });
    });

builder.Services.AddSingleton<Instrumentation>();

// Traces, metrics and logs are exported over OTLP. The endpoint, protocol and service name come from the
// standard OTEL_EXPORTER_OTLP_ENDPOINT, OTEL_EXPORTER_OTLP_PROTOCOL and OTEL_SERVICE_NAME environment variables.
// With none set, the SDK exports gRPC to http://localhost:4317 as "GeoLocationAPI".
var openTelemetry = builder.Services.AddOpenTelemetry()
    .ConfigureResource(resource => resource.AddService(
        serviceName: "GeoLocationAPI",
        serviceNamespace: "GeoLocation",
        serviceVersion: Instrumentation.Version,
        autoGenerateServiceInstanceId: false))
    .WithTracing(tracing => tracing
        .AddXRayTraceId()
        .AddAspNetCoreInstrumentation()
        .AddHttpClientInstrumentation()
        .AddSource(Instrumentation.ActivitySourceName))
    .WithMetrics(metrics => metrics
        .AddAspNetCoreInstrumentation()
        .AddHttpClientInstrumentation()
        .AddRuntimeInstrumentation()
        .AddMeter(Instrumentation.ActivitySourceName))
    .WithLogging()
    .UseOtlpExporter();

if (builder.Environment.IsDevelopment())
{
    openTelemetry.WithTracing(tracing => tracing.AddConsoleExporter());
}

Sdk.SetDefaultTextMapPropagator(new AWSXRayPropagator());

builder.Services.AddProblemDetails();

builder.Services.AddOptions<DbSettings>()
    .BindConfiguration(DbSettings.SectionName)
    .ValidateDataAnnotations()
    .Validate(o => File.Exists(o.ResolvedPath), "DBSettings:GeoLite2CityDB does not point to an existing file.")
    .ValidateOnStart();

builder.Services.AddOptions<ProxyInformation>()
    .BindConfiguration(ProxyInformation.SectionName)
    .ValidateDataAnnotations()
    .Validate(o => IPAddress.TryParse(o.ProxyKnownNetwork, out _), "ProxyInformation:ProxyKnownNetwork is not a valid IP address.")
    .ValidateOnStart();

builder.Services.AddSingleton<IGeoLocationService, GeoLocationService>();

var healthCheckIp = Required("HealthCheck:IpToTest");
if (!IPAddress.TryParse(healthCheckIp, out _))
{
    throw new InvalidOperationException("HealthCheck:IpToTest is not a valid IP address.");
}

builder.Services.AddHealthChecks()
    .AddTypeActivatedCheck<GeoLocationHealthCheck>(
        "GeoLocationHealthCheck",
        args: new object[] { healthCheckIp });

builder.Services.AddOptions<ForwardedHeadersOptions>()
    .Configure<IOptions<ProxyInformation>>((options, proxy) =>
    {
        options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
        options.KnownIPNetworks.Add(new System.Net.IPNetwork(
            IPAddress.Parse(proxy.Value.ProxyKnownNetwork),
            proxy.Value.ProxyKnownNetworkCIDR));
        options.ForwardLimit = null;
    });

var app = builder.Build();

// Configure the HTTP request pipeline.
// Unhandled exceptions and empty 4xx/5xx responses are returned as RFC 9457 problem details.
app.UseExceptionHandler();
app.UseStatusCodePages();

if (app.Environment.IsDevelopment())
{
    // OpenAPI document at /openapi/v1.json, interactive reference at /scalar/v1.
    app.MapOpenApi().WithDocumentPerVersion();
    app.MapScalarApiReference();
}

app.UseForwardedHeaders();

app.UseAuthorization();

app.MapControllers();

app.MapHealthChecks("/hc", new HealthCheckOptions
{
    ResponseWriter = WriteResponse
});

app.Run();

string Required(string key) =>
    builder.Configuration[key] ?? throw new InvalidOperationException($"Required configuration value '{key}' is missing.");

Task WriteResponse(HttpContext context, HealthReport healthReport)
{
    context.Response.ContentType = "application/json; charset=utf-8";

    var options = new JsonWriterOptions { Indented = true };

    using var memoryStream = new MemoryStream();
    using (var jsonWriter = new Utf8JsonWriter(memoryStream, options))
    {
        jsonWriter.WriteStartObject();
        jsonWriter.WriteString("Status", healthReport.Status.ToString());
        jsonWriter.WriteString("Duration", healthReport.TotalDuration.ToString());
        jsonWriter.WriteString("FrameworkDescription", RuntimeInformation.FrameworkDescription);
        jsonWriter.WriteString("ProcessArchitecture", RuntimeInformation.ProcessArchitecture.ToString());
        jsonWriter.WriteStartObject("Results");

        foreach (var healthReportEntry in healthReport.Entries)
        {
            jsonWriter.WriteStartObject(healthReportEntry.Key);
            jsonWriter.WriteString("Status",
                healthReportEntry.Value.Status.ToString());
            jsonWriter.WriteString("Description",
                healthReportEntry.Value.Description);
            jsonWriter.WriteStartObject("Data");

            foreach (var item in healthReportEntry.Value.Data)
            {
                jsonWriter.WritePropertyName(item.Key);

                JsonSerializer.Serialize(jsonWriter, item.Value,
                    item.Value?.GetType() ?? typeof(object));
            }

            jsonWriter.WriteEndObject();
            jsonWriter.WriteEndObject();
        }

        jsonWriter.WriteEndObject();
        jsonWriter.WriteEndObject();
    }

    return context.Response.WriteAsync(
        Encoding.UTF8.GetString(memoryStream.ToArray()));
}



// Makes the entry point visible to WebApplicationFactory in the test project.
public partial class Program { }
