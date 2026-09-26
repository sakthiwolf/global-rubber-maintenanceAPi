using GlobalRubber.MMM.Domain.Constants;
using GlobalRubber.MMM.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace GlobalRubber.MMM.Infrastructure.Data.Configurations;

public class MachineBreakdownConfiguration : IEntityTypeConfiguration<MachineBreakdown>
{
    public void Configure(EntityTypeBuilder<MachineBreakdown> builder)
    {
        builder.ToTable("machine_breakdown_transaction", "transactions");

        builder.HasKey(b => b.MachineBreakdownId);
        builder.Property(b => b.MachineBreakdownId).HasColumnName("machine_breakdown_id").ValueGeneratedOnAdd();

        // UQ_machine_breakdown_transaction_breakdown_no
        builder.Property(b => b.BreakdownNo).HasColumnName("breakdown_no").HasMaxLength(20).IsUnicode(false).IsRequired();
        builder.HasIndex(b => b.BreakdownNo).IsUnique();

        builder.Property(b => b.MachineId).HasColumnName("machine_id").IsRequired();

        // stored as date / time columns
        builder.Property(b => b.BreakdownDate).HasColumnName("breakdown_date").HasColumnType("date").IsRequired();
        builder.Property(b => b.BreakdownTime).HasColumnName("breakdown_time").HasColumnType("time(0)").IsRequired();

        // Free-text Reported By (015_machine_breakdown_reported_by_text.sql) - NOT an FK.
        builder.Property(b => b.ReportedBy).HasColumnName("reported_by").HasMaxLength(100);

        builder.Property(b => b.Problem).HasColumnName("problem").HasMaxLength(500).IsRequired();
        builder.Property(b => b.BreakdownTypeId).HasColumnName("breakdown_type_id");
        // CK_machine_breakdown_transaction_priority: Low / Medium / High / Critical (DF 'Medium').
        builder.Property(b => b.Priority).HasColumnName("priority").HasMaxLength(10).IsUnicode(false).IsRequired()
            .HasDefaultValue(BreakdownPriority.Medium);
        builder.Property(b => b.Description).HasColumnName("description").HasMaxLength(1000);

        // CK_machine_breakdown_transaction_stage: Reported / Assigned / Maintenance Started / Resolved / Closed (DF 'Reported').
        builder.Property(b => b.Stage).HasColumnName("stage").HasMaxLength(25).IsUnicode(false).IsRequired()
            .HasDefaultValue(BreakdownStage.Reported);

        builder.Property(b => b.AssignedEngineerId).HasColumnName("assigned_engineer_id");
        builder.Property(b => b.AssignedAt).HasColumnName("assigned_at").HasColumnType("datetime2(0)");
        builder.Property(b => b.MaintenanceStartedAt).HasColumnName("maintenance_started_at").HasColumnType("datetime2(0)");
        builder.Property(b => b.ResolvedAt).HasColumnName("resolved_at").HasColumnType("datetime2(0)");
        builder.Property(b => b.ClosedAt).HasColumnName("closed_at").HasColumnType("datetime2(0)");
        builder.Property(b => b.RootCause).HasColumnName("root_cause").HasMaxLength(1000);
        builder.Property(b => b.CorrectiveAction).HasColumnName("corrective_action").HasMaxLength(1000);
        // CK_machine_breakdown_transaction_downtime_hours: IS NULL OR >= 0.
        builder.Property(b => b.DowntimeHours).HasColumnName("downtime_hours").HasColumnType("decimal(8,1)");

        // FK_machine_breakdown_transaction_machine - NO ACTION.
        builder.HasOne(b => b.Machine).WithMany().HasForeignKey(b => b.MachineId).OnDelete(DeleteBehavior.NoAction);
        // FK_machine_breakdown_transaction_breakdown_type - NO ACTION.
        builder.HasOne(b => b.BreakdownType).WithMany().HasForeignKey(b => b.BreakdownTypeId).OnDelete(DeleteBehavior.NoAction);
        // FK_machine_breakdown_transaction_assigned_engineer - NO ACTION.
        builder.HasOne(b => b.AssignedEngineer).WithMany().HasForeignKey(b => b.AssignedEngineerId).OnDelete(DeleteBehavior.NoAction);

        builder.ConfigureAuditColumns();
    }
}
