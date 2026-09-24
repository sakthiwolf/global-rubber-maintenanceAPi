using GlobalRubber.MMM.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace GlobalRubber.MMM.Infrastructure.Data.Configurations;

public class PermissionConfiguration : IEntityTypeConfiguration<Permission>
{
    public void Configure(EntityTypeBuilder<Permission> builder)
    {
        builder.ToTable("permission_master", "security");

        builder.HasKey(e => e.PermissionId);
        builder.Property(e => e.PermissionId).HasColumnName("permission_id").ValueGeneratedOnAdd();

        builder.Property(e => e.RoleId).HasColumnName("role_id").IsRequired();
        builder.Property(e => e.ModuleId).HasColumnName("module_id").IsRequired();
        builder.Property(e => e.CanView).HasColumnName("can_view").IsRequired();
        builder.Property(e => e.CanAdd).HasColumnName("can_add").IsRequired();
        builder.Property(e => e.CanEdit).HasColumnName("can_edit").IsRequired();
        builder.Property(e => e.CanDelete).HasColumnName("can_delete").IsRequired();
        builder.Property(e => e.CanApprove).HasColumnName("can_approve").IsRequired();
        builder.Property(e => e.CanExport).HasColumnName("can_export").IsRequired();

        builder.HasIndex(e => new { e.RoleId, e.ModuleId }).IsUnique();

        builder.HasOne(e => e.Role)
            .WithMany(e => e.Permissions)
            .HasForeignKey(e => e.RoleId)
            .OnDelete(DeleteBehavior.NoAction);

        builder.HasOne(e => e.Module)
            .WithMany(e => e.Permissions)
            .HasForeignKey(e => e.ModuleId)
            .OnDelete(DeleteBehavior.NoAction);

        builder.ConfigureAuditColumns();
    }
}
