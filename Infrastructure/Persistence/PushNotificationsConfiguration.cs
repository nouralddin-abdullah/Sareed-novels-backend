using Domain.Entities;
using Infrastructure.Push;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.Persistence;

internal sealed class UserDeviceConfiguration : IEntityTypeConfiguration<UserDevice>
{
    public void Configure(EntityTypeBuilder<UserDevice> entity)
    {
        entity.HasKey(d => d.Id);

        entity.Property(d => d.Token).IsRequired().HasMaxLength(UserDevice.TokenMaxLength);
        entity.Property(d => d.Platform).IsRequired().HasMaxLength(16);
        entity.Property(d => d.AppVersion).HasMaxLength(32);
        entity.Property(d => d.Locale).IsRequired().HasMaxLength(16);

        entity.HasOne<User>()
              .WithMany()
              .HasForeignKey(d => d.UserId)
              .OnDelete(DeleteBehavior.Cascade);

        entity.HasIndex(d => d.Token).IsUnique();
        entity.HasIndex(d => d.UserId);
    }
}

internal sealed class NotificationPreferencesConfiguration : IEntityTypeConfiguration<NotificationPreferences>
{
    public void Configure(EntityTypeBuilder<NotificationPreferences> entity)
    {
        entity.HasKey(p => p.UserId);

        entity.HasOne<User>()
              .WithOne()
              .HasForeignKey<NotificationPreferences>(p => p.UserId)
              .OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class PushOutboxMessageConfiguration : IEntityTypeConfiguration<PushOutboxMessage>
{
    public void Configure(EntityTypeBuilder<PushOutboxMessage> entity)
    {
        entity.ToTable("PushOutbox");
        entity.HasKey(o => o.Id);

        entity.Property(o => o.LastError).HasMaxLength(PushOutboxMessage.LastErrorMaxLength);

        // A deleted notification (or user) takes its pending pushes with it.
        entity.HasOne<Notification>()
              .WithMany()
              .HasForeignKey(o => o.NotificationId)
              .OnDelete(DeleteBehavior.Cascade);

        // The worker's queue: pending rows by due time. Finished rows aren't in it.
        entity.HasIndex(o => o.NextAttemptAt)
              .HasFilter("[Status] = 0")
              .HasDatabaseName("IX_PushOutbox_Due");

        // Deleting finished rows after a few days.
        entity.HasIndex(o => o.CreatedAt);
    }
}
