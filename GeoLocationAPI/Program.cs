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
using OpenTelemetry.Context.Propagation;
using OpenTelemetry.Extensions.AWS.Trace;
using OpenTelemetry.Logs;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using Scalar.AspNetCore;
using System.Net;
using System.Runtime.InteropServices;
using System.Text.Json;



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

// Traces, metrics and logs are exported over OTLP. The endpoint and protocol come from the standard
// OTEL_EXPORTER_OTLP_ENDPOINT and OTEL_EXPORTER_OTLP_PROTOCOL variables. With none set, the SDK exports gRPC to
// http://localhost:4317, which is the collector sidecar.
//
// A service name set in code wins over OTEL_SERVICE_NAME, so the variable is read here and passed on.
var serviceName = builder.Configuration["OTEL_SERVICE_NAME"] ?? "GeoLocationAPI";
var openTelemetry = builder.Services.AddOpenTelemetry()
    // Attributes that are the same for the life of the process belong on the resource, not on every span.
    .ConfigureResource(resource => resource
        .AddService(
            serviceName: serviceName,
            serviceNamespace: "GeoLocation",
            serviceVersion: Instrumentation.Version)
        .AddAttributes([new KeyValuePair<string, object>("deployment.environment.name", builder.Environment.EnvironmentName)]))
    .WithTracing(tracing => tracing
        .AddXRayTraceId()
        // Failed work gets an Error status instead of staying Unset, so it can be found in a trace backend.
        .SetErrorStatusOnException()
        // The ALB and ECS call the health check every few seconds. Tracing every call buries the real traces
        // (they would be nearly all of the traces). Failures still show up in logs and in the request metrics.
        .AddAspNetCoreInstrumentation(options =>
        {
            options.Filter = context => !context.Request.Path.StartsWithSegments(TelemetryViews.HealthCheckPath);
            // Unhandled exceptions are returned as a 500 problem details body. The exception type, message and
            // stack trace go on the request span, so a failed trace explains itself.
            options.RecordException = true;
        })
        .AddSource(Instrumentation.ActivitySourceName))
    .WithMetrics(metrics => metrics
        .AddAspNetCoreInstrumentation()
        // Runtime metrics are built in to .NET 9 and later, so no instrumentation package is needed.
        .AddMeter("System.Runtime")
        .AddMeter(Instrumentation.MeterName)
        .AddView(TelemetryViews.Configure))
    // The log body is the message template ("{IpAddress} was not found") with the values as attributes.
    // The formatted message is added as well, so a log line reads naturally in CloudWatch Logs.
    .WithLogging(configureBuilder: null, configureOptions: options => options.IncludeFormattedMessage = true)
    .UseOtlpExporter();

if (builder.Environment.IsDevelopment())
{
    openTelemetry.WithTracing(tracing => tracing.AddConsoleExporter());
}
else
{
    // Logs reach CloudWatch through OTLP, so the console would store every line a second time. Warnings and
    // errors stay on the console as a fallback for when the collector sidecar is unavailable.
    builder.Logging.AddFilter<Microsoft.Extensions.Logging.Console.ConsoleLoggerProvider>(null, LogLevel.Warning);
}

// W3C trace context and baggage for callers that send them, and the X-Amzn-Trace-Id header the load balancer
// adds. The first valid trace context wins, so a caller's traceparent is used before the load balancer's header.
Sdk.SetDefaultTextMapPropagator(new CompositeTextMapPropagator(
[
    new TraceContextPropagator(),
    new BaggagePropagator(),
    new AWSXRayPropagator(),
]));

builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<ExceptionTraceHandler>();

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

builder.Services.AddOptions<HealthCheckSettings>()
    .BindConfiguration(HealthCheckSettings.SectionName)
    .ValidateDataAnnotations()
    .Validate(o => IPAddress.TryParse(o.IpToTest, out _), "HealthCheck:IpToTest is not a valid IP address.")
    .ValidateOnStart();

builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddHealthChecks().AddCheck<GeoLocationHealthCheck>("GeoLocationHealthCheck");

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

app.MapControllers();

app.MapHealthChecks(TelemetryViews.HealthCheckPath, new HealthCheckOptions
{
    ResponseWriter = WriteResponse
});

app.Run();

static async Task WriteResponse(HttpContext context, HealthReport healthReport)
{
    context.Response.ContentType = "application/json; charset=utf-8";

    await using var jsonWriter = new Utf8JsonWriter(context.Response.BodyWriter, new JsonWriterOptions { Indented = true });

    jsonWriter.WriteStartObject();
    jsonWriter.WriteString("Status", healthReport.Status.ToString());
    jsonWriter.WriteString("Duration", healthReport.TotalDuration.ToString());
    jsonWriter.WriteString("FrameworkDescription", RuntimeInformation.FrameworkDescription);
    jsonWriter.WriteString("ProcessArchitecture", RuntimeInformation.ProcessArchitecture.ToString());
    jsonWriter.WriteStartObject("Results");

    foreach (var healthReportEntry in healthReport.Entries)
    {
        jsonWriter.WriteStartObject(healthReportEntry.Key);
        jsonWriter.WriteString("Status", healthReportEntry.Value.Status.ToString());
        jsonWriter.WriteString("Description", healthReportEntry.Value.Description);
        jsonWriter.WriteStartObject("Data");

        foreach (var item in healthReportEntry.Value.Data)
        {
            jsonWriter.WritePropertyName(item.Key);
            JsonSerializer.Serialize(jsonWriter, item.Value, item.Value?.GetType() ?? typeof(object));
        }

        jsonWriter.WriteEndObject();
        jsonWriter.WriteEndObject();
    }

    jsonWriter.WriteEndObject();
    jsonWriter.WriteEndObject();
}

// Makes the entry point visible to WebApplicationFactory in the test project.
public partial class Program { }
