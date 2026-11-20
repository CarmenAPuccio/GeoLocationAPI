using Amazon.CDK;

namespace CdkGeoLocationApi
{
    internal static class Program
    {
        public static void Main()
        {
            var app = new App();
            new CdkGeoLocationApiStack(app, "CdkGeoLocationApiStack", new StackProps());
            app.Synth();
        }
    }
}
