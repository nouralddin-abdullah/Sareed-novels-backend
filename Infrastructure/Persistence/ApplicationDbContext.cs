using Domain.Entities;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace Infrastructure.Persistence;

public class ApplicationDbContext(DbContextOptions<ApplicationDbContext> options) : IdentityDbContext<User>(options)
{
    internal DbSet<Novel> Novels { get; set; }
    internal DbSet<Follow> Follows { get; set; }
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<Follow>(entity =>
        {
            entity.HasKey(f => new { f.FollowerId, f.FollowedId });

            entity.HasOne(f => f.Follower)
                  .WithMany(u => u.Following)
                  .HasForeignKey(f => f.FollowerId)
                  .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne(f => f.Followed)
                  .WithMany(u => u.Followers)
                  .HasForeignKey(f => f.FollowedId)
                  .OnDelete(DeleteBehavior.Restrict);

            entity.HasIndex(f => f.FollowerId);
            entity.HasIndex(f => f.FollowedId);
        });

        modelBuilder.Entity<Novel>(entity =>
        {
            entity.HasKey(n => n.Id);

            entity.HasOne(n => n.Owner)
                  .WithMany(u => u.Novels)
                  .HasForeignKey(n => n.AuthorId);

            entity.HasIndex(n => n.AuthorId);
        });
    }
}
