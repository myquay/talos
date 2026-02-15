# Plan: Application Insights Instrumentation

## Goal

Instrument Talos with Azure Application Insights for logging, telemetry, and observability. The integration must be **optional** — only enabled when a connection string is present in configuration. When not configured, the application should behave exactly as it does today with no performance overhead.

---

## Current State

- ASP.NET Core app targeting `net10.0`
- Existing `ILogger<T>` usage in services and controllers (CallbackController, AuthorizationService, TokenService, ClientDiscoveryService, ProfileDiscoveryService, MicroformatsService, GitHubIdentityProvider)
- Standard `Logging` section in `appsettings.json`
- No telemetry or observability tooling currently configured
- Three named `HttpClient` instances: `ProfileDiscovery`, `GitHub`, `ClientDiscovery`
- EF Core with SQLite
- Rate limiting configured globally and per-endpoint

---

## Implementation Steps

### 1. Add NuGet Package

Add the Application Insights SDK for ASP.NET Core to `Talos.Web.csproj`:

```xml
<PackageReference Include="Microsoft.ApplicationInsights.AspNetCore" Version="2.*" />
```

This brings in auto-collection for requests, dependencies (HTTP, SQL), exceptions, and performance counters.

### 2. Add Configuration Section

Add an `Observability` section to `appsettings.example.json`:

```json
"Observability": {
  "ApplicationInsights": {
    "ConnectionString": ""
  }
}
```

When `ConnectionString` is empty or absent, Application Insights will not be registered. No new settings class is needed — the SDK reads from `ApplicationInsights:ConnectionString` by convention, but we'll gate registration on its presence.

### 3. Conditionally Register Application Insights in `Program.cs`

After the existing service registrations, add:

```csharp
// Application Insights — enabled only when a connection string is configured
var aiConnectionString = builder.Configuration["Observability:ApplicationInsights:ConnectionString"];
if (!string.IsNullOrWhiteSpace(aiConnectionString))
{
    builder.Services.AddApplicationInsightsTelemetry(options =>
    {
        options.ConnectionString = aiConnectionString;
    });
}
```

This ensures:
- Zero overhead when not configured (no telemetry modules loaded)
- All existing `ILogger` calls automatically flow to Application Insights when enabled
- HTTP dependency tracking for the three named `HttpClient`s works out of the box
- EF Core SQL commands are tracked automatically
- Request telemetry is captured for all controller endpoints

### 4. ~~Add a Telemetry Initializer to Enrich Data~~ (Dropped)

The v3 Application Insights SDK removed `ITelemetryInitializer`. Cloud role name defaults to the entry assembly name and can be overridden via the `OTEL_SERVICE_NAME` environment variable.

### 5. Add Custom Telemetry for Key Auth Operations via `IAuthTelemetry`

Create an `IAuthTelemetry` interface (`src/Talos.Web/Telemetry/IAuthTelemetry.cs`) with two implementations:
- `AppInsightsAuthTelemetry` — wraps `TelemetryClient.TrackEvent()` for Application Insights
- `NullAuthTelemetry` — no-op, used when Application Insights is not configured

Registration in `Program.cs` selects the correct implementation based on whether AI is enabled.

Controllers inject `IAuthTelemetry` (not `TelemetryClient` directly), which makes them fully testable without any Application Insights dependencies in tests.

| Event Name | When | Properties |
|---|---|---|
| `AuthorizationStarted` | Auth request received | `client_id`, `scope`, `response_type` |
| `AuthorizationCompleted` | Code issued | `client_id`, `identity_provider` |
| `TokenIssued` | Access token created | `grant_type`, `scope` |
| `TokenRefreshed` | Refresh token used | `client_id` |
| `TokenRevoked` | Revocation endpoint called | `token_type_hint` |
| `TokenIntrospected` | Introspection endpoint called | `active` |
| `AuthorizationDenied` | Auth request rejected | `reason` |

**Implementation approach:** Inject `IAuthTelemetry` into the relevant controllers. `AppInsightsAuthTelemetry` delegates to `TelemetryClient.TrackEvent()` with a properties dictionary. `NullAuthTelemetry` is a no-op. The DI container selects the right implementation based on config.

### 6. Track External HTTP Dependency Health

Application Insights auto-tracks `HttpClient` calls, but add meaningful names to improve readability in the Application Map:

- Tag outbound calls via a custom `DelegatingHandler` registered on the named `HttpClient`s that sets `Activity.Current?.SetTag("dependency.target", "github-api")` (or similar).

Create `src/Talos.Web/Telemetry/TelemetryDelegatingHandler.cs`:

```csharp
using System.Diagnostics;

namespace Talos.Web.Telemetry;

public class TelemetryDelegatingHandler(string dependencyName) : DelegatingHandler
{
    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Activity.Current?.SetTag("dependency.name", dependencyName);
        return await base.SendAsync(request, cancellationToken);
    }
}
```

Register on each named client:

```csharp
builder.Services.AddHttpClient("GitHub", ...)
    .AddHttpMessageHandler(() => new TelemetryDelegatingHandler("GitHub API"));
```

Only register the handler when Application Insights is enabled to avoid unnecessary allocations.

### 7. Log Filtering for Application Insights

Add an Application Insights-specific log level filter so that verbose framework logs don't inflate costs, while application logs are captured at `Information` level:

```json
"Logging": {
  "LogLevel": {
    "Default": "Information",
    "Microsoft.AspNetCore": "Warning"
  },
  "ApplicationInsights": {
    "LogLevel": {
      "Default": "Information",
      "Microsoft.AspNetCore": "Warning",
      "Microsoft.EntityFrameworkCore": "Warning"
    }
  }
}
```

### 8. Update `appsettings.example.json`

Add the `Observability` section with an empty connection string and the Application Insights log filter:

```json
"Observability": {
  "ApplicationInsights": {
    "ConnectionString": ""
  }
}
```

### 9. Health Check Telemetry (Optional Enhancement)

If a health check endpoint is added later, Application Insights availability tracking can be wired in. For now, the `/api/status` endpoint provides basic health info and will be tracked as request telemetry automatically.

---

## Files Changed

| File | Change |
|---|---|
| `src/Talos.Web/Talos.Web.csproj` | Add `Microsoft.ApplicationInsights.AspNetCore` package reference |
| `src/Talos.Web/Program.cs` | Conditional Application Insights registration + IAuthTelemetry DI + delegating handler |
| `src/Talos.Web/Telemetry/IAuthTelemetry.cs` | New — interface for auth telemetry events |
| `src/Talos.Web/Telemetry/AppInsightsAuthTelemetry.cs` | New — Application Insights implementation |
| `src/Talos.Web/Telemetry/NullAuthTelemetry.cs` | New — no-op implementation |
| `src/Talos.Web/Telemetry/TelemetryDelegatingHandler.cs` | New — tags outbound HTTP dependency names |
| `src/Talos.Web/Controllers/AuthController.cs` | Track `AuthorizationStarted` / `AuthorizationDenied` events |
| `src/Talos.Web/Controllers/CallbackController.cs` | Track `AuthorizationCompleted` event |
| `src/Talos.Web/Controllers/TokenController.cs` | Track `TokenIssued` / `TokenRefreshed` / `TokenRevoked` / `TokenIntrospected` events |
| `src/Talos.Web/appsettings.example.json` | Add `Observability` section and AI log filter |

---

## Verification

1. **Without config:** Run the app with no `Observability` section — confirm no Application Insights modules are loaded (check DI container or startup logs).
2. **With config:** Set a valid Application Insights connection string — confirm telemetry appears in the Azure portal:
   - Request telemetry for all API endpoints
   - Dependency telemetry for GitHub API and profile discovery HTTP calls
   - Custom events for auth operations
   - ILogger output flowing to traces
3. **Tests:** Existing tests should pass unchanged since Application Insights is not registered in the test host. No new test dependencies needed.

---

## Notes

- The `IAuthTelemetry` interface provides clean separation — `NullAuthTelemetry` has zero overhead when Application Insights is disabled, and `AppInsightsAuthTelemetry` delegates to `TelemetryClient` when enabled.
- Connection string approach (not instrumentation key) is used as instrumentation keys are deprecated.
- No frontend (browser) SDK is included in this plan. If client-side telemetry is desired later, the Vue app can integrate `@microsoft/applicationinsights-web` separately.
