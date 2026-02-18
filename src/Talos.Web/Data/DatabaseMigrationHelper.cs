using Microsoft.EntityFrameworkCore;

namespace Talos.Web.Data;

/// <summary>
/// Handles EF Core database migrations.
/// </summary>
public static class DatabaseMigrationHelper
{
    /// <summary>
    /// Applies any pending EF Core migrations.
    /// </summary>
    public static async Task MigrateAsync(TalosDbContext db)
    {
        await db.Database.MigrateAsync();
    }
}
