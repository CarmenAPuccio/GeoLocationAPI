using GeoLocationAPI.Swagger;
using GeoLocationAPI.V1.HealthChecks;
using GeoLocationAPI.V1.Models;
using GeoLocationAPI.V1.Services;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Mvc.ApiExplorer;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;
using OpenTelemetry;
using OpenTelemetry.Contrib.Extensions.AWSXRay.Trace;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using Swashbuckle.AspNetCore.SwaggerGen;
using System.Net;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;


// This is required if the collector doesn't expose an https endpoint as .NET by default
// only allow http2 (required for gRPC) to secure endpoints
AppContext.SetSwitch("System.Net.Http.SocketsHttpHandler.Http2UnencryptedSupport", true);

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddControllers();

// Learn more about configuring Swagger/OpenAPI at https://aka.ms/aspnetcore/swashbuckle
//builder.Services.AddEndpointsApiExplorer();
builder.Services.AddApiVersioning(

    options =>
    {
        // reporting api versions will return the headers "api-supported-versions" and "api-deprecated-versions"
        options.ReportApiVersions = true;

    });
builder.Services.AddVersionedApiExplorer(
    options =>
    {
        // add the versioned api explorer, which also adds IApiVersionDescriptionProvider service
        // note: the specified format code will format the version as "'v'major[.minor][-status]"
        options.GroupNameFormat = "'v'VVV";

        // note: this option is only necessary when versioning by url segment. the SubstitutionFormat
        // can also be used to control the format of the API version in route templates
        options.SubstituteApiVersionInUrl = true;
    });
builder.Services.AddTransient<IConfigureOptions<SwaggerGenOptions>, ConfigureSwaggerOptions>();

builder.Services.AddSwaggerGen(
        options =>
        {
            // add a custom operation filter which sets default values
            options.OperationFilter<SwaggerDefaultValues>();


            var xmlFilename = $"{Assembly.GetExecutingAssembly().GetName().Name}.xml";
            options.IncludeXmlComments(Path.Combine(AppContext.BaseDirectory, xmlFilename));

            // integrate xml comments
            //options.IncludeXmlComments(xmlFilename);

        });

Sdk.CreateTracerProviderBuilder()
    .SetResourceBuilder(
        ResourceBuilder.CreateDefault().AddService(
            builder.Configuration.GetValue<string>("ServiceName"),
            serviceNamespace: "GeoLocation",
            autoGenerateServiceInstanceId: false)
        .AddTelemetrySdk())
    .AddXRayTraceId()
    .AddAWSInstrumentation()
    .AddAspNetCoreInstrumentation()
    .AddHttpClientInstrumentation()
    .AddOtlpExporter(otlpOptions =>
    {
        otlpOptions.Endpoint = new Uri(builder.Configuration.GetValue<string>("OTEL_OTLP_ENDPOINT"));
    })
    .AddConsoleExporter()
    .Build();


Sdk.SetDefaultTextMapPropagator(new AWSXRayPropagator());

builder.Services.AddSingleton<IGeoLocationService, GeoLocationService>();
builder.Services.Configure<AppSettings>(builder.Configuration.GetSection("DBSettings"));
builder.Services.Configure<AppSettings>(builder.Configuration.GetSection("ProxyInformation"));
builder.Services.AddHealthChecks()
    .AddTypeActivatedCheck<GeoLocationHealthCheck>(
        "GeoLocationHealthCheck",
        args: new object[] {
            builder.Configuration.GetValue<string>("HealtcheckBaseURL"),
            builder.Configuration.GetValue<string>("HealtcheckIPToTest")
            }
        );

builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders =
    ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    options.KnownNetworks.Add(new IPNetwork(IPAddress.Parse(builder.Configuration["ProxyInformation:ProxyKnownNetwork"]), Int32.Parse(builder.Configuration["ProxyInformation:ProxyKnownNetworkCIDR"])));
    options.ForwardLimit = null;

});

var app = builder.Build();

var apiVersionDescriptionProvider = app.Services.GetRequiredService<IApiVersionDescriptionProvider>();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseDeveloperExceptionPage();
}
app.UseSwagger();

app.UseSwaggerUI(
    options =>
    {
        // build a swagger endpoint for each discovered API version
        foreach (var description in apiVersionDescriptionProvider.ApiVersionDescriptions)
        {
            options.SwaggerEndpoint($"/swagger/{description.GroupName}/swagger.json", description.GroupName.ToUpperInvariant());
        }
    });

app.UseForwardedHeaders();

app.UseAuthorization();

app.MapControllers();

app.MapHealthChecks("/hc", new HealthCheckOptions
{
    ResponseWriter = WriteResponse
});

app.Run();

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
        jsonWriter.WriteString("Architecture", RuntimeInformation.ProcessArchitecture.ToString());
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


