# GeoLocationAPI
.NET Web API for retrieving geolocations. Geolocation data provided by MaxMind GeoLite2.

## Prerequisites
To get started you will need:
- A container tool for running the containers locally. These instructions use [Finch](https://github.com/runfinch/finch) (`finch vm init` once to create its VM). Docker works too: replace `finch` with `docker`.
- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0) (LTS). `global.json` pins the SDK major version.
- An IDE for building .NET applications such as [Visual Studio](https://visualstudio.microsoft.com/)
## Running Locally

To get started, simply run the application in an IDE such as Visual Studio or issue the following command from the root of the project:
```sh
dotnet run --project GeoLocationAPI/GeoLocationAPI.csproj
```
Sample URL's:
- API reference (Scalar, Development only):
  - http://localhost:5254/scalar/v1
- OpenAPI definition (Development only):
  - http://localhost:5254/openapi/v1.json
- IPAddress of your machine:
  - http://localhost:5254/api/v1/geolocation/
- Look up an IPAddress:
  -  http://localhost:5254/api/v1/geolocation/8.8.8.8
- Healthcheck:
  - http://localhost:5254/hc

You can also run the project in containers (the API plus a local OpenTelemetry collector that prints telemetry to its log) by running:
```sh
finch compose -f compose.yaml -f compose.development.yaml up --build
```
Once the container is up and running you can simply do something like this:

```sh
curl -X GET "http://localhost:5254/api/v1/GeoLocation/71.168.176.139" -v
```

Sample Payload:
```
{
	"date": "2021-08-12T13:45:02.2587451Z",
	"ipAddress": "71.168.176.139",
	"city": "Trenton",
	"timeZone": "America/New_York",
	"continent": "North America",
	"country": "United States",
	"ipFoundInGeoDB": true,
	"message": "71.168.176.139 found in the GeoDB"
}
```
An address that is not a valid IPv4 or IPv6 address returns HTTP 400 with an [RFC 9457](https://www.rfc-editor.org/rfc/rfc9457) problem details body. A valid address that is not in the database (for example a private address) returns 200 with `ipFoundInGeoDB` set to `false`.
```sh
curl -X GET "http://localhost:5254/api/v1/GeoLocation/not-an-ip" -v
```
Sample Payload:
```
{
	"type": "https://tools.ietf.org/html/rfc9110#section-15.5.1",
	"title": "Invalid IP address",
	"status": 400,
	"detail": "'not-an-ip' is not a valid IPv4 or IPv6 address.",
	"traceId": "00-98811f84a13aa0997da03855facb27d4-b2cbae14a509d6d2-03",
	"ipAddress": "not-an-ip"
}
```
You can check the healthcheck by issuing something like this. Note that it will return *ProcessArchitecture* which is useful to see if the workload is running on Arm64 or X64. The health check looks up the address in `HealthCheck:IpToTest` (in this case 8.8.8.8), which is set in the appsettings files:

```sh
curl -X GET "http://localhost:5254/hc" -v
```

Sample Payload:
```
{
	"Status": "Healthy",
	"Duration": "00:00:00.0084379",
	"FrameworkDescription": ".NET 10.0.15",
	"ProcessArchitecture": "Arm64",
	"Results": {
		"GeoLocationHealthCheck": {
			"Status": "Healthy",
			"Description": "The healthcheck is healthy - 8.8.8.8 found in the GeoDB",
			"Data": {}
		}
	}
}
```
## Configuration
Settings come from the `appsettings*.json` files and can be overridden with environment variables (a double underscore replaces the colon, for example `HealthCheck__IpToTest`). The app checks them at startup and fails with a clear message if one is missing or invalid.

| Setting | Purpose |
| --- | --- |
| `DBSettings:GeoLite2CityDB` | Path to the GeoLite2 City database. A relative path is resolved against the application directory. |
| `ProxyInformation:ProxyKnownNetwork` and `ProxyKnownNetworkCIDR` | The load balancer network whose `X-Forwarded-For` header is trusted. Must match the VPC CIDR in the CDK app. |
| `HealthCheck:IpToTest` | The address the `/hc` health check looks up. |

Telemetry (traces, metrics and logs over OTLP) uses the standard OpenTelemetry environment variables, for example `OTEL_EXPORTER_OTLP_ENDPOINT` (default `http://localhost:4317`) and `OTEL_SERVICE_NAME` (default `GeoLocationAPI`). In the Development environment, traces are also printed to the console. What is sent:
- **Traces:** one per API request, with a `GetGeoLocation` span for the database lookup. The `/hc` health check is not traced, because the load balancer calls it every few seconds and the traces would bury the real ones. A failing health check shows up in the logs (a warning or error from the health check) and in the request metrics (`http.route=/hc` with a 503 status).
- **Metrics:** only an allowlist (see `TelemetryViews.cs`): request duration by method, route and status code, active requests, the `geolocation.lookups` counter tagged with `geolocation.found`, and a few .NET runtime metrics (GC, CPU, memory, thread pool, exceptions). Everything else is dropped, so a library update can't add billed metrics unnoticed. Health check lookups are not counted in `geolocation.lookups`.
- **Logs:** structured, with the trace and span ID. In Production only warnings and errors are also written to the console, as a fallback if the collector is unavailable.

## Running on ECS Fargate

The [AWS CDK](https://aws.amazon.com/cdk/) is used to deploy the application to [ECS Fargate](https://aws.amazon.com/fargate/) and is protected with [AWS WAF](https://aws.amazon.com/waf/) via the CDK for C#. Follow the instructions in the [README.md](CdkGeoLocationApi/README.md).

## Running on Kubernetes
No images are published for this project, so build and push your own. The collector image uses the ECS configuration, which exports to AWS, so the pod needs AWS credentials (for example through IAM roles for service accounts) or you can swap in `local.yaml`.
```sh
export REGISTRY=<account>.dkr.ecr.<region>.amazonaws.com   # or any registry your cluster can pull from
export TAG=latest
finch build --platform linux/arm64 -f GeoLocationAPI/Dockerfile -t $REGISTRY/geolocationapi:$TAG .
finch build --platform linux/arm64 -t $REGISTRY/aws-otel-collector:$TAG aws-otel-collector
finch push $REGISTRY/geolocationapi:$TAG
finch push $REGISTRY/aws-otel-collector:$TAG
```
Use `linux/amd64` instead if the cluster nodes are x86. To deploy to an existing Kubernetes cluster:
```sh
envsubst < templates/geolocationapi.yaml | kubectl apply -f -
```
The [geolocationapi.yaml](/templates/geolocationapi.yaml) manifest doesn't expose the service via a load balancer so in order to test do something like this:

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
- invalid addresses and unknown routes returning RFC 9457 problem details, and the IP parser on its own
- the health check (healthy, address missing, lookup failing), API version headers, the OpenAPI document and the Scalar page, which is absent in Production
- the app refusing to start with a missing or invalid setting
- the custom telemetry: the span and the `geolocation.lookups` counter

**CDK tests** (`CdkGeoLocationApi/test/CdkGeoLocationApi.Tests`) synthesize the stack and assert on the CloudFormation it produces: task size and ARM64, the containers, the least-privilege task role, log groups, the service and load balancer, the Docker image build contexts, and the WAF rules. The VPC CIDR is checked against the API's trusted proxy network, and the collector log group names against the collector config.

Coverage excludes generated code (for example the OpenAPI XML comment support). Container changes are checked by running `finch compose up`, and the CDK entry point by running `cdk synth`. Neither a container run nor a deploy is part of the automated tests.
