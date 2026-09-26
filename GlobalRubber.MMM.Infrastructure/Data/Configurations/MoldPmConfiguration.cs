using GlobalRubber.MMM.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace GlobalRubber.MMM.Infrastructure.Data.Configurations;

public class MoldPmConfiguration : IEntityTypeConfiguration<MoldPm>
{
    public void Configure(EntityTypeBuilder<MoldPm> builder)
    {
        builder.ToTable("mold_pm_transaction", "transactions");

        builder.HasKey(e => e.MoldPmId);
        builder.Property(e => e.MoldPmId).HasColumnName("mold_pm_id").ValueGeneratedOnAdd();

        // UQ_mold_pm_transaction_pm_no - issued from the MOLD_PM document sequence.
        builder.Property(e => e.PmNo).HasColumnName("pm_no").HasMaxLength(20).IsUnicode(false).IsRequired();
        builder.Property(e => e.MoldId).HasColumnName("mold_id").IsRequired();
        // CK_mold_pm_transaction_category.
        builder.Property(e => e.Category).HasColumnName("category").HasMaxLength(20).IsUnicode(false).IsRequired();
        builder.Property(e => e.ScheduledDate).HasColumnName("scheduled_date").HasColumnType("date").IsRequired();
        builder.Property(e => e.CompletedDate).HasColumnName("completed_date").HasColumnType("date");
        builder.Property(e => e.EngineerId).HasColumnName("engineer_id");
        builder.Property(e => e.ChecklistId).HasColumnName("checklist_id");
        builder.Property(e => e.MoldUsageAtService).HasColumnName("mold_usage_at_service").IsRequired();
        // Migration 014 - CK_mold_pm_transaction_shot_based: a Shot-based PM always has its threshold and interval (> 0).
        builder.Property(e => e.ThresholdShots).HasColumnName("threshold_shots");
        builder.Property(e => e.IntervalShots).HasColumnName("interval_shots");
        builder.Property(e => e.UsageAtCompletion).HasColumnName("usage_at_completion");
        // CK_mold_pm_transaction_usage_reset: only a Replacement resets usage - the automatic PM never does.
        builder.Property(e => e.UsageReset).HasColumnName("usage_reset").IsRequired();
        builder.Property(e => e.UsageBeforeReset).HasColumnName("usage_before_reset");
        builder.Property(e => e.MaintenanceBy).HasColumnName("maintenance_by").HasMaxLength(100); // migration 014
        builder.Property(e => e.Remarks).HasColumnName("remarks").HasMaxLength(1000);
        // CK_mold_pm_transaction_status: Scheduled / In Progress / Completed.
        builder.Property(e => e.Status).HasColumnName("status").HasMaxLength(15).IsUnicode(false).IsRequired();

        builder.HasIndex(e => e.PmNo).IsUnique();
        // UX_mold_pm_transaction_open_shot_based (migration 014): at most one open automatic PM per mold.
        builder.HasIndex(e => e.MoldId).IsUnique()
            .HasFilter("[category] = 'Shot-based' AND [status] IN ('Scheduled', 'In Progress')")
            .HasDatabaseName("UX_mold_pm_transaction_open_shot_based");
        // UX_mold_pm_transaction_shot_based_threshold (migration 014): at most one automatic PM per mold and cycle threshold.
        builder.HasIndex(e => new { e.MoldId, e.ThresholdShots }).IsUnique()
            .HasFilter("[category] = 'Shot-based' AND [threshold_shots] IS NOT NULL")
            .HasDatabaseName("UX_mold_pm_transaction_shot_based_threshold");

        // FK_mold_pm_transaction_mold (NO ACTION).
        builder.HasOne(e => e.Mold).WithMany().HasForeignKey(e => e.MoldId).OnDelete(DeleteBehavior.NoAction);

        builder.ConfigureAuditColumns();
    }
}
