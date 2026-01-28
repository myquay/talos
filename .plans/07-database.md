# 07 - Database

## Overview

Talos uses SQLite for persistent storage with Entity Framework Core. The database stores pending authentications, authorization codes, refresh tokens, and provider OAuth state. **No user data is stored** - users are authenticated via third-party identity providers.

## Why SQLite?

1. **Zero Configuration**: No separate database server needed
2. **Single File**: Easy backup and migration
3. **Good Performance**: More than adequate for IndieAuth authorization server
4. **EF Core Support**: Full ORM support with migrations

## Schema Design

```
┌─────────────────────────────────────────┐
│         PendingAuthentications          │
├─────────────────────────────────────────┤
│ Id (PK)             VARCHAR(32)         │
│ ClientId            VARCHAR(2048)       │
│ RedirectUri         VARCHAR(2048)       │
│ State               VARCHAR(1024)       │
│ CodeChallenge       VARCHAR(128)        │
│ CodeChallengeMethod VARCHAR(10)         │
│ Scope               VARCHAR(1024) NULL  │
│ Me                  VARCHAR(2048)       │
│ DiscoveredProvidersJson TEXT            │
│ SelectedProviderJson    TEXT NULL       │
│ ProviderVerified    BOOLEAN             │
│ VerifiedAt          DATETIME NULL       │
│ VerifiedProvider    VARCHAR(50) NULL    │
│ VerifiedUsername    VARCHAR(256) NULL   │
│ CreatedAt           DATETIME            │
│ ExpiresAt           DATETIME            │
└─────────────────────────────────────────┘

┌─────────────────────────────────────────┐
│            ProviderStates               │
├─────────────────────────────────────────┤
│ State (PK)          VARCHAR(64)         │
│ PendingAuthId       VARCHAR(32)         │
│ CreatedAt           DATETIME            │
│ ExpiresAt           DATETIME            │
└─────────────────────────────────────────┘

┌─────────────────────────────────────────┐
│           AuthorizationCodes            │
├─────────────────────────────────────────┤
│ Code (PK)           VARCHAR(64)         │
│ ClientId            VARCHAR(2048)       │
│ RedirectUri         VARCHAR(2048)       │
│ Me                  VARCHAR(2048)       │
│ Scope               VARCHAR(1024) NULL  │
│ CodeChallenge       VARCHAR(128)        │
│ CodeChallengeMethod VARCHAR(10)         │
│ CreatedAt           DATETIME            │
│ ExpiresAt           DATETIME            │
│ Used                BOOLEAN             │
│ UsedAt              DATETIME NULL       │
└─────────────────────────────────────────┘

┌─────────────────────────────────────────┐
│            RefreshTokens                │
├─────────────────────────────────────────┤
│ Token (PK)          VARCHAR(64)         │
│ Me                  VARCHAR(2048)       │
│ ClientId            VARCHAR(2048)       │
│ Scope               VARCHAR(1024) NULL  │
│ CreatedAt           DATETIME            │
│ ExpiresAt           DATETIME            │
│ Revoked             BOOLEAN             │
│ RevokedAt           DATETIME NULL       │
└─────────────────────────────────────────┘
```

## Entity Models

### PendingAuthentication.cs

```csharp
// Data/Entities/PendingAuthentication.cs
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json;

namespace Talos.Web.Data.Entities;

/// <summary>
/// Stores pending authentication requests during the OAuth flow.
/// Created when authorization request is received, deleted after code is issued.
/// </summary>
public class PendingAuthentication
{
    [Key]
    [MaxLength(32)]
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    // Original IndieAuth request parameters
    [Required]
    [MaxLength(2048)]
    public string ClientId { get; set; } = "";

    [Required]
    [MaxLength(2048)]
    public string RedirectUri { get; set; } = "";

    [Required]
    [MaxLength(1024)]
    public string State { get; set; } = "";

    [Required]
    [MaxLength(128)]
    public string CodeChallenge { get; set; } = "";

    [Required]
    [MaxLength(10)]
    public string CodeChallengeMethod { get; set; } = "S256";

    [MaxLength(1024)]
    public string? Scope { get; set; }

    /// <summary>
    /// The user's profile URL (the "me" parameter)
    /// </summary>
    [Required]
    [MaxLength(2048)]
    public string Me { get; set; } = "";

    /// <summary>
    /// Discovered identity providers (serialized JSON)
    /// </summary>
    public string DiscoveredProvidersJson { get; set; } = "[]";

    [NotMapped]
    public List<DiscoveredProvider> DiscoveredProviders
    {
        get => JsonSerializer.Deserialize<List<DiscoveredProvider>>(
            DiscoveredProvidersJson) ?? new();
        set => DiscoveredProvidersJson = JsonSerializer.Serialize(value);
    }

    /// <summary>
    /// The provider selected by the user (serialized JSON)
    /// </summary>
    public string? SelectedProviderJson { get; set; }

    [NotMapped]
    public DiscoveredProvider? SelectedProvider
    {
        get => SelectedProviderJson != null
            ? JsonSerializer.Deserialize<DiscoveredProvider>(SelectedProviderJson)
            : null;
        set => SelectedProviderJson = value != null
            ? JsonSerializer.Serialize(value)
            : null;
    }

    /// <summary>
    /// Whether identity provider verification succeeded
    /// </summary>
    public bool ProviderVerified { get; set; }

    public DateTime? VerifiedAt { get; set; }

    [MaxLength(50)]
    public string? VerifiedProvider { get; set; }

    [MaxLength(256)]
    public string? VerifiedUsername { get; set; }

    public DateTime CreatedAt { get; set; }
    public DateTime ExpiresAt { get; set; }
}

/// <summary>
/// Model for discovered identity provider (stored as JSON)
/// </summary>
public class DiscoveredProvider
{
    public string ProviderType { get; set; } = "";
    public string ProfileUrl { get; set; } = "";
    public string Username { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public string? IconUrl { get; set; }
}
```

### ProviderState.cs

```csharp
// Data/Entities/ProviderState.cs
using System.ComponentModel.DataAnnotations;

namespace Talos.Web.Data.Entities;

/// <summary>
/// Maps OAuth state parameter to pending authentication.
/// This is separate from the client's state parameter.
/// </summary>
public class ProviderState
{
    [Key]
    [MaxLength(64)]
    public string State { get; set; } = "";

    [Required]
    [MaxLength(32)]
    public string PendingAuthenticationId { get; set; } = "";

    public DateTime CreatedAt { get; set; }
    public DateTime ExpiresAt { get; set; }
}
```

### AuthorizationCode.cs

```csharp
// Data/Entities/AuthorizationCode.cs
using System.ComponentModel.DataAnnotations;

namespace Talos.Web.Data.Entities;

/// <summary>
/// Authorization code issued after successful authentication and consent.
/// Short-lived (10 minutes), single-use.
/// </summary>
public class AuthorizationCode
{
    [Key]
    [MaxLength(64)]
    public string Code { get; set; } = "";

    [Required]
    [MaxLength(2048)]
    public string ClientId { get; set; } = "";

    [Required]
    [MaxLength(2048)]
    public string RedirectUri { get; set; } = "";

    /// <summary>
    /// The authenticated user's profile URL
    /// </summary>
    [Required]
    [MaxLength(2048)]
    public string Me { get; set; } = "";

    [MaxLength(1024)]
    public string? Scope { get; set; }

    [Required]
    [MaxLength(128)]
    public string CodeChallenge { get; set; } = "";

    [Required]
    [MaxLength(10)]
    public string CodeChallengeMethod { get; set; } = "S256";

    public DateTime CreatedAt { get; set; }
    public DateTime ExpiresAt { get; set; }

    public bool Used { get; set; }
    public DateTime? UsedAt { get; set; }
}
```

### RefreshToken.cs

```csharp
// Data/Entities/RefreshToken.cs
using System.ComponentModel.DataAnnotations;

namespace Talos.Web.Data.Entities;

/// <summary>
/// Long-lived refresh token for obtaining new access tokens.
/// Stored for revocation capability.
/// </summary>
public class RefreshToken
{
    [Key]
    [MaxLength(64)]
    public string Token { get; set; } = "";

    /// <summary>
    /// The user's profile URL
    /// </summary>
    [Required]
    [MaxLength(2048)]
    public string Me { get; set; } = "";

    [Required]
    [MaxLength(2048)]
    public string ClientId { get; set; } = "";

    [MaxLength(1024)]
    public string? Scope { get; set; }

    public DateTime CreatedAt { get; set; }
    public DateTime ExpiresAt { get; set; }

    public bool Revoked { get; set; }
    public DateTime? RevokedAt { get; set; }
}
```

## DbContext

```csharp
// Data/TalosDbContext.cs
using Microsoft.EntityFrameworkCore;
using Talos.Web.Data.Entities;

namespace Talos.Web.Data;

public class TalosDbContext : DbContext
{
    public TalosDbContext(DbContextOptions<TalosDbContext> options)
        : base(options)
    {
    }

    public DbSet<PendingAuthentication> PendingAuthentications => Set<PendingAuthentication>();
    public DbSet<ProviderState> ProviderStates => Set<ProviderState>();
    public DbSet<AuthorizationCode> AuthorizationCodes => Set<AuthorizationCode>();
    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // PendingAuthentication indexes
        modelBuilder.Entity<PendingAuthentication>(entity =>
        {
            entity.HasIndex(e => e.ExpiresAt)
                .HasDatabaseName("IX_PendingAuthentications_ExpiresAt");
            
            entity.HasIndex(e => e.Me)
                .HasDatabaseName("IX_PendingAuthentications_Me");
        });

        // ProviderState indexes
        modelBuilder.Entity<ProviderState>(entity =>
        {
            entity.HasIndex(e => e.ExpiresAt)
                .HasDatabaseName("IX_ProviderStates_ExpiresAt");
            
            entity.HasIndex(e => e.PendingAuthenticationId)
                .HasDatabaseName("IX_ProviderStates_PendingAuthId");
        });

        // AuthorizationCode indexes
        modelBuilder.Entity<AuthorizationCode>(entity =>
        {
            entity.HasIndex(e => e.ExpiresAt)
                .HasDatabaseName("IX_AuthorizationCodes_ExpiresAt");
            
            entity.HasIndex(e => e.ClientId)
                .HasDatabaseName("IX_AuthorizationCodes_ClientId");
        });

        // RefreshToken indexes
        modelBuilder.Entity<RefreshToken>(entity =>
        {
            entity.HasIndex(e => e.Me)
                .HasDatabaseName("IX_RefreshTokens_Me");
            
            entity.HasIndex(e => e.ClientId)
                .HasDatabaseName("IX_RefreshTokens_ClientId");
            
            entity.HasIndex(e => e.ExpiresAt)
                .HasDatabaseName("IX_RefreshTokens_ExpiresAt");
            
            entity.HasIndex(e => new { e.Revoked, e.ExpiresAt })
                .HasDatabaseName("IX_RefreshTokens_Revoked_ExpiresAt");
        });
    }
}
```

## Program.cs Configuration

```csharp
// In Program.cs

// Add Entity Framework with SQLite
builder.Services.AddDbContext<TalosDbContext>(options =>
{
    var connectionString = builder.Configuration.GetConnectionString("DefaultConnection")
        ?? "Data Source=talos.db";
    options.UseSqlite(connectionString);
});

// Apply migrations on startup (development)
if (app.Environment.IsDevelopment())
{
    using var scope = app.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<TalosDbContext>();
    db.Database.Migrate();
}
```

## Migrations

### Create Initial Migration

```bash
cd src/Talos.Web
dotnet ef migrations add InitialCreate
dotnet ef database update
```

## Cleanup Service

Periodic cleanup of expired data:

```csharp
// Services/CleanupService.cs
public class CleanupService : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<CleanupService> _logger;
    private readonly TimeSpan _interval = TimeSpan.FromHours(1);

    public CleanupService(
        IServiceProvider serviceProvider,
        ILogger<CleanupService> logger)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await CleanupExpiredDataAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during cleanup");
            }

            await Task.Delay(_interval, stoppingToken);
        }
    }

    private async Task CleanupExpiredDataAsync()
    {
        using var scope = _serviceProvider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TalosDbContext>();
        var now = DateTime.UtcNow;

        // Delete expired pending authentications
        var expiredPending = await db.PendingAuthentications
            .Where(p => p.ExpiresAt < now)
            .ToListAsync();

        if (expiredPending.Any())
        {
            db.PendingAuthentications.RemoveRange(expiredPending);
            _logger.LogInformation("Deleted {Count} expired pending authentications", 
                expiredPending.Count);
        }

        // Delete expired provider states
        var expiredStates = await db.ProviderStates
            .Where(s => s.ExpiresAt < now)
            .ToListAsync();

        if (expiredStates.Any())
        {
            db.ProviderStates.RemoveRange(expiredStates);
            _logger.LogInformation("Deleted {Count} expired provider states", 
                expiredStates.Count);
        }

        // Delete expired authorization codes
        var expiredCodes = await db.AuthorizationCodes
            .Where(c => c.ExpiresAt < now)
            .ToListAsync();

        if (expiredCodes.Any())
        {
            db.AuthorizationCodes.RemoveRange(expiredCodes);
            _logger.LogInformation("Deleted {Count} expired authorization codes", 
                expiredCodes.Count);
        }

        // Delete old refresh tokens (expired + 7 days)
        var oldTokens = await db.RefreshTokens
            .Where(t => t.ExpiresAt < now.AddDays(-7))
            .ToListAsync();

        if (oldTokens.Any())
        {
            db.RefreshTokens.RemoveRange(oldTokens);
            _logger.LogInformation("Deleted {Count} old refresh tokens", oldTokens.Count);
        }

        await db.SaveChangesAsync();
    }
}

// Register in Program.cs
builder.Services.AddHostedService<CleanupService>();
```

## Configuration

### appsettings.json

```json
{
  "ConnectionStrings": {
    "DefaultConnection": "Data Source=talos.db"
  }
}
```

### Production Configuration

```json
{
  "ConnectionStrings": {
    "DefaultConnection": "Data Source=/data/talos.db"
  }
}
```

## Data Retention

| Table | Retention | Notes |
|-------|-----------|-------|
| PendingAuthentications | 30 minutes | Deleted after code issued or expired |
| ProviderStates | 10 minutes | Deleted after OAuth callback or expired |
| AuthorizationCodes | 10 minutes | Single-use, deleted after exchange |
| RefreshTokens | 30 days + 7 | Kept 7 days after expiry for audit |

## Backup Strategy

```bash
#!/bin/bash
# backup-talos.sh

BACKUP_DIR="/backups/talos"
DATE=$(date +%Y%m%d_%H%M%S)
DB_PATH="/data/talos.db"

mkdir -p "$BACKUP_DIR"
sqlite3 "$DB_PATH" ".backup '$BACKUP_DIR/talos_$DATE.db'"
gzip "$BACKUP_DIR/talos_$DATE.db"
find "$BACKUP_DIR" -name "talos_*.db.gz" -mtime +30 -delete

echo "Backup completed: talos_$DATE.db.gz"
```

## Next Steps

After setting up the database:
1. Testing → [08-testing.md](./08-testing.md)
2. Deployment → [09-deployment.md](./09-deployment.md)

