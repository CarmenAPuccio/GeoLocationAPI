using Amazon.CDK;

namespace CdkGeoLocationApi
{
    sealed class Program
    {
        public static void Main(string[] args)
        {
            var app = new App();
            new CdkGeoLocationApiStack(app, "CdkGeoLocationApiStack", new StackProps { });
            app.Synth();
        }
    }
}
