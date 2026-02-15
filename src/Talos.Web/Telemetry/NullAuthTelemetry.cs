namespace Talos.Web.Telemetry;

/// <summary>
/// No-op implementation of IAuthTelemetry used when Application Insights is not configured.
/// </summary>
public class NullAuthTelemetry : IAuthTelemetry
{
    public void TrackAuthorizationStarted(string clientId, string? scope, string responseType) { }
    public void TrackAuthorizationCompleted(string clientId, string identityProvider) { }
    public void TrackAuthorizationDenied(string clientId, string reason) { }
    public void TrackTokenIssued(string grantType, string? scope) { }
    public void TrackTokenRefreshed(string clientId) { }
    public void TrackTokenRevoked(bool found) { }
    public void TrackTokenIntrospected(bool active) { }
}
