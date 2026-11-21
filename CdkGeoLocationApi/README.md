# GeoLocationAPI CDK project

This C# [AWS CDK](https://aws.amazon.com/cdk/) app deploys the GeoLocationAPI to [Amazon ECS on Fargate](https://aws.amazon.com/fargate/) behind an Application Load Balancer, protected by [AWS WAF](https://aws.amazon.com/waf/). The API's traces go to AWS X-Ray, its metrics to Amazon CloudWatch and its logs to CloudWatch Logs, through an [AWS OpenTelemetry collector](https://github.com/aws-observability/aws-otel-collector) sidecar.

## What it deploys

```
Internet -> WAF -> Application Load Balancer (HTTP :80)
                        |
              Fargate service, 2 tasks, ARM64 (spread across the two zones)
                 +- GeoLocationAPI container  --OTLP-->  aws-otel-collector container
                                                            +- traces  -> X-Ray
                                                            +- metrics -> CloudWatch (EMF)
                                                            +- logs    -> CloudWatch Logs
```

- **Network.** A VPC (`172.25.0.0/16`) with two availability zones, public and private subnets, and one NAT gateway per zone. The tasks run in the private subnets with no public IP. The load balancer is internet-facing.
- **Cluster.** An ECS cluster with Container Insights enabled.
- **Task.** One Fargate task definition on ARM64 with 0.5 vCPU and 1 GiB, running two containers. The API container gets 512 MiB and the collector 512 MiB. The API starts after the collector, and the collector is essential. The API has a container health check on `/hc`.
- **Service.** Two tasks, a rolling deployment (up to 200%, at least 100% healthy) and a deployment circuit breaker that rolls back if new tasks keep failing. The load balancer health check is `/hc`, and targets drain for 10 seconds.
- **Images.** CDK builds both images when you deploy and pushes them to the bootstrap ECR repository. The task definition references them by a hash of their contents, so a change to the code or the collector configuration creates a new task definition revision. The API image is built from the repository root with `GeoLocationAPI/Dockerfile`, because the shared build files live there. The collector image is built from `aws-otel-collector/`.
- **Permissions.** The task role has only what the collector needs: the X-Ray write and sampling actions (X-Ray does not support resource-level permissions for them) and CloudWatch Logs actions limited to the `/geolocationapi/otel/*` log groups. Pulling images and writing container logs use the task execution role that CDK creates.
- **Log groups,** all with six months of retention and deleted with the stack:

  | Log group | Contents |
  | --- | --- |
  | generated name | Container output of the API and the collector (collector streams start with `aws-otel-sidecar-collector`) |
  | `/geolocationapi/otel/logs` | Application logs sent over OTLP |
  | `/geolocationapi/otel/metrics` | CloudWatch metrics in embedded metric format |
  | `aws-waf-logs-geolocationapi` | WAF request logs |

## WAF

The web ACL (a nested stack) blocks everything by default (`403`) and is evaluated in this order:

| Priority | Rule | Action |
| --- | --- | --- |
| 0 to 5 | AWS managed rule groups: Common, Known Bad Inputs, Linux, Unix, Amazon IP Reputation List, Anonymous IP List | Block what they match |
| 6 | `GeoLocationAPI-RateLimit`: more than 300 requests from one IP within 300 seconds | Block with `429` |
| 7 | `GeoLocationAPI-Regex-Rule`: the path matches `(?i)^/hc/?\z` or `(?i)^/api/v[0-9]+/geolocation(/[^/]*)?\z` | Allow |

Only the health check and the API's paths get through, so scanners probing other paths are stopped at the WAF. Anyone who knows the API path can still call it, which is what the rate limit is for. The rate is evaluated periodically, so an IP can briefly exceed the limit before it is blocked, and clients behind a shared address share one count. The allow rule is last because Allow ends evaluation.

## Prerequisites

- The [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)
- The [AWS CDK CLI](https://docs.aws.amazon.com/cdk/v2/guide/getting-started.html), and an account and region that are [bootstrapped](https://docs.aws.amazon.com/cdk/v2/guide/bootstrapping.html)
- A container tool to build the images. To use [Finch](https://github.com/runfinch/finch) instead of Docker, run `finch vm init` once and set `CDK_DOCKER=finch`.

## Commands

Run these from this directory (`CdkGeoLocationApi`). The app builds the images from the repository root, one level up.

| Command | Purpose |
| --- | --- |
| `dotnet build src` | Compile the app |
| `dotnet test test/CdkGeoLocationApi.Tests` | Assert on the synthesized stack |
| `cdk synth` | Emit the CloudFormation template (needs no AWS credentials) |
| `cdk diff` | Compare the deployed stack with the current code |
| `CDK_DOCKER=finch cdk deploy` | Build the images with Finch and deploy |
| `cdk destroy` | Delete the stack |

The stack output `GeoLocationAPILoadBalancerURL` is the URL of the API, for example `http://<load-balancer>/api/v1/geolocation`.

## Checking telemetry

Send a few requests to the load balancer URL, then look in:

- **Traces.** The X-Ray console, or `aws xray get-trace-summaries --start-time <t> --end-time <t>`. The service is `GeoLocationAPI`. Health checks are not traced. Filter by outcome with `annotation.geolocation_found = true` (or `false`).
- **Metrics.** The `ECS/AWSOTel/Application` namespace in CloudWatch: `http.server.request.duration`, `http.server.active_requests`, `geolocation.lookups` and a few `dotnet.*` metrics, and nothing else. A failing health check appears as `http.server.request.duration` with `http.route=/hc` and a 503 status. The ALB `UnHealthyHostCount` metric and the ECS service events show the same failure from the load balancer's side.
- **Logs.** `/geolocationapi/otel/logs` for the application's logs, which carry the trace and span ID. The generated log group has the container output, including the collector's own log.
- **Collector problems.** Look in the collector streams of the generated log group. An access-denied error there means the task role is missing a permission.

## Notes

- The VPC CIDR must match `ProxyInformation` in `GeoLocationAPI/appsettings.Production.json`, because the API trusts `X-Forwarded-For` only from the load balancer's network. A test checks this.
- The collector log group names are fixed and must match `aws-otel-collector/ecs-resource-detection.yaml`. A test checks this too. Because of the fixed names, deploy one copy of the stack per account and region.
- The load balancer listens on HTTP only. Add an HTTPS listener and a certificate before using it for anything real.
- If `cdk deploy` reports that the environment needs a newer bootstrap version, run `cdk bootstrap` once.

## Tests

`test/CdkGeoLocationApi.Tests` synthesizes the stack without copying any assets and asserts on the CloudFormation, so it needs no AWS credentials or container tool. It covers the VPC, cluster, task size and architecture, both containers and their dependency and health check, the task role's permissions, the log groups, the service, the load balancer, the image build contexts, and every WAF rule, including which paths the allow patterns match. The tests share one CDK runtime and run one after another.
