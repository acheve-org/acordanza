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

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        builder.Entity<ApplicationUser>(entity =>
        {
            entity.HasIndex(u => u.NormalizedEmail).IsUnique();
        });

        builder.Entity<ClimbingRoute>(entity =>
        {
            entity.HasIndex(r => r.Grade);
            entity.HasIndex(r => r.Crag);

            entity.HasOne(r => r.AddedBy)
                .WithMany()
                .HasForeignKey(r => r.AddedByUserId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        builder.Entity<Tick>(entity =>
        {
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
            entity.HasIndex(p => p.UserId);
            entity.HasIndex(p => p.CredentialId).IsUnique();

            entity.HasOne(p => p.User)
                .WithMany()
                .HasForeignKey(p => p.UserId)
                .OnDelete(DeleteBehavior.Cascade);
        });
    }
}
