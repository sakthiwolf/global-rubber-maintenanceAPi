using GlobalRubber.MMM.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace GlobalRubber.MMM.Infrastructure.Data.Configurations;

public class MachineConfiguration : IEntityTypeConfiguration<Machine>
{
    public void Configure(EntityTypeBuilder<Machine> builder)
    {
        builder.ToTable("machine_master", "masters");

        builder.HasKey(e => e.MachineId);
        builder.Property(e => e.MachineId).HasColumnName("machine_id").ValueGeneratedOnAdd();

        builder.Property(e => e.MachineCode).HasColumnName("machine_code").HasMaxLength(20).IsUnicode(false).IsRequired();
        builder.Property(e => e.MachineName).HasColumnName("machine_name").HasMaxLength(150).IsRequired();
        builder.Property(e => e.MachineType).HasColumnName("machine_type").HasMaxLength(100).IsRequired();
        builder.Property(e => e.DepartmentId).HasColumnName("department_id").IsRequired();
        builder.Property(e => e.Location).HasColumnName("location").HasMaxLength(150).IsRequired();
        builder.Property(e => e.Manufacturer).HasColumnName("manufacturer").HasMaxLength(100);
        builder.Property(e => e.Model).HasColumnName("model").HasMaxLength(100);
        builder.Property(e => e.SerialNumber).HasColumnName("serial_number").HasMaxLength(100);
        builder.Property(e => e.Capacity).HasColumnName("capacity").HasMaxLength(50);
        builder.Property(e => e.InstallationDate).HasColumnName("installation_date").HasColumnType("date");
        builder.Property(e => e.MaintenanceFrequencyDays).HasColumnName("maintenance_frequency_days").IsRequired();
        builder.Property(e => e.ResponsibleEngineerId).HasColumnName("responsible_engineer_id");
        builder.Property(e => e.Criticality).HasColumnName("criticality").HasMaxLength(10).IsUnicode(false).IsRequired();
        builder.Property(e => e.OperationalStatus).HasColumnName("operational_status").HasMaxLength(20).IsUnicode(false).IsRequired();
        builder.Property(e => e.LastMaintenanceDate).HasColumnName("last_maintenance_date").HasColumnType("date");
        builder.Property(e => e.NextMaintenanceDate).HasColumnName("next_maintenance_date").HasColumnType("date");
        builder.Property(e => e.Remarks).HasColumnName("remarks").HasMaxLength(500);
        builder.Property(e => e.IsActive).HasColumnName("is_active").IsRequired();

        builder.HasIndex(e => e.MachineCode).IsUnique();
        // UX_machine_master_serial_number: unique only when a serial number is entered.
        builder.HasIndex(e => e.SerialNumber).IsUnique().HasFilter("[serial_number] IS NOT NULL");

        // FK_machine_master_department / FK_machine_master_responsible_engineer (NO ACTION).
        builder.HasOne(e => e.Department)
            .WithMany()
            .HasForeignKey(e => e.DepartmentId)
            .OnDelete(DeleteBehavior.NoAction);

        // Responsible Engineer temporarily disabled. Database field and relationship intentionally retained for future
        // re-enablement (the mapping stays; the API neither reads nor writes it - see MachineService / MachineRepository).
        builder.HasOne(e => e.ResponsibleEngineer)
            .WithMany()
            .HasForeignKey(e => e.ResponsibleEngineerId)
            .OnDelete(DeleteBehavior.NoAction);

        builder.ConfigureAuditColumns();
    }
}
