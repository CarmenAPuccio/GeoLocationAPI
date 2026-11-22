using Amazon.CDK;
using Amazon.CDK.AWS.EC2;
using Amazon.CDK.AWS.Ecr.Assets;
using Amazon.CDK.AWS.ECS;
using Amazon.CDK.AWS.ElasticLoadBalancingV2;
using Amazon.CDK.AWS.IAM;
using Amazon.CDK.AWS.Logs;
using Constructs;
using System.Collections.Generic;
using System.IO;

namespace CdkGeoLocationApi;

public sealed class CdkGeoLocationApiStack : Stack
{
    private const int ApiPort = 5254;

    /// <param name="repositoryRoot">
    /// The Docker build context for the API image. Defaults to the parent of the working directory, because
    /// <c>cdk</c> runs from the CdkGeoLocationApi directory.
    /// </param>
    /// <param name="sourceRevision">
    /// The commit to record in the API's <c>service.version</c>. Defaults to the SOURCE_REVISION_ID environment variable.
    /// Without one the version is just the product version.
    /// </param>
    internal CdkGeoLocationApiStack(Construct scope, string id, IStackProps? props = null, string? repositoryRoot = null, string? sourceRevision = null) : base(scope, id, props)
    {
        // Create the VPC
        // NOTE: Make sure the CIDR is the same as ProxyInformation in the GeoLocationAPI appsettings.Production.json
        Vpc geoLocationAPIVPC = new Vpc(this, "GeoLocationAPIVPC", new VpcProps
        {
            IpAddresses = IpAddresses.Cidr("172.25.0.0/16"),
            MaxAzs = 2
        });

        // Create the Fargate Cluster
        Cluster geoLocationAPICluster = new Cluster(this, "GeoLocationAPICluster", new ClusterProps
        {
            Vpc = geoLocationAPIVPC,
            ContainerInsightsV2 = ContainerInsights.ENABLED
        });

        // Container stdout/stderr (awslogs driver)
        LogGroup geoLocationAPILogGroup = new LogGroup(this, "GeoLocationAPILogGroup", new LogGroupProps
        {
            Retention = RetentionDays.SIX_MONTHS,
            RemovalPolicy = RemovalPolicy.DESTROY
        });

        // Log groups the OpenTelemetry collector writes to. These names must match
        // aws-otel-collector/ecs-resource-detection.yaml.
        new LogGroup(this, "OtelMetricsLogGroup", new LogGroupProps
        {
            LogGroupName = "/geolocationapi/otel/metrics",
            Retention = RetentionDays.SIX_MONTHS,
            RemovalPolicy = RemovalPolicy.DESTROY
        });

        new LogGroup(this, "OtelLogsLogGroup", new LogGroupProps
        {
            LogGroupName = "/geolocationapi/otel/logs",
            Retention = RetentionDays.SIX_MONTHS,
            RemovalPolicy = RemovalPolicy.DESTROY
        });

        // The role the containers run as. Pulling images and the awslogs driver use the task execution role,
        // which CDK creates with the permissions it needs. This role only holds what the collector needs.
        Role geoLocationAPITaskRole = new Role(this, "GeoLocationAPITaskRole", new RoleProps
        {
            AssumedBy = new ServicePrincipal("ecs-tasks.amazonaws.com"),
            Description = "Role for the GeoLocationAPI task. Lets the OpenTelemetry collector export traces, metrics and logs."
        });

        // X-Ray does not support resource-level permissions for these actions.
        geoLocationAPITaskRole.AddToPolicy(new PolicyStatement(new PolicyStatementProps
        {
            Resources = new[] { "*" },
            Actions = new[] {
                "xray:PutTraceSegments",
                "xray:PutTelemetryRecords",
                "xray:GetSamplingRules",
                "xray:GetSamplingTargets",
                "xray:GetSamplingStatisticSummaries"
            }
        }));

        geoLocationAPITaskRole.AddToPolicy(new PolicyStatement(new PolicyStatementProps
        {
            Resources = new[] {
                Arn.Format(new ArnComponents
                {
                    Service = "logs",
                    Resource = "log-group",
                    ResourceName = "/geolocationapi/otel/*",
                    ArnFormat = ArnFormat.COLON_RESOURCE_NAME
                }, this)
            },
            Actions = new[] {
                "logs:CreateLogGroup",
                "logs:CreateLogStream",
                "logs:DescribeLogStreams",
                "logs:PutLogEvents",
                "logs:PutRetentionPolicy"
            }
        }));

        // GeoLocation API Security Group - elbv2 CDK will automatically map this to the LB SG
        SecurityGroup geolocationapiSG = new SecurityGroup(this, "GeoLocationAPISG", new SecurityGroupProps
        {
            Vpc = geoLocationAPIVPC,
            AllowAllOutbound = true
        });

        // GeoLocationAPI Service task definition. The API is small, so 0.5 vCPU and 1 GiB are enough
        // for both containers (512 MiB each).
        FargateTaskDefinition geolocationAPITaskDef = new FargateTaskDefinition(this, "GeolocationAPITaskDef", new FargateTaskDefinitionProps
        {
            Family = "GeoLocationAPI",
            Cpu = 512,
            MemoryLimitMiB = 1024,
            TaskRole = geoLocationAPITaskRole,
            RuntimePlatform = new RuntimePlatform
            {
                OperatingSystemFamily = OperatingSystemFamily.LINUX,
                CpuArchitecture = CpuArchitecture.ARM64
            },
        });

        // The Docker build context is the repository root, because Directory.Build.props,
        // Directory.Packages.props and global.json live there. The .dockerignore at the root keeps
        // everything else out.
        repositoryRoot ??= Path.GetFullPath("..");
        sourceRevision ??= System.Environment.GetEnvironmentVariable("SOURCE_REVISION_ID");

        // Build the AWS OTEL Collector Container
        DockerImageAsset awsOTELCollectorAsset = new DockerImageAsset(this, "AWSOTELCollectorAsset", new DockerImageAssetProps
        {
            Directory = Path.Join(repositoryRoot, "aws-otel-collector"),
            Platform = Platform_.LINUX_ARM64,
        });

        // AWS OTEL Collector Container Definition
        ContainerDefinition awsOTELCollectorContainer = geolocationAPITaskDef.AddContainer("aws-otel-collector", new ContainerDefinitionOptions
        {
            Image = ContainerImage.FromDockerImageAsset(awsOTELCollectorAsset),
            MemoryLimitMiB = 512,
            Cpu = 128,
            Command = new[] {
                "--config=/etc/ecs/ecs-resource-detection.yaml"
            },
            Essential = true,
            Logging = new AwsLogDriver(new AwsLogDriverProps
            {
                LogGroup = geoLocationAPILogGroup,
                StreamPrefix = "aws-otel-sidecar-collector"
            }),
        });

        // Build the GeoLocationAPI Container
        DockerImageAsset geolocationAPIAsset = new DockerImageAsset(this, "GeolocationAPIAsset", new DockerImageAssetProps
        {
            Directory = repositoryRoot,
            File = "GeoLocationAPI/Dockerfile",
            Platform = Platform_.LINUX_ARM64,
            BuildArgs = string.IsNullOrWhiteSpace(sourceRevision)
                ? new Dictionary<string, string>()
                : new Dictionary<string, string> { ["SOURCE_REVISION_ID"] = sourceRevision }
        });

        // GeoLocationAPI Container Definition. The port comes from the image (ASPNETCORE_HTTP_PORTS) and the
        // OTLP endpoint defaults to http://localhost:4317, which reaches the collector container in this task.
        ContainerDefinition geolocationAPIContainer = geolocationAPITaskDef.AddContainer("GeoLocationAPI", new ContainerDefinitionOptions
        {
            Image = ContainerImage.FromDockerImageAsset(geolocationAPIAsset),
            MemoryLimitMiB = 512,
            Cpu = 384,
            Essential = true,
            Logging = new AwsLogDriver(new AwsLogDriverProps
            {
                LogGroup = geoLocationAPILogGroup,
                StreamPrefix = "geolocationapi-logs"
            }),
            Environment = new Dictionary<string, string>() {
                {"ASPNETCORE_ENVIRONMENT", "Production"}
            },
            HealthCheck = new Amazon.CDK.AWS.ECS.HealthCheck
            {
                Command = new[] {
                    "CMD-SHELL",
                    $"wget --quiet --tries=1 --spider http://localhost:{ApiPort}/hc || exit 1"
                },
                Interval = Duration.Seconds(30),
                Timeout = Duration.Seconds(5),
                Retries = 3,
                StartPeriod = Duration.Seconds(30)
            },
            PortMappings = new[] { new PortMapping { ContainerPort = ApiPort } }
        });

        geolocationAPIContainer.AddContainerDependencies(new ContainerDependency
        {
            Container = awsOTELCollectorContainer,
            Condition = ContainerDependencyCondition.START
        });

        // Create the Fargate Service
        FargateService geoLocationAPIService = new FargateService(this, "GeoLocationAPIService", new FargateServiceProps
        {
            Cluster = geoLocationAPICluster,
            DesiredCount = 2,
            TaskDefinition = geolocationAPITaskDef,
            SecurityGroups = new[] { geolocationapiSG },
            AssignPublicIp = false,
            MaxHealthyPercent = 200,
            MinHealthyPercent = 100,
            // Roll back automatically if a deployment's tasks keep failing.
            CircuitBreaker = new DeploymentCircuitBreaker { Rollback = true }
        });

        // Create ALB - elbv2 automatically creates LB security group
        ApplicationLoadBalancer lb = new ApplicationLoadBalancer(this, "LB", new ApplicationLoadBalancerProps
        {
            Vpc = geoLocationAPIVPC,
            InternetFacing = true
        });

        ApplicationListener listener = lb.AddListener("PublicListener", new BaseApplicationListenerProps { Port = 80 });

        // Attach ALB to ECS Service
        listener.AddTargets("GeoLocationAPI", new AddApplicationTargetsProps
        {
            Port = 80,
            Targets = new[] { geoLocationAPIService.LoadBalancerTarget(new LoadBalancerTargetOptions {
                ContainerName = "GeoLocationAPI",
                ContainerPort = ApiPort
            })},
            HealthCheck = new Amazon.CDK.AWS.ElasticLoadBalancingV2.HealthCheck
            {
                HealthyThresholdCount = 2,
                Interval = Duration.Seconds(5),
                Timeout = Duration.Seconds(2),
                Path = "/hc"
            },
            // Only drain containers for 10 seconds when stopping them.
            // Increase if your app has long lived connections
            DeregistrationDelay = Duration.Seconds(10)
        });

        // Create the CdkGeoLocationApiWafStack Nested Stack
        new CdkGeoLocationApiWafStack(this, "GeoLocationAPIWafStack", new ResourceNestedStackProps
        {
            ALBResourceArn = lb.LoadBalancerArn
        });

        //Output the DNS where you can access your service
        new CfnOutput(this, "GeoLocationAPI-LoadBalancerURL", new CfnOutputProps
        { Value = "http://" + lb.LoadBalancerDnsName + "/api/v1/geolocation" });
    }
}
