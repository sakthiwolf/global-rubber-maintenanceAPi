using GlobalRubber.MMM.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace GlobalRubber.MMM.Infrastructure.Data.Configurations;

public class EmployeeConfiguration : IEntityTypeConfiguration<Employee>
{
    public void Configure(EntityTypeBuilder<Employee> builder)
    {
        builder.ToTable("employee_master", "masters");

        builder.HasKey(e => e.EmployeeId);
        builder.Property(e => e.EmployeeId).HasColumnName("employee_id").ValueGeneratedOnAdd();

        builder.Property(e => e.EmployeeCode).HasColumnName("employee_code").HasMaxLength(20).IsUnicode(false).IsRequired();
        builder.Property(e => e.EmployeeName).HasColumnName("employee_name").HasMaxLength(100).IsRequired();
        builder.Property(e => e.Designation).HasColumnName("designation").HasMaxLength(100).IsRequired();
        builder.Property(e => e.DepartmentId).HasColumnName("department_id").IsRequired();
        builder.Property(e => e.Mobile).HasColumnName("mobile").HasMaxLength(15).IsUnicode(false);
        builder.Property(e => e.Email).HasColumnName("email").HasMaxLength(150).IsUnicode(false);
        builder.Property(e => e.IsActive).HasColumnName("is_active").IsRequired();

        builder.HasIndex(e => e.EmployeeCode).IsUnique();

        // FK_employee_master_department (NO ACTION).
        builder.HasOne(e => e.Department)
            .WithMany()
            .HasForeignKey(e => e.DepartmentId)
            .OnDelete(DeleteBehavior.NoAction);

        builder.ConfigureAuditColumns();
    }
}
