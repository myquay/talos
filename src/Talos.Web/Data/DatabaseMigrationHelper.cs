using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Talos.Web.Data;

/// <summary>
/// Handles EF Core migration bootstrapping for databases that were created
/// outside of EF migrations (e.g. via <c>EnsureCreated</c>).
/// </summary>
public static class DatabaseMigrationHelper
{
    /// <summary>
    /// Applies any pending EF Core migrations, first seeding the migration
    /// history table when the schema already exists but was never tracked.
    /// </summary>
    public static async Task MigrateAsync(TalosDbContext db)
    {
        await SeedMigrationHistoryIfNeededAsync(db);
        await db.Database.MigrateAsync();
    }

    private const string InitialMigrationId = "20260215104808_AddClientMetadataToPendingAuth";

    /// <summary>
    /// When application tables exist but <c>__EFMigrationsHistory</c> does not
    /// contain the initial migration, creates/seeds the history table so that
    /// <see cref="RelationalDatabaseFacadeExtensions.MigrateAsync"/> skips
    /// already-applied schema changes.
    /// </summary>
    private static async Task SeedMigrationHistoryIfNeededAsync(TalosDbContext db)
    {
        var conn = db.Database.GetDbConnection();
        await conn.OpenAsync();

        try
        {
            if (!await ApplicationTablesExistAsync(conn))
                return;

            var historyExists = await MigrationHistoryExistsAsync(conn);
            if (historyExists && await MigrationIsRecordedAsync(conn))
                return;

            await CreateAndSeedHistoryTableAsync(conn, historyExists);
        }
        finally
        {
            await conn.CloseAsync();
        }
    }

    private static async Task<bool> MigrationHistoryExistsAsync(System.Data.Common.DbConnection conn)
    {
        await using var cmd = conn.CreateCommand();
        cmd.CommandText =
            "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = '__EFMigrationsHistory'";
        return Convert.ToInt64(await cmd.ExecuteScalarAsync()) > 0;
    }

    private static async Task<bool> MigrationIsRecordedAsync(System.Data.Common.DbConnection conn)
    {
        await using var cmd = conn.CreateCommand();
        cmd.CommandText =
            $"SELECT COUNT(*) FROM \"__EFMigrationsHistory\" WHERE \"MigrationId\" = '{InitialMigrationId}'";
        return Convert.ToInt64(await cmd.ExecuteScalarAsync()) > 0;
    }

    private static async Task<bool> ApplicationTablesExistAsync(System.Data.Common.DbConnection conn)
    {
        await using var cmd = conn.CreateCommand();
        cmd.CommandText =
            "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = 'AuthorizationCodes'";
        return Convert.ToInt64(await cmd.ExecuteScalarAsync()) > 0;
    }

    private static async Task CreateAndSeedHistoryTableAsync(System.Data.Common.DbConnection conn, bool historyTableExists)
    {
        await using var cmd = conn.CreateCommand();
        
        if (historyTableExists)
        {
            cmd.CommandText = $"""
                INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
                VALUES ('{InitialMigrationId}', '10.0.2');
                """;
        }
        else
        {
            cmd.CommandText = $"""
                CREATE TABLE "__EFMigrationsHistory" (
                    "MigrationId" TEXT NOT NULL PRIMARY KEY,
                    "ProductVersion" TEXT NOT NULL
                );
                INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
                VALUES ('{InitialMigrationId}', '10.0.2');
                """;
        }
        
        await cmd.ExecuteNonQueryAsync();
    }
}
