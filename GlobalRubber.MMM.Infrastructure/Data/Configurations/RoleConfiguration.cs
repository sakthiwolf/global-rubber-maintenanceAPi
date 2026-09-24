using GlobalRubber.MMM.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace GlobalRubber.MMM.Infrastructure.Data.Configurations;

public class RoleConfiguration : IEntityTypeConfiguration<Role>
{
    public void Configure(EntityTypeBuilder<Role> builder)
    {
        builder.ToTable("role_master", "security");

        builder.HasKey(e => e.RoleId);
        builder.Property(e => e.RoleId).HasColumnName("role_id").ValueGeneratedOnAdd();

        builder.Property(e => e.RoleCode).HasColumnName("role_code").HasMaxLength(30).IsRequired();
        builder.Property(e => e.RoleName).HasColumnName("role_name").HasMaxLength(100).IsRequired();
        builder.Property(e => e.Description).HasColumnName("description").HasMaxLength(200);
        builder.Property(e => e.IsSystemRole).HasColumnName("is_system_role").IsRequired();
        builder.Property(e => e.IsActive).HasColumnName("is_active").IsRequired();

        builder.HasIndex(e => e.RoleCode).IsUnique();

        builder.ConfigureAuditColumns();
    }
}
