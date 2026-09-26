using GlobalRubber.MMM.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace GlobalRubber.MMM.Infrastructure.Data.Configurations;

public class SparePartUsageConfiguration : IEntityTypeConfiguration<SparePartUsage>
{
    public void Configure(EntityTypeBuilder<SparePartUsage> builder)
    {
        builder.ToTable("spare_part_usage_transaction", "transactions");

        builder.HasKey(e => e.SparePartUsageId);
        builder.Property(e => e.SparePartUsageId).HasColumnName("spare_part_usage_id").ValueGeneratedOnAdd();

        // UQ_spare_part_usage_transaction_usage_no - issued from the SPARE_PART_USAGE document sequence (SPU).
        builder.Property(e => e.UsageNo).HasColumnName("usage_no").HasMaxLength(20).IsUnicode(false).IsRequired();
        builder.Property(e => e.SparePartId).HasColumnName("spare_part_id").IsRequired();
        // CK_spare_part_usage_transaction_quantity: > 0 (whole units - INT).
        builder.Property(e => e.Quantity).HasColumnName("quantity").IsRequired();
        builder.Property(e => e.UsageDate).HasColumnName("usage_date").HasColumnType("date").IsRequired();
        // CK_spare_part_usage_transaction_used_for / _maintenance_source (migration 016).
        builder.Property(e => e.UsedFor).HasColumnName("used_for").HasMaxLength(25).IsUnicode(false).IsRequired();
        builder.Property(e => e.ReferenceNo).HasColumnName("reference_no").HasMaxLength(50);
        builder.Property(e => e.MachineId).HasColumnName("machine_id");
        builder.Property(e => e.MoldId).HasColumnName("mold_id");
        builder.Property(e => e.MachinePmId).HasColumnName("machine_pm_id");
        builder.Property(e => e.MoldPmId).HasColumnName("mold_pm_id");
        builder.Property(e => e.UsedByEmployeeId).HasColumnName("used_by_employee_id");
        builder.Property(e => e.UnitCostAtIssue).HasColumnName("unit_cost_at_issue").HasColumnType("decimal(18,2)");
        builder.Property(e => e.Remarks).HasColumnName("remarks").HasMaxLength(500);
        // Migration 016: CK_spare_part_usage_transaction_status (Posted / Reversed) and _reversal.
        builder.Property(e => e.Status).HasColumnName("status").HasMaxLength(10).IsUnicode(false).IsRequired();
        builder.Property(e => e.ReversedAt).HasColumnName("reversed_at").HasColumnType("datetime2(0)");
        builder.Property(e => e.ReversedBy).HasColumnName("reversed_by");
        builder.Property(e => e.ReversalReason).HasColumnName("reversal_reason").HasMaxLength(500);
        // UX_spare_part_usage_transaction_request_id: the idempotency key, unique when present.
        builder.Property(e => e.RequestId).HasColumnName("request_id");

        builder.HasIndex(e => e.UsageNo).IsUnique();
        builder.HasIndex(e => e.RequestId).IsUnique().HasFilter("[request_id] IS NOT NULL").HasDatabaseName("UX_spare_part_usage_transaction_request_id");

        // All FKs NO ACTION (008_create_constraints.sql).
        builder.HasOne(e => e.SparePart).WithMany().HasForeignKey(e => e.SparePartId).OnDelete(DeleteBehavior.NoAction);
        builder.HasOne(e => e.Machine).WithMany().HasForeignKey(e => e.MachineId).OnDelete(DeleteBehavior.NoAction);
        builder.HasOne(e => e.Mold).WithMany().HasForeignKey(e => e.MoldId).OnDelete(DeleteBehavior.NoAction);
        builder.HasOne(e => e.MachinePm).WithMany().HasForeignKey(e => e.MachinePmId).OnDelete(DeleteBehavior.NoAction);
        builder.HasOne(e => e.MoldPm).WithMany().HasForeignKey(e => e.MoldPmId).OnDelete(DeleteBehavior.NoAction);
        builder.HasOne(e => e.UsedByEmployee).WithMany().HasForeignKey(e => e.UsedByEmployeeId).OnDelete(DeleteBehavior.NoAction);

        builder.ConfigureAuditColumns();
    }
}

/// <summary>transactions.spare_part_stock_transaction (migration 016) - the insert-only stock ledger.</summary>
public class SparePartStockTransactionConfiguration : IEntityTypeConfiguration<SparePartStockTransaction>
{
    public void Configure(EntityTypeBuilder<SparePartStockTransaction> builder)
    {
        builder.ToTable("spare_part_stock_transaction", "transactions");

        builder.HasKey(e => e.StockTransactionId);
        builder.Property(e => e.StockTransactionId).HasColumnName("stock_transaction_id").ValueGeneratedOnAdd();
        builder.Property(e => e.SparePartId).HasColumnName("spare_part_id").IsRequired();
        // CK_spare_part_stock_transaction_type / _balance / _new_stock / _quantity / _direction.
        builder.Property(e => e.TransactionType).HasColumnName("transaction_type").HasMaxLength(15).IsUnicode(false).IsRequired();
        builder.Property(e => e.Quantity).HasColumnName("quantity").IsRequired();
        builder.Property(e => e.PreviousStock).HasColumnName("previous_stock").IsRequired();
        builder.Property(e => e.NewStock).HasColumnName("new_stock").IsRequired();
        builder.Property(e => e.ReferenceType).HasColumnName("reference_type").HasMaxLength(30).IsUnicode(false);
        builder.Property(e => e.ReferenceId).HasColumnName("reference_id");
        builder.Property(e => e.ReferenceNo).HasColumnName("reference_no").HasMaxLength(50);
        builder.Property(e => e.TransactionAt).HasColumnName("transaction_at").HasColumnType("datetime2(0)")
            .HasDefaultValueSql("SYSUTCDATETIME()").ValueGeneratedOnAdd();
        builder.Property(e => e.CreatedBy).HasColumnName("created_by");
        builder.Property(e => e.Remarks).HasColumnName("remarks").HasMaxLength(500);
    }
}
