using Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.Persistence;

internal sealed class UserBlockConfiguration : IEntityTypeConfiguration<UserBlock>
{
    public void Configure(EntityTypeBuilder<UserBlock> entity)
    {
        entity.HasKey(b => new { b.BlockerId, b.BlockedId });

        // SQL Server allows only one cascading path from AspNetUsers to this table: deleting a user takes the blocks
        // they made with them; the blocks others made against them have to be deleted first (as follows are).
        entity.HasOne<User>()
              .WithMany()
              .HasForeignKey(b => b.BlockerId)
              .OnDelete(DeleteBehavior.Cascade);
        entity.HasOne<User>()
              .WithMany()
              .HasForeignKey(b => b.BlockedId)
              .OnDelete(DeleteBehavior.Restrict);

        // Who blocked a user: the other direction of every block check, and deleting a user.
        entity.HasIndex(b => b.BlockedId);
    }
}

internal sealed class ReportConfiguration : IEntityTypeConfiguration<Report>
{
    public void Configure(EntityTypeBuilder<Report> entity)
    {
        entity.HasKey(r => r.Id);

        // Stored by name, the same names the API reads and writes.
        entity.Property(r => r.TargetType).HasConversion<string>().HasMaxLength(20);
        entity.Property(r => r.Reason).HasConversion<string>().HasMaxLength(20);
        entity.Property(r => r.Status).HasConversion<string>().HasMaxLength(20);
        entity.Property(r => r.Resolution).HasConversion<string>().HasMaxLength(20);

        entity.Property(r => r.Details).HasMaxLength(Report.DetailsMaxLength);
        entity.Property(r => r.TargetExcerpt).HasMaxLength(Report.ExcerptMaxLength);
        // User ids, without foreign keys: a report outlives the content and the admin account it names.
        entity.Property(r => r.TargetOwnerId).HasMaxLength(450);
        entity.Property(r => r.ResolvedById).HasMaxLength(450);

        // A user's reports go with the user.
        entity.HasOne<User>()
              .WithMany()
              .HasForeignKey(r => r.ReporterId)
              .OnDelete(DeleteBehavior.Cascade);

        // One open report per reporter and target, even for concurrent requests (the duplicate answers 200).
        entity.HasIndex(r => new { r.ReporterId, r.TargetType, r.TargetId })
              .IsUnique()
              .HasFilter("[Status] = N'Open'")
              .HasDatabaseName("IX_Reports_Reporter_Target_Open");

        // The per-user limit: a reporter's recent reports.
        entity.HasIndex(r => new { r.ReporterId, r.CreatedAt });

        // The moderators' queue.
        entity.HasIndex(r => new { r.Status, r.CreatedAt });

        // The reports on one target, which an admin's action closes together.
        entity.HasIndex(r => new { r.TargetType, r.TargetId, r.Status });

        // Reports about a user's content (deleting a user).
        entity.HasIndex(r => r.TargetOwnerId);
    }
}
