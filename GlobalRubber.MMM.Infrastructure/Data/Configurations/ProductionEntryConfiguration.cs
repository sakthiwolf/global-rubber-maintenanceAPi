using GlobalRubber.MMM.Domain.Constants;
using GlobalRubber.MMM.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace GlobalRubber.MMM.Infrastructure.Data.Configurations;

public class ProductionEntryConfiguration : IEntityTypeConfiguration<ProductionEntry>
{
    public void Configure(EntityTypeBuilder<ProductionEntry> builder)
    {
        builder.ToTable("production_entry_transaction", "transactions");

        builder.HasKey(e => e.ProductionEntryId);
        builder.Property(e => e.ProductionEntryId).HasColumnName("production_entry_id").ValueGeneratedOnAdd();

        builder.Property(e => e.EntryNo).HasColumnName("entry_no").HasMaxLength(20).IsUnicode(false).IsRequired();
        builder.Property(e => e.EntryDate).HasColumnName("entry_date").HasColumnType("date").IsRequired();
        // CK_production_entry_transaction_shift: Shift A / Shift B / Shift C.
        builder.Property(e => e.Shift).HasColumnName("shift").HasMaxLength(10).IsUnicode(false).IsRequired();
        builder.Property(e => e.MachineId).HasColumnName("machine_id").IsRequired();
        builder.Property(e => e.ProductId).HasColumnName("product_id").IsRequired();
        builder.Property(e => e.MoldId).HasColumnName("mold_id").IsRequired();
        // CK_production_entry_transaction_production_qty (> 0) / _rejected_qty (>= 0 AND <= production_qty).
        builder.Property(e => e.ProductionQty).HasColumnName("production_qty").IsRequired();
        builder.Property(e => e.RejectedQty).HasColumnName("rejected_qty").IsRequired().HasDefaultValue(0);

        // Persisted computed column: never inserted/updated by EF, read back by SQL Server after the insert.
        builder.Property(e => e.GoodQty).HasColumnName("good_qty")
            .HasComputedColumnSql("[production_qty] - [rejected_qty]", stored: true);

        builder.Property(e => e.MoldUsageBefore).HasColumnName("mold_usage_before").IsRequired();
        builder.Property(e => e.MoldUsageAfter).HasColumnName("mold_usage_after").IsRequired();
        builder.Property(e => e.Remarks).HasColumnName("remarks").HasMaxLength(500);
        // CK_production_entry_transaction_status: Saved / Cancelled (DF 'Saved').
        builder.Property(e => e.Status).HasColumnName("status").HasMaxLength(10).IsUnicode(false).IsRequired()
            .HasDefaultValue(ProductionEntryStatus.Saved);

        // UQ_production_entry_transaction_entry_no
        builder.HasIndex(e => e.EntryNo).IsUnique();
        // IX_production_entry_transaction_date_machine / _mold_date
        builder.HasIndex(e => new { e.EntryDate, e.MachineId });
        builder.HasIndex(e => new { e.MoldId, e.EntryDate });

        // FK_production_entry_transaction_machine / _product / _mold - all NO ACTION in the database.
        builder.HasOne(e => e.Machine).WithMany().HasForeignKey(e => e.MachineId).OnDelete(DeleteBehavior.NoAction);
        builder.HasOne(e => e.Product).WithMany().HasForeignKey(e => e.ProductId).OnDelete(DeleteBehavior.NoAction);
        builder.HasOne(e => e.Mold).WithMany().HasForeignKey(e => e.MoldId).OnDelete(DeleteBehavior.NoAction);

        builder.ConfigureAuditColumns();
    }
}
