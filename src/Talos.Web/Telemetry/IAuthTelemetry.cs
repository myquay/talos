namespace Talos.Web.Telemetry;

/// <summary>
/// Abstraction for tracking authentication-related telemetry events.
/// Implementations can send events to Application Insights, or act as a no-op.
/// </summary>
public interface IAuthTelemetry
{
    void TrackAuthorizationStarted(string clientId, string? scope, string responseType);
    void TrackAuthorizationCompleted(string clientId, string identityProvider);
    void TrackAuthorizationDenied(string clientId, string reason);
    void TrackTokenIssued(string grantType, string? scope);
    void TrackTokenRefreshed(string clientId);
    void TrackTokenRevoked(bool found);
    void TrackTokenIntrospected(bool active);
}
