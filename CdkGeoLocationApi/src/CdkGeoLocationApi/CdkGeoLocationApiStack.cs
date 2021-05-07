using Amazon.CDK;
using Amazon.CDK.AWS.EC2;
using Amazon.CDK.AWS.ECS;
using Amazon.CDK.AWS.ElasticLoadBalancingV2;
using Amazon.CDK.AWS.IAM;
using System.IO;
using System.Collections.Generic;

namespace CdkGeoLocationApi
{
    public class CdkGeoLocationApiStack : Stack
    {
        internal CdkGeoLocationApiStack(Construct scope, string id, IStackProps props = null) : base(scope, id, props)
        {
            // Create the VPC
            // NOTE: Make sure the CIDR is the same as what's set in the appsettings.json for the GeoLocationAPI project
            Vpc geoLocationAPIVPC = new Vpc(this, "GeoLocationAPIVPC", new VpcProps
            {
                Cidr = "172.25.0.0/16",
                MaxAzs = 2
            });

            // Create the Fargate Cluster
            Cluster geoLocationAPICluster = new Cluster(this, "GeoLocationAPICluster", new ClusterProps
            {
                Vpc = geoLocationAPIVPC
            });

            // The IAM role assumed by the task and its containers
            Role geoLocationAPITaskRole = new Role(this, "GeoLocationAPITaskRole", new RoleProps
            {
                AssumedBy = new ServicePrincipal("ecs-tasks.amazonaws.com"),
                RoleName = "GeoLocationAPITaskRole",
                Description = "Role for the GeoLocationAPITaskRole Task Definition"
            });

            geoLocationAPITaskRole.AddToPolicy(new PolicyStatement(new PolicyStatementProps
            {
                Resources = new[] { "*" },
                Actions = new[] {
                    "logs:CreateLogStream",
                    "logs:CreateLogGroup",
                    "logs:DescribeLogStreams",
                    "logs:PutLogEvents"
                }
            }));

            // GeoLocation API Security Group - elbv2 CDK will automatically map this to the LB SG
            SecurityGroup geolocationapiSG = new SecurityGroup(this, "GeoLocationAPISG", new SecurityGroupProps
            {
                Vpc = geoLocationAPIVPC,
                AllowAllOutbound = true
            });

            // GeoLocationAPI Service task definition
            TaskDefinition geolocationAPITaskDef = new TaskDefinition(this, "GeolocationAPITaskDef", new TaskDefinitionProps
            {
                Family = "GeoLocationAPI",
                Compatibility = Compatibility.FARGATE,
                Cpu = "1024",
                MemoryMiB = "4096",
                TaskRole = geoLocationAPITaskRole
            });

            // GeoLocationAPI Container
            ContainerDefinition geolocationAPIContainer = geolocationAPITaskDef.AddContainer("GeoLocationAPI", new ContainerDefinitionOptions
            {
                Image = ContainerImage.FromAsset(Path.GetFullPath("../GeoLocationAPI")),
                MemoryLimitMiB = 512,
                Essential = true,
                Logging = new AwsLogDriver(new AwsLogDriverProps { StreamPrefix = "geolocationapi-logs" }),
                Environment = new Dictionary<string, string>() {
                    {"ASPNETCORE_URLS", "http://+:5000"},
                    {"ASPNETCORE_ENVIRONMENT", "Production"}
                },
                HealthCheck = new Amazon.CDK.AWS.ECS.HealthCheck
                {
                    Command = new[] {
                        "CMD-SHELL",
                        "wget --quiet --tries=1 --spider http://localhost:5000/hc || exit 1"
                        },
                    Interval = Duration.Seconds(30),
                    Timeout = Duration.Seconds(30),
                    Retries = 5,
                    StartPeriod = Duration.Seconds(3)
                },
                PortMappings = new[] { new PortMapping { ContainerPort = 5000 } }
            });

            // NGINX Reverse Proxy Container
            ContainerDefinition nginxContainer = geolocationAPITaskDef.AddContainer("NGINX-Proxy", new ContainerDefinitionOptions
            {
                Image = ContainerImage.FromAsset(Path.GetFullPath("../nginx")),
                MemoryLimitMiB = 512,
                Essential = true,
                Logging = new AwsLogDriver(new AwsLogDriverProps { StreamPrefix = "geolocationapi-logs" }),
                Environment = new Dictionary<string, string>() {
                    {"NGINX_PORT", "80"},
                    {"GEOLOCATIONAPI_HOST", "localhost"},
                    {"GEOLOCATIONAPI_PORT", "5000"}
                },
                PortMappings = new[] { new PortMapping { ContainerPort = 80 } }
            });

            nginxContainer.AddContainerDependencies(new ContainerDependency
            {
                Container = geolocationAPIContainer,
                Condition = ContainerDependencyCondition.HEALTHY
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
                MinHealthyPercent = 100
            });

            // Create ALB - elbv2 automatically creates LB security group
            ApplicationLoadBalancer lb = new ApplicationLoadBalancer(this, "LB", new ApplicationLoadBalancerProps
            {
                Vpc = geoLocationAPIVPC,
                InternetFacing = true
            });

            ApplicationListener listener = lb.AddListener("PublicListener", new BaseApplicationListenerProps { Port = 80 });

            // Attach ALB to ECS Service
            ApplicationTargetGroup geoLocationAPITargetGroup = listener.AddTargets("GeoLocationAPI", new AddApplicationTargetsProps
            {
                Port = 80,
                Targets = new[] { geoLocationAPIService.LoadBalancerTarget( new LoadBalancerTargetOptions {
                    ContainerName = "NGINX-Proxy",
                    ContainerPort = 80
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

            //Output the DNS where you can access your service
            new CfnOutput(this, "GeoLocationAPI-LoadBalancerDNS", new CfnOutputProps
            { Value = lb.LoadBalancerDnsName });
        }
    }
}
