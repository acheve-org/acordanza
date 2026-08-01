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
    public DbSet<Area> Areas => Set<Area>();
    public DbSet<FirstAscensionist> FirstAscensionists => Set<FirstAscensionist>();
    public DbSet<Topo> Topos => Set<Topo>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        // ASP.NET Identity's own tables (AspNetUsers, AspNetRoles, ...) live in
        // "identity"; everything app-specific is carved out into "core" below.
        builder.HasDefaultSchema("identity");

        builder.Entity<ApplicationUser>(entity =>
        {
            entity.HasIndex(u => u.NormalizedEmail).IsUnique();
            // Default "" (rather than leaving the column non-nullable with no
            // default) lets the AddColumn migration apply cleanly against
            // rows that predate Name; Program.cs backfills those on startup.
            entity.Property(u => u.Name).HasMaxLength(20).HasDefaultValue("");
        });

        builder.Entity<Area>(entity =>
        {
            entity.ToTable("Areas", "core");

            entity.HasIndex(a => a.Name);
            // Default "" (rather than leaving the column non-nullable with no
            // default) lets the AddColumn migration apply cleanly against
            // rows that predate Slug; Program.cs backfills those on startup.
            entity.Property(a => a.Slug).HasMaxLength(280).HasDefaultValue("");

            entity.HasOne(a => a.ParentArea)
                .WithMany(a => a.Children)
                .HasForeignKey(a => a.ParentAreaId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.OwnsMany(a => a.Polygon, p => p.ToJson());
        });

        builder.Entity<FirstAscensionist>(entity =>
        {
            entity.ToTable("FirstAscensionists", "core");

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
            entity.HasIndex(r => r.AreaId);

            entity.HasOne(r => r.AddedBy)
                .WithMany()
                .HasForeignKey(r => r.AddedByUserId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne(r => r.Area)
                .WithMany()
                .HasForeignKey(r => r.AreaId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne(r => r.ValidatedBy)
                .WithMany()
                .HasForeignKey(r => r.ValidatedByUserId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasMany(r => r.FirstAscensionists)
                .WithMany()
                .UsingEntity<Dictionary<string, object>>(
                    "RouteFirstAscensionists",
                    j => j.HasOne<FirstAscensionist>().WithMany().HasForeignKey("FirstAscensionistId").OnDelete(DeleteBehavior.Cascade),
                    j => j.HasOne<ClimbingRoute>().WithMany().HasForeignKey("RouteId").OnDelete(DeleteBehavior.Cascade),
                    j =>
                    {
                        j.ToTable("RouteFirstAscensionists", "core");
                        j.HasKey("RouteId", "FirstAscensionistId");
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

        builder.Entity<Topo>(entity =>
        {
            entity.ToTable("Topos", "core");

            entity.HasIndex(t => t.RouteId);

            entity.HasOne(t => t.Route)
                .WithMany(r => r.Topos)
                .HasForeignKey(t => t.RouteId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(t => t.UploadedBy)
                .WithMany()
                .HasForeignKey(t => t.UploadedByUserId)
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
