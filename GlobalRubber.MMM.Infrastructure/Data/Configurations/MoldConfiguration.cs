using GlobalRubber.MMM.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace GlobalRubber.MMM.Infrastructure.Data.Configurations;

public class MoldConfiguration : IEntityTypeConfiguration<Mold>
{
    public void Configure(EntityTypeBuilder<Mold> builder)
    {
        builder.ToTable("mold_master", "masters");

        builder.HasKey(e => e.MoldId);
        builder.Property(e => e.MoldId).HasColumnName("mold_id").ValueGeneratedOnAdd();

        builder.Property(e => e.MoldCode).HasColumnName("mold_code").HasMaxLength(20).IsUnicode(false).IsRequired();
        builder.Property(e => e.MoldName).HasColumnName("mold_name").HasMaxLength(150).IsRequired();
        builder.Property(e => e.ProductId).HasColumnName("product_id").IsRequired();
        builder.Property(e => e.MoldType).HasColumnName("mold_type").HasMaxLength(100).IsRequired();
        builder.Property(e => e.CavityCount).HasColumnName("cavity_count").IsRequired();
        builder.Property(e => e.Manufacturer).HasColumnName("manufacturer").HasMaxLength(100);
        builder.Property(e => e.SerialNumber).HasColumnName("serial_number").HasMaxLength(100);
        builder.Property(e => e.Location).HasColumnName("location").HasMaxLength(100);
        builder.Property(e => e.StorageLocation).HasColumnName("storage_location").HasMaxLength(100);
        builder.Property(e => e.CommissionDate).HasColumnName("commission_date").HasColumnType("date");
        builder.Property(e => e.MaximumShots).HasColumnName("maximum_shots").IsRequired();
        builder.Property(e => e.WarningShots).HasColumnName("warning_shots").IsRequired();
        builder.Property(e => e.ReplacementShots).HasColumnName("replacement_shots").IsRequired();
        // CK_mold_master_maintenance_frequency_shots (migration 014): NULL or > 0 - the usage-based PM interval.
        builder.Property(e => e.MaintenanceFrequencyShots).HasColumnName("maintenance_frequency_shots");
        builder.Property(e => e.CurrentUsageShots).HasColumnName("current_usage_shots").IsRequired();
        // Migration 014: CK_mold_master_pm_cycle_start_shots (>= 0), default 0; CK_mold_master_pm_warning_shots (NULL, or
        // > 0 and below the interval).
        builder.Property(e => e.PmCycleStartShots).HasColumnName("pm_cycle_start_shots").IsRequired();
        builder.Property(e => e.PmWarningShots).HasColumnName("pm_warning_shots");
        builder.Property(e => e.ResponsibleEmployeeId).HasColumnName("responsible_employee_id");
        builder.Property(e => e.Status).HasColumnName("status").HasMaxLength(20).IsUnicode(false).IsRequired();
        builder.Property(e => e.Remarks).HasColumnName("remarks").HasMaxLength(500);

        // Persisted computed column: never inserted/updated by EF, read back by SQL Server after every save.
        builder.Property(e => e.LifeState).HasColumnName("life_state").HasMaxLength(7).IsUnicode(false)
            .HasComputedColumnSql(
                "CASE WHEN [current_usage_shots] >= [replacement_shots] THEN 'Replace' " +
                "WHEN [current_usage_shots] >= [warning_shots] THEN 'Warning' ELSE 'Normal' END",
                stored: true);

        builder.HasIndex(e => e.MoldCode).IsUnique();
        // UX_mold_master_serial_number: unique only when a serial number is entered.
        builder.HasIndex(e => e.SerialNumber).IsUnique().HasFilter("[serial_number] IS NOT NULL");

        // FK_mold_master_product / FK_mold_master_responsible_employee (NO ACTION).
        builder.HasOne(e => e.Product)
            .WithMany()
            .HasForeignKey(e => e.ProductId)
            .OnDelete(DeleteBehavior.NoAction);

        builder.HasOne(e => e.ResponsibleEmployee)
            .WithMany()
            .HasForeignKey(e => e.ResponsibleEmployeeId)
            .OnDelete(DeleteBehavior.NoAction);

        builder.ConfigureAuditColumns();
    }
}
