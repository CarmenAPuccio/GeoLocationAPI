# GeoLocationAPI
.NET Web API for retrieving geolocations. Geolocation data provided by MaxMind GeoLite2.

## Prerequisites
To get started you will need:
- [Docker](https://docs.docker.com/install/) installed on your local machine.
- [.NET](https://dotnet.microsoft.com/)
- An IDE for building .NET applications such as [Visual Studio](https://visualstudio.microsoft.com/)
## Running Locally

To get started, simply run the application in an IDE such as Visual Studio or issue the following command from the root of the project:
```sh
dotnet run --project GeoLocationAPI/GeoLocationAPI.csproj
```
Sample URL's:
- Swagger Definition: 
  - http://localhost:5254/swagger/index.html
- IPAddress of your machine:
  - http://localhost:5254/api/v1/geolocation/
- Look up an IPAddress:
  -  http://localhost:5254/api/v1/geolocation/8.8.8.8
- Healthcheck:
  - http://localhost:5254/hc

You can also run the project in Docker by running:
```sh
docker-compose -f docker-compose.yml -f docker-compose.development.yml up
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
You can check the healthcheck by issuing something like this. Note that it will return *ProcessArchitecture* which is useful to see if the workload is running on Arm64 or X64. The HealtcheckIPToTest (in this case 8.8.8.8) is set in the appsettings.json files:

```sh
curl -X GET "http://localhost:5254/hc" -v
```

Sample Payload:
```
{
	"Status": "Healthy",
	"Duration": "00:00:00.0084379",
	"FrameworkDescription": ".NET 6.0.12",
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
## Running on ECS Fargate

The [AWS CDK](https://aws.amazon.com/cdk/) is used to deploy the application to [ECS Fargate](https://aws.amazon.com/fargate/) and is protected with [AWS WAF](https://aws.amazon.com/waf/) via the CDK for C#. Follow the instructions in the [README.md](CdkGeoLocationApi/README.md).

Alternatively, you can use the [Docker Compose for Amazon ECS](https://docs.docker.com/cloud/ecs-integration/) integration to launch the application to ECS Fargate by using the Docker CLI. You can look at [docker-compose-ecs-demo.yml](docker-compose-ecs-demo.yml) to see a simple example. **Note:** the GeoLocationAPI project uses [OpenTelemetry](https://opentelemetry.io/) and since the Docker Compose for Amazon ECS integration currently doesn't support creating sidecars in the task definition, this simple example doesn't showcase the [aws-otel-collector](https://github.com/aws-observability/aws-otel-collector). To see that functionality, deploy with the CDK instead as mentioned above in the [README.md](CdkGeoLocationApi/README.md).
## Running on Kubernetes

To deploy to an existing Kubernetes cluster:

```sh
kubectl apply -f templates/
```

The [geolocationapi.yaml](/templates/geolocationapi.yaml) manifest doesn't expose the service via a load balancer so in order to test do something like this:

```sh
kubectl exec -it busybox -n default -- wget -qO- http://{Service-IP}:{Port}/api/v1/geolocation/8.8.8.8
```
