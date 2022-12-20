# Welcome to the GeoLocationAPI CDK C# project!

This is a project for C# development with the CDK to build the GeoLocationAPI. This project uses the [AWS CDK](https://aws.amazon.com/cdk/) to deploy the application to [ECS Fargate](https://aws.amazon.com/fargate/) and is protected with [AWS WAF](https://aws.amazon.com/waf/) via the CDK for C#.

The `cdk.json` file tells the CDK Toolkit how to execute your app.

It uses the [.NET Core CLI](https://docs.microsoft.com/dotnet/articles/core/) to compile and execute your project.

## Useful commands

* `dotnet build src` compile this app
* `cdk deploy`       deploy this stack to your default AWS account/region
* `cdk diff`         compare deployed stack with current state
* `cdk synth`        emits the synthesized CloudFormation template