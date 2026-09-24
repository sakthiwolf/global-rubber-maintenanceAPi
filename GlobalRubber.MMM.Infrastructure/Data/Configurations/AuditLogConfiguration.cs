using GlobalRubber.MMM.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace GlobalRubber.MMM.Infrastructure.Data.Configurations;

public class AuditLogConfiguration : IEntityTypeConfiguration<AuditLog>
{
    public void Configure(EntityTypeBuilder<AuditLog> builder)
    {
        builder.ToTable("audit_log", "audit");

        builder.HasKey(e => e.AuditLogId);
        builder.Property(e => e.AuditLogId).HasColumnName("audit_log_id").ValueGeneratedOnAdd();

        builder.Property(e => e.EventAt).HasColumnName("event_at").IsRequired();
        builder.Property(e => e.UserId).HasColumnName("user_id");
        builder.Property(e => e.UserName).HasColumnName("user_name").HasMaxLength(100).IsRequired();
        builder.Property(e => e.Module).HasColumnName("module").HasMaxLength(50).IsRequired();
        builder.Property(e => e.Action).HasColumnName("action").HasMaxLength(30).IsRequired();
        builder.Property(e => e.EntityName).HasColumnName("entity_name").HasMaxLength(50);
        builder.Property(e => e.EntityId).HasColumnName("entity_id");
        builder.Property(e => e.RecordRef).HasColumnName("record_ref").HasMaxLength(50);
        builder.Property(e => e.Description).HasColumnName("description").HasMaxLength(1000).IsRequired();
        builder.Property(e => e.IpAddress).HasColumnName("ip_address").HasMaxLength(45);

        builder.HasMany(e => e.Details)
            .WithOne(d => d.AuditLog)
            .HasForeignKey(d => d.AuditLogId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
