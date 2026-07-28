using Acordanza.Api.Models;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace Acordanza.Api.Data;

public class AppDbContext(DbContextOptions<AppDbContext> options)
    : IdentityDbContext<ApplicationUser>(options)
{
    public DbSet<ClimbingRoute> Routes => Set<ClimbingRoute>();
    public DbSet<Tick> Ticks => Set<Tick>();
    public DbSet<PasskeyCredential> PasskeyCredentials => Set<PasskeyCredential>();
    public DbSet<Crag> Crags => Set<Crag>();
    public DbSet<RouteOpener> RouteOpeners => Set<RouteOpener>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        // ASP.NET Identity's own tables (AspNetUsers, AspNetRoles, ...) live in
        // "identity"; everything app-specific is carved out into "core" below.
        builder.HasDefaultSchema("identity");

        builder.Entity<ApplicationUser>(entity =>
        {
            entity.HasIndex(u => u.NormalizedEmail).IsUnique();
        });

        builder.Entity<Crag>(entity =>
        {
            entity.ToTable("Crags", "core");

            entity.HasIndex(c => c.Name);

            entity.HasOne(c => c.ParentCrag)
                .WithMany(c => c.Children)
                .HasForeignKey(c => c.ParentCragId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        builder.Entity<RouteOpener>(entity =>
        {
            entity.ToTable("RouteOpeners", "core");

            entity.HasIndex(o => o.Name);

            entity.HasOne(o => o.User)
                .WithMany()
                .HasForeignKey(o => o.UserId)
                .OnDelete(DeleteBehavior.SetNull);
        });

        builder.Entity<ClimbingRoute>(entity =>
        {
            entity.ToTable("Routes", "core");

            entity.HasIndex(r => r.Grade);
            entity.HasIndex(r => r.CragId);

            entity.HasOne(r => r.AddedBy)
                .WithMany()
                .HasForeignKey(r => r.AddedByUserId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne(r => r.Crag)
                .WithMany()
                .HasForeignKey(r => r.CragId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasMany(r => r.Openers)
                .WithMany()
                .UsingEntity<Dictionary<string, object>>(
                    "RouteRouteOpeners",
                    j => j.HasOne<RouteOpener>().WithMany().HasForeignKey("OpenerId").OnDelete(DeleteBehavior.Cascade),
                    j => j.HasOne<ClimbingRoute>().WithMany().HasForeignKey("RouteId").OnDelete(DeleteBehavior.Cascade),
                    j =>
                    {
                        j.ToTable("RouteRouteOpeners", "core");
                        j.HasKey("RouteId", "OpenerId");
                    });
        });

        builder.Entity<Tick>(entity =>
        {
            entity.ToTable("Ticks", "core");

            entity.HasIndex(t => t.RouteId);

            entity.HasOne(t => t.Route)
                .WithMany(r => r.Ticks)
                .HasForeignKey(t => t.RouteId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(t => t.Climber)
                .WithMany()
                .HasForeignKey(t => t.ClimberUserId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        builder.Entity<PasskeyCredential>(entity =>
        {
            entity.ToTable("PasskeyCredentials", "auth");

            entity.HasIndex(p => p.UserId);
            entity.HasIndex(p => p.CredentialId).IsUnique();

            entity.HasOne(p => p.User)
                .WithMany()
                .HasForeignKey(p => p.UserId)
                .OnDelete(DeleteBehavior.Cascade);
        });
    }
}
