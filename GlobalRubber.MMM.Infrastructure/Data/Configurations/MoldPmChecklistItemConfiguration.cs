using GlobalRubber.MMM.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace GlobalRubber.MMM.Infrastructure.Data.Configurations;

public class MoldPmChecklistItemConfiguration : IEntityTypeConfiguration<MoldPmChecklistItem>
{
    public void Configure(EntityTypeBuilder<MoldPmChecklistItem> builder)
    {
        builder.ToTable("mold_pm_checklist_transaction", "transactions");

        builder.HasKey(e => e.MoldPmChecklistId);
        builder.Property(e => e.MoldPmChecklistId).HasColumnName("mold_pm_checklist_id").ValueGeneratedOnAdd();

        builder.Property(e => e.MoldPmId).HasColumnName("mold_pm_id").IsRequired();
        builder.Property(e => e.SortOrder).HasColumnName("sort_order").IsRequired();
        builder.Property(e => e.ItemLabel).HasColumnName("item_label").HasMaxLength(200).IsRequired();
        builder.Property(e => e.IsChecked).HasColumnName("is_checked").IsRequired();

        // The snapshot outlives the master item (FK ON DELETE SET NULL, like the machine PM snapshot).
        builder.Property(e => e.ChecklistItemId).HasColumnName("checklist_item_id");
        builder.HasOne<MaintenanceChecklistItem>().WithMany().HasForeignKey(e => e.ChecklistItemId).OnDelete(DeleteBehavior.SetNull);

        builder.HasIndex(e => e.MoldPmId);
    }
}
