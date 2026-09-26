using GlobalRubber.MMM.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace GlobalRubber.MMM.Infrastructure.Data.Configurations;

/// <summary>transactions.notification_transaction (migration 014). Insert-only; created_at is set by SQL Server.</summary>
public class NotificationConfiguration : IEntityTypeConfiguration<Notification>
{
    public void Configure(EntityTypeBuilder<Notification> builder)
    {
        builder.ToTable("notification_transaction", "transactions");

        builder.HasKey(e => e.NotificationId);
        builder.Property(e => e.NotificationId).HasColumnName("notification_id").ValueGeneratedOnAdd();

        // CK_notification_transaction_type / _severity.
        builder.Property(e => e.NotificationType).HasColumnName("notification_type").HasMaxLength(30).IsUnicode(false).IsRequired();
        // FK_notification_transaction_module -> security.module_master.module_code (recipients: View on this module).
        builder.Property(e => e.ModuleCode).HasColumnName("module_code").HasMaxLength(50).IsUnicode(false).IsRequired();
        builder.Property(e => e.Severity).HasColumnName("severity").HasMaxLength(10).IsUnicode(false).IsRequired();
        builder.Property(e => e.Title).HasColumnName("title").HasMaxLength(150).IsRequired();
        builder.Property(e => e.Message).HasColumnName("message").HasMaxLength(500).IsRequired();
        builder.Property(e => e.EntityName).HasColumnName("entity_name").HasMaxLength(50).IsUnicode(false);
        builder.Property(e => e.EntityId).HasColumnName("entity_id");
        builder.Property(e => e.RecordRef).HasColumnName("record_ref").HasMaxLength(50);
        builder.Property(e => e.LinkPath).HasColumnName("link_path").HasMaxLength(200).IsUnicode(false);
        // UQ_notification_transaction_event_key: one notification per event.
        builder.Property(e => e.EventKey).HasColumnName("event_key").HasMaxLength(100).IsUnicode(false).IsRequired();
        builder.Property(e => e.CreatedAt).HasColumnName("created_at").HasColumnType("datetime2(0)")
            .HasDefaultValueSql("SYSUTCDATETIME()").ValueGeneratedOnAdd();
        builder.Property(e => e.CreatedBy).HasColumnName("created_by");

        builder.HasIndex(e => e.EventKey).IsUnique();
    }
}
