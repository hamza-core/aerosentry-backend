using AeroSentry.Api.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace AeroSentry.Api.Data;

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options)
    {
    }

    public DbSet<User> Users => Set<User>();
    public DbSet<Property> Properties => Set<Property>();
    public DbSet<PropertyGuard> PropertyGuards => Set<PropertyGuard>();
    public DbSet<Camera> Cameras => Set<Camera>();
    public DbSet<LaserGrid> LaserGrids => Set<LaserGrid>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<User>(entity =>
        {
            entity.Property(u => u.Email).HasMaxLength(256);
            entity.HasIndex(u => u.Email).IsUnique();

            // Guard -> owner Admin (self-reference). Restrict: deleting a user never
            // silently deletes the users they own.
            entity.HasOne<User>()
                  .WithMany()
                  .HasForeignKey(u => u.OwnerAdminId)
                  .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<Property>(entity =>
        {
            entity.Property(p => p.Name).HasMaxLength(200);
            entity.Property(p => p.Address).HasMaxLength(300);

            entity.HasOne(p => p.OwnerAdmin)
                  .WithMany()
                  .HasForeignKey(p => p.OwnerAdminId)
                  .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<Camera>(entity =>
        {
            entity.Property(c => c.Name).HasMaxLength(200);
            entity.Property(c => c.SourceType).HasMaxLength(20);
            entity.Property(c => c.Host).HasMaxLength(255);
            entity.Property(c => c.StreamPath).HasMaxLength(300);
            entity.Property(c => c.Username).HasMaxLength(100);
            entity.Property(c => c.Password).HasMaxLength(200);

            // No two cameras in the same property may share a name.
            entity.HasIndex(c => new { c.PropertyId, c.Name }).IsUnique();

            entity.HasOne(c => c.Property)
                  .WithMany()
                  .HasForeignKey(c => c.PropertyId)
                  .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<LaserGrid>(entity =>
        {
            entity.Property(g => g.Name).HasMaxLength(200);

            // No two laser grids in the same property may share a name.
            entity.HasIndex(g => new { g.PropertyId, g.Name }).IsUnique();

            entity.HasOne(g => g.Property)
                  .WithMany()
                  .HasForeignKey(g => g.PropertyId)
                  .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<PropertyGuard>(entity =>
        {
            // Composite primary key: the same guard cannot be assigned to the same property twice.
            entity.HasKey(pg => new { pg.PropertyId, pg.GuardId });

            entity.HasOne(pg => pg.Property)
                  .WithMany(p => p.PropertyGuards)
                  .HasForeignKey(pg => pg.PropertyId)
                  .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(pg => pg.Guard)
                  .WithMany()
                  .HasForeignKey(pg => pg.GuardId)
                  .OnDelete(DeleteBehavior.Cascade);
        });
    }
}
