using GlobalRubber.MMM.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace GlobalRubber.MMM.Infrastructure.Data.Configurations;

public class ModuleConfiguration : IEntityTypeConfiguration<Module>
{
    public void Configure(EntityTypeBuilder<Module> builder)
    {
        builder.ToTable("module_master", "security");

        builder.HasKey(e => e.ModuleId);
        builder.Property(e => e.ModuleId).HasColumnName("module_id").ValueGeneratedOnAdd();

        builder.Property(e => e.ModuleCode).HasColumnName("module_code").HasMaxLength(50).IsRequired();
        builder.Property(e => e.ModuleName).HasColumnName("module_name").HasMaxLength(100).IsRequired();
        builder.Property(e => e.ParentModuleId).HasColumnName("parent_module_id");
        builder.Property(e => e.MenuGroup).HasColumnName("menu_group").HasMaxLength(20).IsRequired();
        builder.Property(e => e.Route).HasColumnName("route").HasMaxLength(200);
        builder.Property(e => e.Icon).HasColumnName("icon").HasMaxLength(50);
        builder.Property(e => e.SortOrder).HasColumnName("sort_order").IsRequired();
        builder.Property(e => e.IsMenuVisible).HasColumnName("is_menu_visible").IsRequired();
        builder.Property(e => e.IsActive).HasColumnName("is_active").IsRequired();

        builder.HasIndex(e => e.ModuleCode).IsUnique();

        builder.HasOne(e => e.ParentModule)
            .WithMany(e => e.ChildModules)
            .HasForeignKey(e => e.ParentModuleId)
            .OnDelete(DeleteBehavior.NoAction);

        builder.ConfigureAuditColumns();
    }
}
