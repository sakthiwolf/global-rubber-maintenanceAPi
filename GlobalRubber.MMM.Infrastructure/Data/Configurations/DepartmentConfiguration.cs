using GlobalRubber.MMM.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace GlobalRubber.MMM.Infrastructure.Data.Configurations;

public class DepartmentConfiguration : IEntityTypeConfiguration<Department>
{
    public void Configure(EntityTypeBuilder<Department> builder)
    {
        builder.ToTable("department_master", "masters");

        builder.HasKey(e => e.DepartmentId);
        builder.Property(e => e.DepartmentId).HasColumnName("department_id").ValueGeneratedOnAdd();

        builder.Property(e => e.DepartmentCode).HasColumnName("department_code").HasMaxLength(20).IsUnicode(false).IsRequired();
        builder.Property(e => e.DepartmentName).HasColumnName("department_name").HasMaxLength(100).IsRequired();
        builder.Property(e => e.Remarks).HasColumnName("remarks").HasMaxLength(500);
        builder.Property(e => e.IsActive).HasColumnName("is_active").IsRequired();

        builder.HasIndex(e => e.DepartmentCode).IsUnique();

        builder.ConfigureAuditColumns();
    }
}
