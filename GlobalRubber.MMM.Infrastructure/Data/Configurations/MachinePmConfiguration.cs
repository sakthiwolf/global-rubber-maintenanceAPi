using GlobalRubber.MMM.Domain.Constants;
using GlobalRubber.MMM.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace GlobalRubber.MMM.Infrastructure.Data.Configurations;

public class MachinePmConfiguration : IEntityTypeConfiguration<MachinePm>
{
    public void Configure(EntityTypeBuilder<MachinePm> builder)
    {
        builder.ToTable("machine_pm_transaction", "transactions");

        builder.HasKey(e => e.MachinePmId);
        builder.Property(e => e.MachinePmId).HasColumnName("machine_pm_id").ValueGeneratedOnAdd();

        builder.Property(e => e.PmNo).HasColumnName("pm_no").HasMaxLength(20).IsUnicode(false).IsRequired();
        builder.Property(e => e.MachineId).HasColumnName("machine_id").IsRequired();
        builder.Property(e => e.MaintenanceTypeId).HasColumnName("maintenance_type_id"); // NULLable since migration 012
        builder.Property(e => e.ScheduledDate).HasColumnName("scheduled_date").HasColumnType("date").IsRequired();
        builder.Property(e => e.CompletedDate).HasColumnName("completed_date").HasColumnType("date");
        builder.Property(e => e.EngineerId).HasColumnName("engineer_id");
        builder.Property(e => e.ChecklistId).HasColumnName("checklist_id");
        builder.Property(e => e.Remarks).HasColumnName("remarks").HasMaxLength(1000);
        builder.Property(e => e.MaintenanceBy).HasColumnName("maintenance_by").HasMaxLength(100); // migration 012
        // CK_machine_pm_transaction_status: Scheduled / In Progress / Completed (DF 'Scheduled').
        builder.Property(e => e.Status).HasColumnName("status").HasMaxLength(15).IsUnicode(false).IsRequired()
            .HasDefaultValue(MachinePmStatus.Scheduled);

        // UQ_machine_pm_transaction_pm_no
        builder.HasIndex(e => e.PmNo).IsUnique();
        // IX_machine_pm_transaction_machine / IX_machine_pm_transaction_status_scheduled_date
        builder.HasIndex(e => e.MachineId);
        builder.HasIndex(e => new { e.MachineId, e.EngineerId, e.Status, e.ScheduledDate });

        // FK_machine_pm_transaction_machine / _maintenance_type / _engineer / _checklist - all NO ACTION.
        builder.HasOne(e => e.Machine).WithMany().HasForeignKey(e => e.MachineId).OnDelete(DeleteBehavior.NoAction);
        builder.HasOne(e => e.MaintenanceType).WithMany().HasForeignKey(e => e.MaintenanceTypeId).IsRequired(false).OnDelete(DeleteBehavior.NoAction);
        builder.HasOne(e => e.Engineer).WithMany().HasForeignKey(e => e.EngineerId).OnDelete(DeleteBehavior.NoAction);
        builder.HasOne(e => e.Checklist).WithMany().HasForeignKey(e => e.ChecklistId).OnDelete(DeleteBehavior.NoAction);

        // FK_machine_pm_checklist_transaction_pm is ON DELETE CASCADE (the header is never deleted by the API).
        builder.HasMany(e => e.ChecklistItems).WithOne().HasForeignKey(i => i.MachinePmId).OnDelete(DeleteBehavior.Cascade);

        builder.ConfigureAuditColumns();
    }
}
