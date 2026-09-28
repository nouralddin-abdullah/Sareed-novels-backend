using Domain.Constants;
using Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.Persistence;

internal sealed class PlayPurchaseConfiguration : IEntityTypeConfiguration<PlayPurchase>
{
    public void Configure(EntityTypeBuilder<PlayPurchase> entity)
    {
        entity.HasKey(p => p.Id);

        // Tokens are ASCII; varchar keeps the unique index key small.
        entity.Property(p => p.PurchaseToken)
              .IsRequired()
              .IsUnicode(false)
              .HasMaxLength(PlayPurchase.PurchaseTokenMaxLength);

        // One row per token: a purchase is credited at most once, whoever sends it and however often.
        entity.HasIndex(p => p.PurchaseToken)
              .IsUnique()
              .HasDatabaseName("IX_PlayPurchases_PurchaseToken_Unique");

        entity.Property(p => p.ProductId).IsUnicode(false).HasMaxLength(PlayPurchase.ProductIdMaxLength);
        entity.Property(p => p.OrderId).IsUnicode(false).HasMaxLength(PlayPurchase.OrderIdMaxLength);
        entity.Property(p => p.LastConsumeError).HasMaxLength(PlayPurchase.ConsumeErrorMaxLength);

        entity.Property(p => p.Status)
              .IsRequired()
              .HasMaxLength(20)
              .HasDefaultValue(PlayPurchaseStatus.Credited);

        entity.HasOne(p => p.User)
              .WithMany()
              .HasForeignKey(p => p.UserId)
              .OnDelete(DeleteBehavior.Cascade);

        // The consume retry worker's query; the column is null for every row with nothing owed.
        entity.HasIndex(p => p.NextConsumeAttemptAt)
              .HasDatabaseName("IX_PlayPurchases_NextConsumeAttemptAt");
    }
}

internal sealed class PlaySyncCursorConfiguration : IEntityTypeConfiguration<PlaySyncCursor>
{
    public void Configure(EntityTypeBuilder<PlaySyncCursor> entity)
    {
        entity.HasKey(c => c.Name);
        entity.Property(c => c.Name).IsUnicode(false).HasMaxLength(50);
    }
}
