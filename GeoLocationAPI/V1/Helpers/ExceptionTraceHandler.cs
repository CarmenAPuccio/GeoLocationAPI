using Microsoft.AspNetCore.Diagnostics;
using System.Diagnostics;

namespace GeoLocationAPI.V1.Helpers;

/// <summary>
/// Records an unhandled exception (type, message and stack trace) on the request's span. The ASP.NET Core
/// instrumentation does not do this once the exception handler middleware has taken over, so without this
/// a failed trace shows an error status but not why. It never handles the exception itself: the response
/// is still the RFC 9457 problem details body.
/// </summary>
public sealed class ExceptionTraceHandler : IExceptionHandler
{
    public ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        Activity.Current?.AddException(exception);

        return ValueTask.FromResult(false);
    }
}
