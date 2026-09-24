using GlobalRubber.MMM.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace GlobalRubber.MMM.Infrastructure.Data.Configurations;

public class MaintenanceTypeConfiguration : IEntityTypeConfiguration<MaintenanceType>
{
    public void Configure(EntityTypeBuilder<MaintenanceType> builder)
    {
        builder.ToTable("maintenance_type_master", "masters");

        builder.HasKey(e => e.MaintenanceTypeId);
        builder.Property(e => e.MaintenanceTypeId).HasColumnName("maintenance_type_id").ValueGeneratedOnAdd();

        builder.Property(e => e.MaintenanceTypeCode).HasColumnName("maintenance_type_code").HasMaxLength(20).IsUnicode(false).IsRequired();
        builder.Property(e => e.MaintenanceTypeName).HasColumnName("maintenance_type_name").HasMaxLength(100).IsRequired();
        // CK_maintenance_type_master_applies_to: Machine / Mold / Both.
        builder.Property(e => e.AppliesTo).HasColumnName("applies_to").HasMaxLength(10).IsUnicode(false).IsRequired();
        builder.Property(e => e.IsActive).HasColumnName("is_active").IsRequired();

        builder.HasIndex(e => e.MaintenanceTypeCode).IsUnique();

        builder.ConfigureAuditColumns();
    }
}
