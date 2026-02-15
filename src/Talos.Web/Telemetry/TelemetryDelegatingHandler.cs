using System.Diagnostics;

namespace Talos.Web.Telemetry;

/// <summary>
/// Tags outbound HTTP calls with a readable dependency name for Application Insights.
/// </summary>
public class TelemetryDelegatingHandler(string dependencyName) : DelegatingHandler
{
    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Activity.Current?.SetTag("dependency.name", dependencyName);
        return await base.SendAsync(request, cancellationToken);
    }
}
