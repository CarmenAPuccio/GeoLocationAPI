# GeoLocationAPI

.NET Web API that looks up the location of an IP address in a bundled MaxMind GeoLite2 City database. It runs on ECS Fargate (ARM64) behind an ALB with AWS WAF, deployed by a C# CDK app, and reports traces, metrics and logs through an OpenTelemetry collector sidecar.

## Layout

- `GeoLocationAPI/` the web API (controllers, `V1/Services`, `V1/Helpers/Instrumentation.cs`, `Configuration/`).
- `GeoLocationAPI.Tests/` xUnit integration tests using `WebApplicationFactory`.
- `CdkGeoLocationApi/test/CdkGeoLocationApi.Tests/` assertion tests on the synthesized CDK stack.
- `scripts/coverage.sh` runs both test projects and enforces 100% coverage.
- `CdkGeoLocationApi/` the CDK app (`src/CdkGeoLocationApi`). Run `cdk` commands from this directory.
- `aws-otel-collector/` collector image and configs: `local.yaml` (compose, prints telemetry) and `ecs-resource-detection.yaml` (ECS sidecar).
- `compose.yaml`, `compose.development.yaml` local containers.
- `templates/geolocationapi.yaml` Kubernetes manifest. Image names are `${REGISTRY}/...:${TAG}` placeholders, filled in with `envsubst`. No images are published.
- `.github/` the CodeQL workflow and its configuration.
- Repo root: `global.json`, `Directory.Build.props`, `Directory.Packages.props`, `.editorconfig` (file-scoped namespaces are enforced). These apply to every project, and the Docker build context is the repo root for that reason.

## Commands

- Build everything: `dotnet build GeoLocationAPI.sln`
- Test: `dotnet test GeoLocationAPI.sln`. Coverage gate: `./scripts/coverage.sh`
- Run locally: `dotnet run --project GeoLocationAPI/GeoLocationAPI.csproj` (http://localhost:5254, Scalar at `/scalar/v1`, health at `/hc`)
- Containers use Finch, not Docker: `finch compose -f compose.yaml -f compose.development.yaml up --build`
- CDK: `cd CdkGeoLocationApi && cdk synth`. Deploying uses `SOURCE_REVISION_ID=$(git rev-parse --short HEAD) CDK_DOCKER=finch cdk deploy`, which stamps the commit into `service.version`. Do not deploy without being asked. The stack is `CdkGeoLocationApiStack` in the CLI's default account and region, with fixed log group and WAF names, so there is one copy per account and region.

## Rules for changes

- Target .NET 10 (LTS). Do not move to a preview or non-LTS release unless asked.
- Warnings are errors. Fix the cause. Do not suppress analyzer rules except in an `.editorconfig` for a stated reason (see `GeoLocationAPI.Tests/.editorconfig` and `CdkGeoLocationApi/.editorconfig`).
- Package versions live only in `Directory.Packages.props`. `PackageReference` items carry no version. Check `dotnet list package --outdated --vulnerable --deprecated` after dependency changes.
- Follow current .NET conventions: built-in OpenAPI (`Microsoft.AspNetCore.OpenApi`) with Scalar, `Asp.Versioning`, options validated with `ValidateOnStart`, `LoggerMessage` source-generated logging, `AddProblemDetails()` for all errors, `TimeProvider` and `IHttpClientFactory` where relevant. When unsure, check the Microsoft Learn page for the feature and compare with `dotnet new webapi --use-controllers` from the installed SDK.
- Code scanning (CodeQL, `security-and-quality`) runs without building, so generated code is not analyzed. Fix an alert in our code rather than dismissing it.
- Run `dotnet build` and `dotnet test` after every change, and add tests with each behavior change. Line and branch coverage must stay at 100% for both test projects (`./scripts/coverage.sh`). Prefer writing the test over excluding code. `[ExcludeFromCodeCoverage]` needs a justification, as on the CDK `Main`.
- xUnit is v2 (no `TestContext.Current`). Tests that use process-wide state (telemetry listeners, the jsii runtime behind the CDK) run in serialized collections.
- Verify behavior by running it, not just by building. For container changes, run `finch compose up`. For CDK changes, run `cdk synth` and inspect the output. For collector config changes, start the real collector image with the config (`finch run ... --config=...`) and read its startup log.
- Metrics are an allowlist in `TelemetryViews.cs`. A new metric must be added there on purpose. Health checks are neither traced nor counted as lookups. Count business metrics in the controller, not the service.
- Telemetry follows the OpenTelemetry .NET best practices (https://opentelemetry.io/docs/languages/dotnet/): shared `ActivitySource` and `Meter`, source-generated structured logs with the exception as its own parameter, static attributes on the resource, span status left Unset unless failed, no addresses in the service's logs. Check the docs before adding a signal.
- Telemetry uses the standard `OTEL_*` environment variables. Do not add custom endpoint or service-name settings. A service name set in code overrides `OTEL_SERVICE_NAME`, so `Program.cs` reads the variable itself. The custom metric and span source are both named `GeoLocationAPI`.
- Errors are RFC 9457 problem details (`application/problem+json`). A valid IP missing from the database is a 200 with `ipFoundInGeoDB: false`, not an error.
- `GeoLocationAPI/GeoLite2-City.mmdb` must stay in the repository. The app needs it.
- No backward-compatibility constraints: a sample stack may be deployed, but nothing external depends on setting names or response shapes.

## Things that must stay in step across files

Tests enforce most of these. If you change one side, change the other.

- The VPC CIDR in `CdkGeoLocationApiStack.cs` and `ProxyInformation` in `appsettings.Production.json` (the API trusts `X-Forwarded-For` only from that network).
- The collector log group names in `aws-otel-collector/ecs-resource-detection.yaml` and the CDK stack (`/geolocationapi/otel/*`), which the task role's permissions also cover.
- Delta metric temporality (`appsettings.json`) and the collector's CloudWatch (EMF) exporter. Cumulative metrics lose their first interval there.
- The propagator order in `Program.cs`: W3C `traceparent` first, so a caller's trace wins over the `X-Amzn-Trace-Id` header the ALB always adds.
- The WAF allow-rule regexes in `CdkGeoLocationApiWafStack.cs` and the API routes. They are anchored, so a new route needs a new pattern.
- The health check path `/hc`: `TelemetryViews.HealthCheckPath`, the ALB target group, and the container health check.

## Backlog

- Response shape cleanup: rename `ipFoundInGeoDB` (for example to `found`), drop the redundant `message`, review `date`. Update `GeoLocationHealthCheck`, tests and README with it.
- ALB listens on HTTP only. AWS Continuum flagged this (rated low). ACM cannot issue a certificate for the default `*.elb.amazonaws.com` name, so HTTPS needs a domain. Plan: make it optional, adding an HTTPS listener with a modern TLS policy and a permanent redirect from port 80 only when a certificate ARN is passed as CDK context. Do not add `UseHttpsRedirection()` to the app: TLS ends at the ALB, so it would redirect the health checks and fail them.
- Consider `cdk-nag` for the CDK app, and a scheduled job to refresh the GeoLite2 database.
