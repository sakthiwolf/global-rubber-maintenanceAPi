using GlobalRubber.MMM.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace GlobalRubber.MMM.Infrastructure.Data.Configurations;

/// <summary>transactions.notification_read_transaction (migration 018). Insert-only per-user read state.</summary>
public class NotificationReadConfiguration : IEntityTypeConfiguration<NotificationRead>
{
    public void Configure(EntityTypeBuilder<NotificationRead> builder)
    {
        builder.ToTable("notification_read_transaction", "transactions");

        // PK_notification_read_transaction: a notification is read at most once per user.
        builder.HasKey(e => new { e.UserId, e.NotificationId });
        builder.Property(e => e.UserId).HasColumnName("user_id");
        builder.Property(e => e.NotificationId).HasColumnName("notification_id");
        builder.Property(e => e.ReadAt).HasColumnName("read_at").HasColumnType("datetime2(0)");

        // FK_notification_read_transaction_notification (ON DELETE CASCADE); FK_..._user -> security.user_master (NO ACTION,
        // kept as a plain id like every mapped table). IX_notification_read_transaction_notification.
        builder.HasOne<Notification>().WithMany().HasForeignKey(e => e.NotificationId).OnDelete(DeleteBehavior.Cascade);
        builder.HasIndex(e => e.NotificationId);
    }
}
