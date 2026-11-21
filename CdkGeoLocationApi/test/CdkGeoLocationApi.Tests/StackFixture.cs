using Amazon.CDK;
using Amazon.CDK.Assertions;
using System.Text.Json;

namespace CdkGeoLocationApi.Tests;

/// <summary>
/// Builds the app once and exposes the synthesized CloudFormation templates as JSON.
/// </summary>
public sealed class StackFixture : IDisposable
{
    private readonly string _outputDirectory = Path.Join(Path.GetTempPath(), "cdk-geolocationapi-tests-" + Guid.NewGuid().ToString("N"));

    public StackFixture()
    {
        RepositoryRoot = FindRepositoryRoot();

        App = Program.BuildApp(
            new AppProps
            {
                Outdir = _outputDirectory,
                // Reference the repository in place instead of copying it into cdk.out.
                Context = new Dictionary<string, object> { ["aws:cdk:disable-asset-staging"] = true },
            },
            RepositoryRoot);

        Stack = (Stack)App.Node.FindChild("CdkGeoLocationApiStack");
        Main = Parse(Template.FromStack(Stack).ToJSON());

        var nested = (NestedStack)Stack.Node.FindChild("GeoLocationAPIWafStack");
        Waf = Parse(Template.FromStack(nested).ToJSON());

        AssetManifest = JsonDocument.Parse(File.ReadAllText(Path.Join(App.Synth().Directory, "CdkGeoLocationApiStack.assets.json"))).RootElement;
    }

    public string RepositoryRoot { get; }

    public App App { get; }

    public Stack Stack { get; }

    public JsonElement Main { get; }

    public JsonElement Waf { get; }

    public JsonElement AssetManifest { get; }

    /// <summary>The Properties of every resource of a type in the main stack.</summary>
    public IReadOnlyList<JsonElement> Resources(string type) => ResourcesIn(Main, type);

    public static IReadOnlyList<JsonElement> ResourcesIn(JsonElement template, string type) =>
        template.GetProperty("Resources").EnumerateObject()
            .Where(r => r.Value.GetProperty("Type").GetString() == type)
            .Select(r => r.Value.TryGetProperty("Properties", out var p) ? p : default)
            .ToList();

    public void Dispose()
    {
        if (Directory.Exists(_outputDirectory))
        {
            Directory.Delete(_outputDirectory, recursive: true);
        }
    }

    private static JsonElement Parse(object template) => JsonDocument.Parse(JsonSerializer.Serialize(template)).RootElement;

    private static string FindRepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Join(directory.FullName, "global.json")))
            {
                return directory.FullName;
            }
        }

        throw new InvalidOperationException("global.json was not found above the test output directory.");
    }
}

/// <summary>
/// The jsii runtime behind the CDK is one process and is not safe to use from parallel tests,
/// so every CDK test shares one fixture and runs serially.
/// </summary>
[CollectionDefinition(nameof(StackCollectionDefinition))]
public sealed class StackCollectionDefinition : ICollectionFixture<StackFixture>;
