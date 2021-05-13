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
  - http://localhost:5000/swagger/index.html
- IPAddress of your machine:
  - http://localhost:5000/api/v1/geolocation/
- Look up an IPAddress:
  -  http://localhost:5000/api/v1/geolocation/8.8.8.8

You can also run the project in Docker by running:
```sh
docker-compose -f docker-compose.yml -f docker-compose.development.yml up
```
The .NET application is fronted with a NGINX reverse proxy in the Docker implementation per this [recommendation](https://docs.microsoft.com/en-us/aspnet/core/host-and-deploy/linux-nginx?view=aspnetcore-5.0) from Microsoft. Once the container is up and running you can simply do something like this:

```sh
curl -X GET "http://localhost/api/v1/GeoLocation/8.8.8.8" -v
```
## Running on ECS Fargate

The [AWS CDK](https://aws.amazon.com/cdk/) is used to deploy the application to ECS Fargate via the CDK for C#. Follow the instructions in the [README.md](CdkGeoLocationApi/README.md).
## Running on Kubernetes

To deploy to an existing Kubernetes cluster:

```sh
kubectl apply -f templates/
```

The [geolocationapi.yaml](/templates/geolocationapi.yaml) manifest doesn't expose the service via a load balancer so in order to test do something like this:

```sh
kubectl exec -it busybox -n default -- wget -qO- http://{Service-IP}/api/v1/geolocation/8.8.8.8
```
