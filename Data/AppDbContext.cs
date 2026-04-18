using GlobalJobHunter.Service.Models;
using Microsoft.EntityFrameworkCore;

namespace GlobalJobHunter.Service.Data;

public sealed class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    public DbSet<JobRecord> JobRecords => Set<JobRecord>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
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
    }
}
