using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Talos.Web.Data;

namespace Talos.Web.Tests.Data;

public class DatabaseMigrationHelperTests
{
    [Fact]
    public async Task MigrateAsync_WhenMigrationLockIsHeld_ObservesCancellation()
    {
        var databasePath = Path.Combine(Path.GetTempPath(), $"talos-migration-{Guid.NewGuid():N}.db");
        var options = new DbContextOptionsBuilder<TalosDbContext>()
            .UseSqlite($"Data Source={databasePath}")
            .Options;

        try
        {
            await using var db = new TalosDbContext(options);
            await DatabaseMigrationHelper.MigrateAsync(db);
            await db.Database.ExecuteSqlRawAsync(
                "INSERT OR REPLACE INTO \"__EFMigrationsLock\" (\"Id\", \"Timestamp\") VALUES (1, {0})",
                DateTimeOffset.UtcNow);

            using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));
            var migrate = async () => await DatabaseMigrationHelper.MigrateAsync(db, cancellation.Token);

            await migrate.Should().ThrowAsync<OperationCanceledException>();
        }
        finally
        {
            File.Delete(databasePath);
            File.Delete($"{databasePath}-shm");
            File.Delete($"{databasePath}-wal");
        }
    }
}
