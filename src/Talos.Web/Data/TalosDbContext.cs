using Microsoft.EntityFrameworkCore;
using Talos.Web.Data.Entities;

namespace Talos.Web.Data;

public class TalosDbContext : DbContext
{
    public TalosDbContext(DbContextOptions<TalosDbContext> options) : base(options)
    {
    }

    public DbSet<PendingAuthenticationEntity> PendingAuthentications => Set<PendingAuthenticationEntity>();
    public DbSet<AuthorizationCodeEntity> AuthorizationCodes => Set<AuthorizationCodeEntity>();
    public DbSet<RefreshTokenEntity> RefreshTokens => Set<RefreshTokenEntity>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<PendingAuthenticationEntity>(entity =>
        {
            entity.HasKey(e => e.SessionId);
            entity.HasIndex(e => e.ExpiresAt);
        });

        modelBuilder.Entity<AuthorizationCodeEntity>(entity =>
        {
            entity.HasKey(e => e.Code);
            entity.HasIndex(e => e.ExpiresAt);
        });

        modelBuilder.Entity<RefreshTokenEntity>(entity =>
        {
            entity.HasKey(e => e.Token);
            entity.HasIndex(e => e.ProfileUrl);
            entity.HasIndex(e => e.ExpiresAt);
        });
    }
}

