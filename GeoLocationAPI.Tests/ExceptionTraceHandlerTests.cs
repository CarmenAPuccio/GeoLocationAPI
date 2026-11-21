using GeoLocationAPI.V1.Helpers;
using Microsoft.AspNetCore.Http;
using System.Diagnostics;

namespace GeoLocationAPI.Tests;

public sealed class ExceptionTraceHandlerTests
{
    private readonly ExceptionTraceHandler _handler = new();

    [Fact]
    public async Task NeverHandlesTheException_SoTheProblemDetailsResponseStillRuns()
    {
        var handled = await _handler.TryHandleAsync(new DefaultHttpContext(), new InvalidOperationException("boom"), CancellationToken.None);

        Assert.False(handled);
    }

    [Fact]
    public async Task RecordsTheExceptionOnTheCurrentActivity()
    {
        using var listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == "ExceptionTraceHandlerTests",
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
        };
        ActivitySource.AddActivityListener(listener);
        using var source = new ActivitySource("ExceptionTraceHandlerTests");
        using var activity = source.StartActivity("request");

        await _handler.TryHandleAsync(new DefaultHttpContext(), new InvalidOperationException("boom"), CancellationToken.None);

        var recorded = Assert.Single(activity!.Events);
        Assert.Equal("exception", recorded.Name);
        Assert.Equal("boom", recorded.Tags.Single(t => t.Key == "exception.message").Value);
    }
}
