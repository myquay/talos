using Microsoft.ApplicationInsights;

namespace Talos.Web.Telemetry;

/// <summary>
/// Sends authentication telemetry events to Application Insights via TelemetryClient.
/// </summary>
public class AppInsightsAuthTelemetry(TelemetryClient telemetryClient) : IAuthTelemetry
{
    public void TrackAuthorizationStarted(string clientId, string? scope, string responseType)
    {
        telemetryClient.TrackEvent("AuthorizationStarted", new Dictionary<string, string>
        {
            ["client_id"] = clientId,
            ["scope"] = scope ?? "",
            ["response_type"] = responseType
        });
    }

    public void TrackAuthorizationCompleted(string clientId, string identityProvider)
    {
        telemetryClient.TrackEvent("AuthorizationCompleted", new Dictionary<string, string>
        {
            ["client_id"] = clientId,
            ["identity_provider"] = identityProvider
        });
    }

    public void TrackAuthorizationDenied(string clientId, string reason)
    {
        telemetryClient.TrackEvent("AuthorizationDenied", new Dictionary<string, string>
        {
            ["client_id"] = clientId,
            ["reason"] = reason
        });
    }

    public void TrackTokenIssued(string grantType, string? scope)
    {
        telemetryClient.TrackEvent("TokenIssued", new Dictionary<string, string>
        {
            ["grant_type"] = grantType,
            ["scope"] = scope ?? ""
        });
    }

    public void TrackTokenRefreshed(string clientId)
    {
        telemetryClient.TrackEvent("TokenRefreshed", new Dictionary<string, string>
        {
            ["client_id"] = clientId
        });
    }

    public void TrackTokenRevoked(bool found)
    {
        telemetryClient.TrackEvent("TokenRevoked", new Dictionary<string, string>
        {
            ["token_found"] = found.ToString()
        });
    }

    public void TrackTokenIntrospected(bool active)
    {
        telemetryClient.TrackEvent("TokenIntrospected", new Dictionary<string, string>
        {
            ["active"] = active.ToString()
        });
    }
}
