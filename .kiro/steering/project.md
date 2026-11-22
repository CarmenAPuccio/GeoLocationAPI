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
- Repo root: `global.json`, `Directory.Build.props`, `Directory.Packages.props`. These apply to every project, and the Docker build context is the repo root for that reason.

## Commands

- Build everything: `dotnet build GeoLocationAPI.sln`
- Test: `dotnet test GeoLocationAPI.sln`. Coverage gate: `./scripts/coverage.sh`
- Run locally: `dotnet run --project GeoLocationAPI/GeoLocationAPI.csproj` (http://localhost:5254, Scalar at `/scalar/v1`, health at `/hc`)
- Containers use Finch, not Docker: `finch compose -f compose.yaml -f compose.development.yaml up --build`
- CDK: `cd CdkGeoLocationApi && cdk synth`. Deploying uses `CDK_DOCKER=finch cdk deploy`. Do not deploy without being asked.

## Rules for changes

- Target .NET 10 (LTS). Do not move to a preview or non-LTS release unless asked.
- Warnings are errors. Fix the cause. Do not suppress analyzer rules except in an `.editorconfig` for a stated reason (see `GeoLocationAPI.Tests/.editorconfig` and `CdkGeoLocationApi/.editorconfig`).
- Package versions live only in `Directory.Packages.props`. `PackageReference` items carry no version. Check `dotnet list package --outdated --vulnerable --deprecated` after dependency changes.
- Follow current .NET conventions: built-in OpenAPI (`Microsoft.AspNetCore.OpenApi`) with Scalar, `Asp.Versioning`, options validated with `ValidateOnStart`, `LoggerMessage` source-generated logging, `AddProblemDetails()` for all errors, `TimeProvider` and `IHttpClientFactory` where relevant. When unsure, check the Microsoft Learn page for the feature and compare with `dotnet new webapi --use-controllers` from the installed SDK.
- Code scanning (CodeQL, `security-and-quality`) runs without building, so generated code is not analyzed. Fix an alert in our code rather than dismissing it.
- Run `dotnet build` and `dotnet test` after every change, and add tests with each behavior change. Line and branch coverage must stay at 100% for both test projects (`./scripts/coverage.sh`). Prefer writing the test over excluding code. `[ExcludeFromCodeCoverage]` needs a justification, as on the CDK `Main`.
- xUnit is v2 (no `TestContext.Current`). Tests that use process-wide state (telemetry listeners, the jsii runtime behind the CDK) run in serialized collections.
- Verify behavior by running it, not just by building. For container changes, run `finch compose up`. For CDK changes, run `cdk synth` and inspect the output.
- Metrics are an allowlist in `TelemetryViews.cs`. A new metric must be added there on purpose. Health checks are neither traced nor counted as lookups. Count business metrics in the controller, not the service.
- Telemetry follows the OpenTelemetry .NET best practices (https://opentelemetry.io/docs/languages/dotnet/): shared `ActivitySource` and `Meter`, source-generated structured logs with the exception as its own parameter, static attributes on the resource, span status left Unset unless failed, no addresses in the service's logs. Check the docs before adding a signal.
- Telemetry uses the standard `OTEL_*` environment variables. Do not add custom endpoint or service-name settings. The custom metric and span source are both named `GeoLocationAPI`.
- Errors are RFC 9457 problem details (`application/problem+json`). A valid IP missing from the database is a 200 with `ipFoundInGeoDB: false`, not an error.
- `GeoLocationAPI/GeoLite2-City.mmdb` must stay in the repository. The app needs it.
- No backward-compatibility constraints: nothing is deployed that depends on old setting names or response shapes.

## Backlog

- Response shape cleanup: rename `ipFoundInGeoDB` (for example to `found`), drop the redundant `message`, review `date`. Update `GeoLocationHealthCheck`, tests and README with it.
- ALB listens on HTTP only. Add HTTPS with a certificate before real use.
- Consider `cdk-nag` for the CDK app, and a scheduled job to refresh the GeoLite2 database.
