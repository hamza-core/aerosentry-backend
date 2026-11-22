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
