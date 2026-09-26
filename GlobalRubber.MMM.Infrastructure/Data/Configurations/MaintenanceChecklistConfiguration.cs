using GlobalRubber.MMM.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace GlobalRubber.MMM.Infrastructure.Data.Configurations;

public class MaintenanceChecklistConfiguration : IEntityTypeConfiguration<MaintenanceChecklist>
{
    public void Configure(EntityTypeBuilder<MaintenanceChecklist> builder)
    {
        builder.ToTable("maintenance_checklist_master", "masters");

        builder.HasKey(e => e.ChecklistId);
        builder.Property(e => e.ChecklistId).HasColumnName("checklist_id").ValueGeneratedOnAdd();

        builder.Property(e => e.ChecklistCode).HasColumnName("checklist_code").HasMaxLength(20).IsUnicode(false).IsRequired();
        builder.Property(e => e.ChecklistName).HasColumnName("checklist_name").HasMaxLength(150).IsRequired();
        // CK_maintenance_checklist_master_applies_to: Machine / Mold.
        builder.Property(e => e.AppliesTo).HasColumnName("applies_to").HasMaxLength(10).IsUnicode(false).IsRequired();
        builder.Property(e => e.IsActive).HasColumnName("is_active").IsRequired();

        // Migration 012. CK_maintenance_checklist_master_frequency: Daily / Weekly / Monthly / Yearly.
        // CK_maintenance_checklist_master_machine: a Mold checklist never has a machine.
        // CK_maintenance_checklist_master_active_config: an active checklist has a frequency, an active Machine one a machine.
        builder.Property(e => e.Frequency).HasColumnName("frequency").HasMaxLength(10).IsUnicode(false);
        builder.Property(e => e.MachineId).HasColumnName("machine_id");

        // Migration 013. CK_maintenance_checklist_master_start_date: an active Machine checklist has a start date.
        builder.Property(e => e.StartDate).HasColumnName("start_date").HasColumnType("date");

        // FK_maintenance_checklist_master_machine (NO ACTION); IX_maintenance_checklist_master_machine.
        builder.HasOne(e => e.Machine)
            .WithMany()
            .HasForeignKey(e => e.MachineId)
            .OnDelete(DeleteBehavior.NoAction);
        builder.HasIndex(e => e.MachineId);

        // UQ_maintenance_checklist_master_code
        builder.HasIndex(e => e.ChecklistCode).IsUnique();

        // FK_checklist_item_master_checklist is ON DELETE CASCADE in the database. The header is never hard-deleted
        // (DELETE on the API is a soft deactivation); items are replaced as a set by the repository on edit.
        builder.HasMany(e => e.Items)
            .WithOne()
            .HasForeignKey(i => i.ChecklistId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.ConfigureAuditColumns();
    }
}
