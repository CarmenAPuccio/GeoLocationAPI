using Amazon.CDK;
using System.Diagnostics.CodeAnalysis;

namespace CdkGeoLocationApi
{
    internal static class Program
    {
        [ExcludeFromCodeCoverage(Justification = "Two-line entry point. cdk synth runs it; BuildApp is what the tests cover.")]
        public static void Main()
        {
            BuildApp().Synth();
        }

        internal static App BuildApp(IAppProps? props = null, string? repositoryRoot = null)
        {
            var app = new App(props);
            new CdkGeoLocationApiStack(app, "CdkGeoLocationApiStack", new StackProps(), repositoryRoot);
            return app;
        }
    }
}
