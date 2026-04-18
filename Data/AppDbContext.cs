using GlobalJobHunter.Service.Models;
using Microsoft.EntityFrameworkCore;

namespace GlobalJobHunter.Service.Data;

public sealed class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    public DbSet<JobRecord> JobRecords => Set<JobRecord>();
    public DbSet<AppUser>   AppUsers   => Set<AppUser>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // ── JobRecords ──────────────────────────────────────────────
        modelBuilder.Entity<JobRecord>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.Url).IsUnique();
            entity.HasIndex(e => e.PostedDate);
            entity.Property(e => e.Title).IsRequired();
            entity.Property(e => e.Company).IsRequired();
            entity.Property(e => e.SourcePlatform).IsRequired();
            entity.Property(e => e.Url).IsRequired();
        });

        // ── AppUsers ────────────────────────────────────────────────
        modelBuilder.Entity<AppUser>(entity =>
        {
            entity.HasKey(e => e.ChatId);           // ChatId is the PK
            entity.HasIndex(e => e.IsActive);        // fast query: WHERE IsActive = 1
            entity.Property(e => e.ChatId).IsRequired();
        });
    }
}
