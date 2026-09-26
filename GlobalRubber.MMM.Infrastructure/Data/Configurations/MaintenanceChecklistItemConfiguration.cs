using GlobalRubber.MMM.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace GlobalRubber.MMM.Infrastructure.Data.Configurations;

public class MaintenanceChecklistItemConfiguration : IEntityTypeConfiguration<MaintenanceChecklistItem>
{
    public void Configure(EntityTypeBuilder<MaintenanceChecklistItem> builder)
    {
        builder.ToTable("maintenance_checklist_item_master", "masters");

        builder.HasKey(e => e.ChecklistItemId);
        builder.Property(e => e.ChecklistItemId).HasColumnName("checklist_item_id").ValueGeneratedOnAdd();

        builder.Property(e => e.ChecklistId).HasColumnName("checklist_id").IsRequired();
        builder.Property(e => e.SortOrder).HasColumnName("sort_order").IsRequired();
        builder.Property(e => e.ItemLabel).HasColumnName("item_label").HasMaxLength(200).IsRequired();

        // No row_version on this table, so ConfigureAuditColumns (which maps one) does not apply.
        builder.Property(e => e.CreatedAt).HasColumnName("created_at").IsRequired();
        builder.Property(e => e.CreatedBy).HasColumnName("created_by");
        builder.Property(e => e.UpdatedAt).HasColumnName("updated_at");
        builder.Property(e => e.UpdatedBy).HasColumnName("updated_by");

        // IX_checklist_item_master_checklist
        builder.HasIndex(e => e.ChecklistId);
    }
}
