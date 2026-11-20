# GeoLocationAPI CDK project

This C# [AWS CDK](https://aws.amazon.com/cdk/) app deploys the GeoLocationAPI to [ECS Fargate](https://aws.amazon.com/fargate/) behind an Application Load Balancer, protected with [AWS WAF](https://aws.amazon.com/waf/). The task runs on ARM64 (Graviton) with two containers: the API and an [AWS OpenTelemetry collector](https://github.com/aws-observability/aws-otel-collector) sidecar that sends traces to X-Ray, metrics to CloudWatch and logs to CloudWatch Logs.

## Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)
- The [AWS CDK CLI](https://docs.aws.amazon.com/cdk/v2/guide/getting-started.html) and a [bootstrapped](https://docs.aws.amazon.com/cdk/v2/guide/bootstrapping.html) account and region
- A container tool to build the images. To use [Finch](https://github.com/runfinch/finch) instead of Docker, run `finch vm init` once and set `CDK_DOCKER=finch`.

## Useful commands

Run these from this directory (`CdkGeoLocationApi`). The app builds the images from the repository root, one level up.

* `dotnet build src` compile this app
* `dotnet test test/CdkGeoLocationApi.Tests` assert on the synthesized stack
* `cdk synth`        emit the synthesized CloudFormation template (needs no AWS credentials)
* `cdk diff`         compare the deployed stack with the current state
* `CDK_DOCKER=finch cdk deploy`   build the images with Finch and deploy

## Notes

- The VPC CIDR (`172.25.0.0/16`) must match `ProxyInformation` in `GeoLocationAPI/appsettings.Production.json`, because the API only trusts `X-Forwarded-For` from the load balancer's network.
- The collector log group names (`/geolocationapi/otel/metrics` and `/geolocationapi/otel/logs`) are fixed and must match `aws-otel-collector/ecs-resource-detection.yaml`. Because of the fixed names, deploy one copy of the stack per account and region.
- The load balancer listens on HTTP port 80 only. Add an HTTPS listener and certificate before using it for anything real.

## Updating a stack deployed from the older version

Run `cdk diff` first and read it. For a stack created before the .NET 10 update, expect these changes (they come from the code, so check that the diff agrees):

- **New container images.** Both images are rebuilt and pushed to the CDK bootstrap ECR repository, and the task definition gets a new revision. ECS replaces the tasks with a rolling deployment (200% max, 100% min healthy). If the new tasks keep failing health checks, the circuit breaker rolls back.
- **Task role replaced.** The old role had a fixed name (`GeoLocationAPITaskRole`) and broad permissions. The new role gets a generated name. CloudFormation creates it, switches the service and deletes the old one.
- **Log groups.** The API log group lost its fixed name, so CloudFormation creates a new one and deletes the old `/geolocationapi-logs`. The collector now writes to `/geolocationapi/otel/metrics` and `/geolocationapi/otel/logs`. The older collector created `/aws/ecs/application/metrics` itself at runtime, and this stack does not manage it. It stays after the update, so delete it once you no longer need the history.
- **Task size.** 1 vCPU and 4 GiB become 0.5 vCPU and 1 GiB.
- **WAF.** The web ACL updates in place: the allow-rule patterns are anchored and a rate limit of 300 requests per IP per 300 seconds is added (429 when exceeded).
- **Bootstrap.** If `cdk deploy` reports that the environment needs a newer bootstrap version, run `cdk bootstrap` once.

After the deploy, check that telemetry arrives (use the stack's load balancer URL to send a few requests first):

- **Traces:** the X-Ray console, or `aws xray get-trace-summaries --start-time <t> --end-time <t>`. The service name is `GeoLocationAPI`.
- **Metrics:** the `ECS/AWSOTel/Application` namespace in CloudWatch. The log group `/geolocationapi/otel/metrics` should have EMF entries.
- **Logs:** `/geolocationapi/otel/logs` for the app's OTLP logs, and the stack's API log group for the container output.
- **Collector problems:** the collector container writes its own errors to the stack's API log group under the `aws-otel-sidecar-collector` stream prefix. Access-denied errors there mean the task role is missing a permission.
