using GlobalRubber.MMM.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace GlobalRubber.MMM.Infrastructure.Data.Configurations;

public class BreakdownTypeConfiguration : IEntityTypeConfiguration<BreakdownType>
{
    public void Configure(EntityTypeBuilder<BreakdownType> builder)
    {
        builder.ToTable("breakdown_type_master", "masters");

        builder.HasKey(e => e.BreakdownTypeId);
        builder.Property(e => e.BreakdownTypeId)
            .HasColumnName("breakdown_type_id")
            .ValueGeneratedOnAdd();

        builder.Property(e => e.BreakdownTypeCode)
            .HasColumnName("breakdown_type_code")
            .HasMaxLength(20)
            .IsUnicode(false)
            .IsRequired();

        builder.Property(e => e.BreakdownTypeName)
            .HasColumnName("breakdown_type_name")
            .HasMaxLength(100)
            .IsRequired();

        builder.Property(e => e.IsActive)
            .HasColumnName("is_active")
            .IsRequired();

        builder.HasIndex(e => e.BreakdownTypeCode)
            .IsUnique();

        builder.ConfigureAuditColumns();
    }
}