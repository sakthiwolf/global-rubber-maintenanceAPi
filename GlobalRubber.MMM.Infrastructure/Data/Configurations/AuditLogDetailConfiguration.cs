using GlobalRubber.MMM.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace GlobalRubber.MMM.Infrastructure.Data.Configurations;

public class AuditLogDetailConfiguration : IEntityTypeConfiguration<AuditLogDetail>
{
    public void Configure(EntityTypeBuilder<AuditLogDetail> builder)
    {
        builder.ToTable("audit_log_detail", "audit");

        builder.HasKey(e => e.AuditLogDetailId);
        builder.Property(e => e.AuditLogDetailId).HasColumnName("audit_log_detail_id").ValueGeneratedOnAdd();

        builder.Property(e => e.AuditLogId).HasColumnName("audit_log_id").IsRequired();
        builder.Property(e => e.FieldName).HasColumnName("field_name").HasMaxLength(100).IsRequired();
        builder.Property(e => e.OldValue).HasColumnName("old_value").HasMaxLength(1000);
        builder.Property(e => e.NewValue).HasColumnName("new_value").HasMaxLength(1000);
    }
}
