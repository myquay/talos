# 11 - Azure Table Storage Provider

## Overview

Add Azure Table Storage as an alternative storage provider to SQLite. This enables cloud-native deployments on Azure with automatic scaling and reduced operational overhead. The implementation will introduce a repository pattern to abstract data access, allowing different storage backends to be configured via app settings.

## Goals

1. **Storage Abstraction**: Introduce repository interfaces to decouple business logic from storage implementation
2. **Configuration-Driven Selection**: Allow storage provider selection via `appsettings.json`
3. **Azure Table Storage Implementation**: Full implementation supporting all existing access patterns
4. **Backward Compatibility**: SQLite remains the default; existing deployments unaffected

## Design Decisions

| Decision | Choice | Rationale |
|----------|--------|-----------|
| Abstraction approach | Repository Pattern | Clean separation; testable; supports multiple providers |
| Storage provider config | `StorageProvider` enum + type-specific settings | Simple, explicit configuration |
| Azure SDK | `Azure.Data.Tables` | Official Azure SDK; good performance |
| Partition key strategy | Entity-type based | Efficient queries; avoids hot partitions |
| Default provider | SQLite | Backward compatible; simplest setup |

---

## Entity Access Pattern Analysis

### PendingAuthenticationEntity

| Operation | Current Code Location | Query Pattern |
|-----------|----------------------|---------------|
| Create | `AuthorizationService.CreateAuthorizationAsync()` | Insert by SessionId |
| Read by SessionId | `AuthorizationService.GetPendingAuthenticationAsync()` | Lookup by SessionId + ExpiresAt > now |
| Update | `AuthorizationService.UpdatePendingAuthenticationAsync()` | Update by SessionId |
| Delete | `AuthorizationService.CreateAuthorizationCodeAsync()` | Delete by SessionId |
| Cleanup (future) | N/A | Delete where ExpiresAt < now |

**Azure Table Storage Design:**
- **Table Name:** `PendingAuthentications`
- **Partition Key:** `"pending"` (single partition for simple cleanup)
- **Row Key:** `{SessionId}`
- **Rationale:** Sessions are short-lived (~30 min); single partition allows efficient expiry cleanup

### AuthorizationCodeEntity

| Operation | Current Code Location | Query Pattern |
|-----------|----------------------|---------------|
| Create | `AuthorizationService.CreateAuthorizationCodeAsync()` | Insert by Code |
| Read by Code | `AuthorizationService.ValidateAuthorizationCodeAsync()` | Lookup by Code + IsUsed=false + ExpiresAt > now |
| Update (mark used) | `AuthorizationService.ValidateAuthorizationCodeAsync()` | Update by Code |
| Cleanup (future) | N/A | Delete where ExpiresAt < now |

**Azure Table Storage Design:**
- **Table Name:** `AuthorizationCodes`
- **Partition Key:** `"code"`
- **Row Key:** `{Code}`
- **Rationale:** Authorization codes are short-lived (~10 min); simple point lookups

### RefreshTokenEntity

| Operation | Current Code Location | Query Pattern |
|-----------|----------------------|---------------|
| Create | `TokenController.HandleAuthorizationCodeGrant()` | Insert by Token |
| Create (rotation) | `TokenController.HandleRefreshTokenGrant()` | Insert by Token |
| Read by Token | `TokenController.HandleRefreshTokenGrant()` | Lookup by Token + IsRevoked=false + ExpiresAt > now |
| Revoke by Token | `TokenController.Revoke()` | Update IsRevoked by Token |
| Revoke by ProfileUrl | `TokenController.Revoke()` (all tokens for user) | Update all where ProfileUrl matches |
| Cleanup (future) | N/A | Delete where ExpiresAt < now |

**Azure Table Storage Design:**
- **Table Name:** `RefreshTokens`
- **Partition Key:** `{ProfileUrl}` (URL-encoded)
- **Row Key:** `{Token}`
- **Rationale:** Enables efficient revocation of all tokens for a user (revoke all sessions); point lookup for token validation still efficient

---

## Azure Table Storage Table Designs (Summary)

```
┌─────────────────────────────────────────────────────────────────┐
│ Table: PendingAuthentications                                   │
├──────────────────┬─────────────────┬────────────────────────────┤
│ PartitionKey     │ RowKey          │ Properties                 │
├──────────────────┼─────────────────┼────────────────────────────┤
│ "pending"        │ {SessionId}     │ ClientId, RedirectUri,     │
│                  │                 │ State, CodeChallenge,      │
│                  │                 │ CodeChallengeMethod,       │
│                  │                 │ Scopes, ProfileUrl,        │
│                  │                 │ ProvidersJson,             │
│                  │                 │ SelectedProviderType,      │
│                  │                 │ ProviderState,             │
│                  │                 │ IsAuthenticated,           │
│                  │                 │ IsConsentGiven,            │
│                  │                 │ CreatedAt, ExpiresAt       │
└──────────────────┴─────────────────┴────────────────────────────┘

┌─────────────────────────────────────────────────────────────────┐
│ Table: AuthorizationCodes                                       │
├──────────────────┬─────────────────┬────────────────────────────┤
│ PartitionKey     │ RowKey          │ Properties                 │
├──────────────────┼─────────────────┼────────────────────────────┤
│ "code"           │ {Code}          │ ClientId, RedirectUri,     │
│                  │                 │ ProfileUrl, Scopes,        │
│                  │                 │ CodeChallenge,             │
│                  │                 │ CodeChallengeMethod,       │
│                  │                 │ CreatedAt, ExpiresAt,      │
│                  │                 │ IsUsed                     │
└──────────────────┴─────────────────┴────────────────────────────┘

┌─────────────────────────────────────────────────────────────────┐
│ Table: RefreshTokens                                            │
├──────────────────┬─────────────────┬────────────────────────────┤
│ PartitionKey     │ RowKey          │ Properties                 │
├──────────────────┼─────────────────┼────────────────────────────┤
│ {ProfileUrl}     │ {Token}         │ ClientId, Scopes,          │
│ (URL-encoded)    │                 │ CreatedAt, ExpiresAt,      │
│                  │                 │ IsRevoked                  │
└──────────────────┴─────────────────┴────────────────────────────┘
```

### Partition Key Design Rationale

| Table | Partition Key | Reasoning |
|-------|--------------|-----------|
| PendingAuthentications | `"pending"` | Short-lived data; simple cleanup; low volume |
| AuthorizationCodes | `"code"` | Short-lived data; point lookups only; low volume |
| RefreshTokens | `{ProfileUrl}` | Enables "revoke all tokens for user" query; common security operation |

---

## Implementation Phases

### Phase 1: Repository Interfaces

**New Files:**
- `src/Talos.Web/Data/Repositories/IPendingAuthenticationRepository.cs`
- `src/Talos.Web/Data/Repositories/IAuthorizationCodeRepository.cs`
- `src/Talos.Web/Data/Repositories/IRefreshTokenRepository.cs`

**Interface Definitions:**

```csharp
// IPendingAuthenticationRepository.cs
public interface IPendingAuthenticationRepository
{
    Task CreateAsync(PendingAuthenticationEntity entity);
    Task<PendingAuthenticationEntity?> GetBySessionIdAsync(string sessionId);
    Task UpdateAsync(PendingAuthenticationEntity entity);
    Task DeleteAsync(string sessionId);
    Task DeleteExpiredAsync();
}

// IAuthorizationCodeRepository.cs
public interface IAuthorizationCodeRepository
{
    Task CreateAsync(AuthorizationCodeEntity entity);
    Task<AuthorizationCodeEntity?> GetValidCodeAsync(string code);
    Task MarkAsUsedAsync(string code);
    Task DeleteExpiredAsync();
}

// IRefreshTokenRepository.cs
public interface IRefreshTokenRepository
{
    Task CreateAsync(RefreshTokenEntity entity);
    Task<RefreshTokenEntity?> GetValidTokenAsync(string token);
    Task RevokeAsync(string token);
    Task RevokeAllForProfileAsync(string profileUrl);
    Task DeleteExpiredAsync();
}
```

### Phase 2: SQLite Repository Implementations

**New Files:**
- `src/Talos.Web/Data/Repositories/Sqlite/SqlitePendingAuthenticationRepository.cs`
- `src/Talos.Web/Data/Repositories/Sqlite/SqliteAuthorizationCodeRepository.cs`
- `src/Talos.Web/Data/Repositories/Sqlite/SqliteRefreshTokenRepository.cs`

**Example Implementation:**

```csharp
public class SqlitePendingAuthenticationRepository(TalosDbContext dbContext) 
    : IPendingAuthenticationRepository
{
    public async Task CreateAsync(PendingAuthenticationEntity entity)
    {
        dbContext.PendingAuthentications.Add(entity);
        await dbContext.SaveChangesAsync();
    }

    public async Task<PendingAuthenticationEntity?> GetBySessionIdAsync(string sessionId)
    {
        return await dbContext.PendingAuthentications
            .FirstOrDefaultAsync(p => p.SessionId == sessionId && p.ExpiresAt > DateTime.UtcNow);
    }
    
    // ... other methods
}
```

### Phase 3: Configuration Model

**New Files:**
- `src/Talos.Web/Configuration/StorageSettings.cs`

**Updates:**
- `src/Talos.Web/appsettings.example.json`
- `src/Talos.Web/appsettings.Development.json`

**Configuration Schema:**

```csharp
public class StorageSettings
{
    /// <summary>
    /// The storage provider to use. Options: "Sqlite" (default), "AzureTableStorage"
    /// </summary>
    public string Provider { get; set; } = "Sqlite";
}

public class AzureTableStorageSettings
{
    /// <summary>
    /// Azure Storage connection string
    /// </summary>
    public string ConnectionString { get; set; } = "";
    
    /// <summary>
    /// Optional table name prefix (useful for multi-tenant or dev/prod separation)
    /// </summary>
    public string? TableNamePrefix { get; set; }
}
```

**appsettings.json example:**

```json
{
  "Storage": {
    "Provider": "Sqlite"
  },
  
  "ConnectionStrings": {
    "DefaultConnection": "Data Source=talos.db"
  },
  
  "AzureTableStorage": {
    "ConnectionString": "",
    "TableNamePrefix": null
  }
}
```

### Phase 4: Azure Table Storage Implementation

**New NuGet Package:**
- `Azure.Data.Tables` (add to `Talos.Web.csproj`)

**New Files:**
- `src/Talos.Web/Data/Repositories/AzureTableStorage/AzureTableStorageOptions.cs`
- `src/Talos.Web/Data/Repositories/AzureTableStorage/TableEntityMappers.cs`
- `src/Talos.Web/Data/Repositories/AzureTableStorage/AzurePendingAuthenticationRepository.cs`
- `src/Talos.Web/Data/Repositories/AzureTableStorage/AzureAuthorizationCodeRepository.cs`
- `src/Talos.Web/Data/Repositories/AzureTableStorage/AzureRefreshTokenRepository.cs`

**Table Entity Classes:**

```csharp
// Extends Azure.Data.Tables.ITableEntity
public class PendingAuthenticationTableEntity : ITableEntity
{
    public string PartitionKey { get; set; } = "pending";
    public string RowKey { get; set; } = ""; // SessionId
    public DateTimeOffset? Timestamp { get; set; }
    public ETag ETag { get; set; }
    
    // Entity properties
    public string ClientId { get; set; } = "";
    public string RedirectUri { get; set; } = "";
    public string State { get; set; } = "";
    public string? CodeChallenge { get; set; }
    public string? CodeChallengeMethod { get; set; }
    public string? Scopes { get; set; }
    public string ProfileUrl { get; set; } = "";
    public string? ProvidersJson { get; set; }
    public string? SelectedProviderType { get; set; }
    public string? ProviderState { get; set; }
    public bool IsAuthenticated { get; set; }
    public bool IsConsentGiven { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime ExpiresAt { get; set; }
}
```

**Example Repository Implementation:**

```csharp
public class AzurePendingAuthenticationRepository : IPendingAuthenticationRepository
{
    private readonly TableClient _tableClient;

    public AzurePendingAuthenticationRepository(TableServiceClient tableServiceClient, 
        IOptions<AzureTableStorageSettings> settings)
    {
        var tableName = (settings.Value.TableNamePrefix ?? "") + "PendingAuthentications";
        _tableClient = tableServiceClient.GetTableClient(tableName);
        _tableClient.CreateIfNotExists();
    }

    public async Task CreateAsync(PendingAuthenticationEntity entity)
    {
        var tableEntity = MapToTableEntity(entity);
        await _tableClient.AddEntityAsync(tableEntity);
    }

    public async Task<PendingAuthenticationEntity?> GetBySessionIdAsync(string sessionId)
    {
        try
        {
            var response = await _tableClient.GetEntityAsync<PendingAuthenticationTableEntity>(
                "pending", sessionId);
            
            if (response.Value.ExpiresAt <= DateTime.UtcNow)
                return null;
                
            return MapToEntity(response.Value);
        }
        catch (RequestFailedException ex) when (ex.Status == 404)
        {
            return null;
        }
    }
    
    // ... mapping and other methods
}
```

### Phase 5: Dependency Injection Setup

**Updates:**
- `src/Talos.Web/Program.cs`

**New File:**
- `src/Talos.Web/Extensions/StorageServiceCollectionExtensions.cs`

```csharp
public static class StorageServiceCollectionExtensions
{
    public static IServiceCollection AddTalosStorage(
        this IServiceCollection services, 
        IConfiguration configuration)
    {
        var storageSettings = configuration.GetSection("Storage").Get<StorageSettings>() 
            ?? new StorageSettings();

        if (storageSettings.Provider.Equals("AzureTableStorage", StringComparison.OrdinalIgnoreCase))
        {
            services.Configure<AzureTableStorageSettings>(
                configuration.GetSection("AzureTableStorage"));
            
            services.AddSingleton(sp =>
            {
                var settings = sp.GetRequiredService<IOptions<AzureTableStorageSettings>>();
                return new TableServiceClient(settings.Value.ConnectionString);
            });
            
            services.AddScoped<IPendingAuthenticationRepository, AzurePendingAuthenticationRepository>();
            services.AddScoped<IAuthorizationCodeRepository, AzureAuthorizationCodeRepository>();
            services.AddScoped<IRefreshTokenRepository, AzureRefreshTokenRepository>();
        }
        else // Default: SQLite
        {
            services.AddDbContext<TalosDbContext>(options =>
                options.UseSqlite(configuration.GetConnectionString("DefaultConnection")));
            
            services.AddScoped<IPendingAuthenticationRepository, SqlitePendingAuthenticationRepository>();
            services.AddScoped<IAuthorizationCodeRepository, SqliteAuthorizationCodeRepository>();
            services.AddScoped<IRefreshTokenRepository, SqliteRefreshTokenRepository>();
        }

        return services;
    }
}
```

### Phase 6: Service Layer Refactoring

**Updates:**
- `src/Talos.Web/Services/AuthorizationService.cs` - Use repositories instead of `TalosDbContext`
- `src/Talos.Web/Controllers/TokenController.cs` - Use repositories instead of `TalosDbContext`

**Example Refactor (AuthorizationService):**

```csharp
public class AuthorizationService(
    IPendingAuthenticationRepository pendingAuthRepository,
    IAuthorizationCodeRepository authCodeRepository,
    IProfileDiscoveryService profileDiscovery,
    // ... other dependencies
) : IAuthorizationService
{
    public async Task<PendingAuthentication?> GetPendingAuthenticationAsync(string sessionId)
    {
        var entity = await pendingAuthRepository.GetBySessionIdAsync(sessionId);
        if (entity == null) return null;
        
        return MapToModel(entity);
    }
    
    // ... other methods updated to use repositories
}
```

### Phase 7: Testing

**New Files:**
- `tests/Talos.Web.Tests/Repositories/SqlitePendingAuthenticationRepositoryTests.cs`
- `tests/Talos.Web.Tests/Repositories/SqliteAuthorizationCodeRepositoryTests.cs`
- `tests/Talos.Web.Tests/Repositories/SqliteRefreshTokenRepositoryTests.cs`
- `tests/Talos.Web.Tests/Repositories/AzurePendingAuthenticationRepositoryTests.cs` (using Azurite emulator)
- `tests/Talos.Web.Tests/Repositories/AzureAuthorizationCodeRepositoryTests.cs`
- `tests/Talos.Web.Tests/Repositories/AzureRefreshTokenRepositoryTests.cs`

**Updates:**
- Existing tests updated to use repository mocks instead of `TalosDbContext`

### Phase 8: Documentation Updates

**Updates:**
- `README.md` - Add Azure Table Storage configuration section
- `src/Talos.Web/appsettings.example.json` - Full example with both providers
- New file: `docs/storage-providers.md` - Detailed storage configuration guide

---

## File Changes Summary

### New Files

| File | Purpose |
|------|---------|
| `src/Talos.Web/Data/Repositories/IPendingAuthenticationRepository.cs` | Repository interface |
| `src/Talos.Web/Data/Repositories/IAuthorizationCodeRepository.cs` | Repository interface |
| `src/Talos.Web/Data/Repositories/IRefreshTokenRepository.cs` | Repository interface |
| `src/Talos.Web/Data/Repositories/Sqlite/SqlitePendingAuthenticationRepository.cs` | SQLite implementation |
| `src/Talos.Web/Data/Repositories/Sqlite/SqliteAuthorizationCodeRepository.cs` | SQLite implementation |
| `src/Talos.Web/Data/Repositories/Sqlite/SqliteRefreshTokenRepository.cs` | SQLite implementation |
| `src/Talos.Web/Data/Repositories/AzureTableStorage/AzurePendingAuthenticationRepository.cs` | Azure implementation |
| `src/Talos.Web/Data/Repositories/AzureTableStorage/AzureAuthorizationCodeRepository.cs` | Azure implementation |
| `src/Talos.Web/Data/Repositories/AzureTableStorage/AzureRefreshTokenRepository.cs` | Azure implementation |
| `src/Talos.Web/Data/Repositories/AzureTableStorage/TableEntities.cs` | Azure table entity classes |
| `src/Talos.Web/Configuration/StorageSettings.cs` | Storage configuration |
| `src/Talos.Web/Extensions/StorageServiceCollectionExtensions.cs` | DI registration |

### Modified Files

| File | Changes |
|------|---------|
| `src/Talos.Web/Talos.Web.csproj` | Add `Azure.Data.Tables` package |
| `src/Talos.Web/Program.cs` | Use `AddTalosStorage()` extension |
| `src/Talos.Web/Services/AuthorizationService.cs` | Use repository interfaces |
| `src/Talos.Web/Controllers/TokenController.cs` | Use repository interfaces |
| `src/Talos.Web/appsettings.example.json` | Add storage configuration |
| `README.md` | Document storage providers |

---

## Implementation Checklist

### Phase 1: Repository Interfaces
- [ ] Create `src/Talos.Web/Data/Repositories/` directory
- [ ] Create `IPendingAuthenticationRepository.cs`
- [ ] Create `IAuthorizationCodeRepository.cs`
- [ ] Create `IRefreshTokenRepository.cs`

### Phase 2: SQLite Repository Implementations
- [ ] Create `src/Talos.Web/Data/Repositories/Sqlite/` directory
- [ ] Create `SqlitePendingAuthenticationRepository.cs`
- [ ] Create `SqliteAuthorizationCodeRepository.cs`
- [ ] Create `SqliteRefreshTokenRepository.cs`

### Phase 3: Configuration Model
- [ ] Create `src/Talos.Web/Configuration/StorageSettings.cs`
- [ ] Create `src/Talos.Web/Configuration/AzureTableStorageSettings.cs`

### Phase 4: Azure Table Storage Implementation
- [ ] Add `Azure.Data.Tables` NuGet package to `Talos.Web.csproj`
- [ ] Create `src/Talos.Web/Data/Repositories/AzureTableStorage/` directory
- [ ] Create `TableEntities.cs` with Azure table entity classes
- [ ] Create `AzurePendingAuthenticationRepository.cs`
- [ ] Create `AzureAuthorizationCodeRepository.cs`
- [ ] Create `AzureRefreshTokenRepository.cs`

### Phase 5: Dependency Injection Setup
- [ ] Create `src/Talos.Web/Extensions/StorageServiceCollectionExtensions.cs`
- [ ] Update `Program.cs` to use `AddTalosStorage()`
- [ ] Remove direct `TalosDbContext` registration from `Program.cs` (move to extension)

### Phase 6: Service Layer Refactoring
- [ ] Update `AuthorizationService` to use repositories
- [ ] Update `TokenController` to use repositories
- [ ] Update any other direct `TalosDbContext` usages

### Phase 7: Testing
- [ ] Update existing tests to use repository mocks
- [ ] Add SQLite repository integration tests
- [ ] Add Azure Table Storage repository tests (optional, requires Azurite)

### Phase 8: Documentation
- [ ] Update `README.md` with storage provider documentation
- [ ] Update `appsettings.example.json` with full storage configuration
- [ ] Verify the application still works with default SQLite configuration
- [ ] Test Azure Table Storage configuration (if Azure resources available)

---

## Configuration Examples

### SQLite (Default)

```json
{
  "Storage": {
    "Provider": "Sqlite"
  },
  "ConnectionStrings": {
    "DefaultConnection": "Data Source=talos.db"
  }
}
```

### Azure Table Storage

```json
{
  "Storage": {
    "Provider": "AzureTableStorage"
  },
  "AzureTableStorage": {
    "ConnectionString": "DefaultEndpointsProtocol=https;AccountName=myaccount;AccountKey=...;EndpointSuffix=core.windows.net",
    "TableNamePrefix": "Talos"
  }
}
```

### Development with Azurite (Local Emulator)

```json
{
  "Storage": {
    "Provider": "AzureTableStorage"
  },
  "AzureTableStorage": {
    "ConnectionString": "UseDevelopmentStorage=true",
    "TableNamePrefix": "Dev"
  }
}
```

---

## Future Considerations

1. **Additional Providers**: The repository pattern allows easy addition of other storage providers (Cosmos DB, PostgreSQL, etc.)

2. **Token Cleanup Job**: Consider adding a hosted service to periodically clean up expired tokens/codes

3. **Caching Layer**: Repository pattern enables easy addition of a caching layer (Redis, Memory Cache) for read operations

4. **Connection Resilience**: Azure Table Storage implementation should include retry policies for transient failures

---

## References

- [Azure Tables client library for .NET](https://learn.microsoft.com/en-us/dotnet/api/overview/azure/data.tables-readme)
- [Azure Table Storage design guidelines](https://learn.microsoft.com/en-us/azure/storage/tables/table-storage-design-guidelines)
- [Azurite emulator](https://learn.microsoft.com/en-us/azure/storage/common/storage-use-azurite)

