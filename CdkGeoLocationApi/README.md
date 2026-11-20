# GeoLocationAPI CDK project

This C# [AWS CDK](https://aws.amazon.com/cdk/) app deploys the GeoLocationAPI to [ECS Fargate](https://aws.amazon.com/fargate/) behind an Application Load Balancer, protected with [AWS WAF](https://aws.amazon.com/waf/). The task runs on ARM64 (Graviton) with two containers: the API and an [AWS OpenTelemetry collector](https://github.com/aws-observability/aws-otel-collector) sidecar that sends traces to X-Ray, metrics to CloudWatch and logs to CloudWatch Logs.

## Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)
- The [AWS CDK CLI](https://docs.aws.amazon.com/cdk/v2/guide/getting-started.html) and a [bootstrapped](https://docs.aws.amazon.com/cdk/v2/guide/bootstrapping.html) account and region
- A container tool to build the images. To use [Finch](https://github.com/runfinch/finch) instead of Docker, run `finch vm init` once and set `CDK_DOCKER=finch`.

## Useful commands

Run these from this directory (`CdkGeoLocationApi`). The app builds the images from the repository root, one level up.

* `dotnet build src` compile this app
* `cdk synth`        emit the synthesized CloudFormation template (needs no AWS credentials)
* `cdk diff`         compare the deployed stack with the current state
* `CDK_DOCKER=finch cdk deploy`   build the images with Finch and deploy

## Notes

- The VPC CIDR (`172.25.0.0/16`) must match `ProxyInformation` in `GeoLocationAPI/appsettings.Production.json`, because the API only trusts `X-Forwarded-For` from the load balancer's network.
- The collector log group names (`/aws/ecs/application/metrics` and `/aws/ecs/application/logs`) are fixed and must match `aws-otel-collector/ecs-resource-detection.yaml`. Because of the fixed names, deploy one copy of the stack per account and region.
- The load balancer listens on HTTP port 80 only. Add an HTTPS listener and certificate before using it for anything real.
