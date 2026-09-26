using GlobalRubber.MMM.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace GlobalRubber.MMM.Infrastructure.Data.Configurations;

public class MachinePmChecklistItemConfiguration : IEntityTypeConfiguration<MachinePmChecklistItem>
{
    public void Configure(EntityTypeBuilder<MachinePmChecklistItem> builder)
    {
        builder.ToTable("machine_pm_checklist_transaction", "transactions");

        builder.HasKey(e => e.MachinePmChecklistId);
        builder.Property(e => e.MachinePmChecklistId).HasColumnName("machine_pm_checklist_id").ValueGeneratedOnAdd();

        builder.Property(e => e.MachinePmId).HasColumnName("machine_pm_id").IsRequired();
        builder.Property(e => e.SortOrder).HasColumnName("sort_order").IsRequired();
        builder.Property(e => e.ItemLabel).HasColumnName("item_label").HasMaxLength(200).IsRequired();
        builder.Property(e => e.IsChecked).HasColumnName("is_checked").IsRequired();

        // FK_machine_pm_checklist_transaction_item is ON DELETE SET NULL: the snapshot outlives the master item.
        builder.Property(e => e.ChecklistItemId).HasColumnName("checklist_item_id");
        builder.HasOne<MaintenanceChecklistItem>().WithMany().HasForeignKey(e => e.ChecklistItemId).OnDelete(DeleteBehavior.SetNull);

        // IX_machine_pm_checklist_transaction_pm
        builder.HasIndex(e => e.MachinePmId);
    }
}
