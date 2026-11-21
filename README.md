# GeoLocationAPI

A .NET 10 Web API that returns the location of an IP address, using the [MaxMind GeoLite2](https://dev.maxmind.com/geoip/geolite2-free-geolocation-data) City database that ships with the app. It runs locally, in containers, on Amazon ECS Fargate (deployed with the AWS CDK and protected by AWS WAF) or on Kubernetes, and sends OpenTelemetry traces, metrics and logs to AWS X-Ray and Amazon CloudWatch.

## API

| Request | Returns |
| --- | --- |
| `GET /api/v1/geolocation` | The location of the caller's IP address |
| `GET /api/v1/geolocation/{ipAddress}` | The location of the given IPv4 or IPv6 address |
| `GET /hc` | Health check, including the runtime and CPU architecture |
| `GET /scalar/v1` and `GET /openapi/v1.json` | Interactive API reference and OpenAPI document (Development only) |

A successful lookup returns `200`:

```json
{
	"date": "2026-11-20T21:45:36.1203497Z",
	"ipAddress": "71.168.176.139",
	"city": "Trenton",
	"timeZone": "America/New_York",
	"continent": "North America",
	"country": "United States",
	"ipFoundInGeoDB": true,
	"message": "71.168.176.139 found in the GeoDB"
}
```

`date` is the UTC time of the lookup. `city`, `timeZone`, `continent` and `country` are `null` when the database has no value for the address (8.8.8.8 has no city, for example).

A valid address that is not in the database, such as a private address, is not an error. It returns `200` with `ipFoundInGeoDB` set to `false`.

A value that is not a valid IPv4 or IPv6 address returns `400` with an [RFC 9457](https://www.rfc-editor.org/rfc/rfc9457) problem details body. Unknown routes and server errors use the same format. The `traceId` matches the request's trace:

```json
{
	"type": "https://tools.ietf.org/html/rfc9110#section-15.5.1",
	"title": "Invalid IP address",
	"status": 400,
	"detail": "'not-an-ip' is not a valid IPv4 or IPv6 address.",
	"traceId": "00-98811f84a13aa0997da03855facb27d4-b2cbae14a509d6d2-03",
	"ipAddress": "not-an-ip"
}
```

The health check looks up the address in `HealthCheck:IpToTest` and is healthy only if the database returns a result. `ProcessArchitecture` shows whether the workload runs on Arm64 or X64:

```json
{
	"Status": "Healthy",
	"Duration": "00:00:00.0001122",
	"FrameworkDescription": ".NET 10.0.15",
	"ProcessArchitecture": "Arm64",
	"Results": {
		"GeoLocationHealthCheck": {
			"Status": "Healthy",
			"Description": "The healthcheck is healthy - 8.8.8.8 found in the GeoDB",
			"Data": { "ipAddress": "8.8.8.8" }
		}
	}
}
```

## How it works

- **Lookups.** One memory-mapped MaxMind reader is opened at startup and shared by all requests. The app refuses to start if the database file is missing.
- **IP parsing.** Input must be a full IPv4 address (four parts) or an IPv6 address. Shorthand such as `127.1` and IPv6 scope IDs such as `fe80::1%eth0` are rejected. A caller address of the form `::ffff:a.b.c.d` is looked up as IPv4.
- **Behind a load balancer.** `X-Forwarded-For` and `X-Forwarded-Proto` are honored only when the request comes from the network in `ProxyInformation`. From anywhere else the headers are ignored, so a client cannot spoof its address.
- **Versioning.** The version is part of the URL (`/api/v1/...`). Responses carry the `api-supported-versions` header, and each version has its own OpenAPI document.
- **The database.** `GeoLocationAPI/GeoLite2-City.mmdb` is bundled and must stay in the repository. GeoLite2 is created by MaxMind and subject to its license. To refresh it, download the GeoLite2 City database from MaxMind (a free account is required) and replace the file.

## Prerequisites

- The [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0) (LTS). `global.json` pins the SDK major version.
- To run containers: [Finch](https://github.com/runfinch/finch) (run `finch vm init` once to create its VM). Docker works too, replace `finch` with `docker`.

## Run locally

```sh
dotnet run --project GeoLocationAPI/GeoLocationAPI.csproj
```

The API listens on http://localhost:5254. Open the API reference at http://localhost:5254/scalar/v1, or try it from the command line:

```sh
curl http://localhost:5254/api/v1/geolocation/8.8.8.8
curl http://localhost:5254/api/v1/geolocation/not-an-ip -i
curl http://localhost:5254/hc
```

## Run in containers

```sh
finch compose -f compose.yaml -f compose.development.yaml up --build
```

This starts the API and a local OpenTelemetry collector that prints the telemetry it receives to its log (`finch logs geolocationapi-aws-otel-collector-1`). The development file sets the Development environment, which enables the API reference. Without it, the API runs as Production does on ECS.

The images:
- `GeoLocationAPI/Dockerfile` builds the API on the .NET 10 SDK image and runs it on `aspnet:10.0-alpine` as a non-root user on port 5254, with a health check on `/hc`. The build context is the repository root, because `Directory.Build.props`, `Directory.Packages.props` and `global.json` live there.
- `aws-otel-collector/Dockerfile` adds two configurations to the AWS OpenTelemetry collector: `local.yaml` (prints telemetry, needs no AWS credentials) and `ecs-resource-detection.yaml` (exports to X-Ray, CloudWatch metrics and CloudWatch Logs).

## Configuration

Settings come from the `appsettings*.json` files and can be overridden with environment variables (a double underscore replaces the colon, for example `HealthCheck__IpToTest`). The app validates them at startup and stops with a clear message if one is missing or invalid.

| Setting | Purpose |
| --- | --- |
| `DBSettings:GeoLite2CityDB` | Path to the GeoLite2 City database. A relative path is resolved against the application directory. |
| `ProxyInformation:ProxyKnownNetwork` and `ProxyKnownNetworkCIDR` | The network of the load balancer whose forwarded headers are trusted. It must match the VPC CIDR in the CDK app. |
| `HealthCheck:IpToTest` | The address the `/hc` health check looks up. |

Telemetry is configured with the standard OpenTelemetry environment variables, for example `OTEL_EXPORTER_OTLP_ENDPOINT` (default `http://localhost:4317`, which is the collector sidecar on ECS and Kubernetes) and `OTEL_SERVICE_NAME` (default `GeoLocationAPI`).

## Telemetry

Everything is exported over OTLP to the collector, which forwards it to AWS. The setup follows the [OpenTelemetry .NET](https://opentelemetry.io/docs/languages/dotnet/) guidance: one SDK configuration through the host (`AddOpenTelemetry()` with `UseOtlpExporter()`), instrumentation libraries instead of hand-made spans where they exist, one shared `ActivitySource` and `Meter`, structured source-generated logging, and static attributes on the resource.

- **Resource.** Every signal carries `service.name`, `service.namespace`, `service.version`, a per-process `service.instance.id` and `deployment.environment.name`. On ECS the collector adds the task, cluster and launch type.
- **Traces.** One per API request from the ASP.NET Core instrumentation, with a `GetGeoLocation` span for the database lookup carrying `geolocation.found` (indexed in X-Ray as an annotation). Trace IDs use the X-Ray format, and the `X-Amzn-Trace-Id` header added by the load balancer is continued. The default sampler records every trace. To sample in production, set `OTEL_TRACES_SAMPLER=parentbased_traceidratio` and `OTEL_TRACES_SAMPLER_ARG=0.1` (10%).
- **Errors.** A failed request span has an Error status, and an unhandled exception is recorded on it (type, message and stack trace) before the 500 problem details response is returned. The `/hc` health check is not traced, because the load balancer calls it every few seconds and the traces would crowd out real requests.
- **Metrics.** Only an allowlist (`GeoLocationAPI/V1/Helpers/TelemetryViews.cs`) leaves the process: request duration by method, route and status code (capped at 100 series), active requests, the `geolocation.lookups` counter tagged with `geolocation.found`, and a few .NET runtime metrics (GC, CPU, memory, thread pool, exceptions). Anything else a library emits is dropped, so a library update cannot add billed metrics unnoticed. Metrics are sent as deltas (`OTEL_EXPORTER_OTLP_METRICS_TEMPORALITY_PREFERENCE` in `appsettings.json`), because the collector's CloudWatch exporter drops the first interval of a cumulative series. Health check lookups are not counted in `geolocation.lookups`.
- **Logs.** Structured and source-generated, with the trace and span ID. In Production only warnings and errors also go to the console, as a fallback if the collector is unavailable. In Development, traces are also printed to the console. The service does not log the addresses it looks up.
- **Health check failures.** They are not traced. They show up as a warning or error log from the health check, as `http.server.request.duration` with `http.route=/hc` and a 503 status, and in the ALB and ECS health signals.
- **Privacy.** The ASP.NET Core instrumentation records standard request attributes on the request span: the URL path (which contains the address being looked up) and the client address. Treat trace access and retention accordingly.

## Deploy

**ECS Fargate.** The [CDK app](CdkGeoLocationApi/README.md) deploys the API and the collector as one ARM64 Fargate task behind an Application Load Balancer, protected by AWS WAF. See its README for the architecture, the deploy commands and how to check the telemetry.

**Kubernetes.** No container images are published for this project, so build and push your own. `templates/geolocationapi.yaml` runs the API and the collector as two containers in one pod. The collector uses the ECS configuration, which exports to AWS, so the pod needs AWS credentials (for example through IAM roles for service accounts). To print telemetry instead, pass `--config=/etc/otel/local.yaml` to the collector container.

```sh
export REGISTRY=<account>.dkr.ecr.<region>.amazonaws.com   # or any registry the cluster can pull from
export TAG=latest
finch build --platform linux/arm64 -f GeoLocationAPI/Dockerfile -t $REGISTRY/geolocationapi:$TAG .
finch build --platform linux/arm64 -t $REGISTRY/aws-otel-collector:$TAG aws-otel-collector
finch push $REGISTRY/geolocationapi:$TAG
finch push $REGISTRY/aws-otel-collector:$TAG

envsubst < templates/geolocationapi.yaml | kubectl apply -f -
```

Use `linux/amd64` instead of `linux/arm64` if the nodes are x86. The manifest does not expose the service through a load balancer. To test it from inside the cluster:

```sh
kubectl exec -it busybox -n default -- wget -qO- http://{Service-IP}:{Port}/api/v1/geolocation/8.8.8.8
```

## Tests

```sh
dotnet test GeoLocationAPI.sln      # everything
./scripts/coverage.sh               # everything, and fails unless line and branch coverage are 100%
```

**API tests** (`GeoLocationAPI.Tests`) are xUnit integration tests. They start the whole API in memory with `WebApplicationFactory` (no ports, containers or network needed), call it over HTTP, and use the real GeoLite2 database. They cover:
- successful lookups, IPv6, the caller's own address (including IPv4-mapped IPv6) and valid addresses that are not in the database
- `X-Forwarded-For` honored from the load balancer network and ignored from anywhere else
- invalid addresses and unknown routes returning problem details, and the IP parser on its own
- the health check (healthy, address missing, lookup failing, its logging), API version headers, the OpenAPI document, and the Scalar page, which is absent in Production
- the app refusing to start with a missing or invalid setting
- telemetry: spans, the X-Ray trace ID format and header continuation, the metric allowlist and tags, delta temporality, the health check being neither traced nor counted, and the Production console log level

**CDK tests** (`CdkGeoLocationApi/test/CdkGeoLocationApi.Tests`) synthesize the stack and assert on the CloudFormation: task size and ARM64, the containers, the task role's permissions, log groups, the service and load balancer, the Docker build contexts and the WAF rules. They also check that the VPC CIDR matches the API's trusted proxy network and that the collector's log group names match the collector configuration.

Coverage excludes generated code, such as the OpenAPI XML comment support. Container runs (`finch compose up`) and deployments are not part of the automated tests.

## Repository layout

| Path | Contents |
| --- | --- |
| `GeoLocationAPI/` | The web API |
| `GeoLocationAPI.Tests/` | API tests |
| `CdkGeoLocationApi/` | CDK app and its tests |
| `aws-otel-collector/` | Collector image and its configurations |
| `templates/` | Kubernetes manifest |
| `compose.yaml`, `compose.development.yaml` | Local containers |
| `scripts/coverage.sh` | Runs all tests and enforces 100% coverage |
| `global.json`, `Directory.Build.props`, `Directory.Packages.props` | SDK version, shared build and analyzer settings (warnings are errors), and central NuGet package versions |
