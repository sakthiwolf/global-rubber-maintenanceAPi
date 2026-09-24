using GlobalRubber.MMM.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace GlobalRubber.MMM.Infrastructure.Data.Configurations;

public class SparePartConfiguration : IEntityTypeConfiguration<SparePart>
{
    public void Configure(EntityTypeBuilder<SparePart> builder)
    {
        builder.ToTable("spare_part_master", "masters");

        builder.HasKey(e => e.SparePartId);
        builder.Property(e => e.SparePartId).HasColumnName("spare_part_id").ValueGeneratedOnAdd();

        builder.Property(e => e.SparePartCode).HasColumnName("spare_part_code").HasMaxLength(20).IsUnicode(false).IsRequired();
        builder.Property(e => e.SparePartName).HasColumnName("spare_part_name").HasMaxLength(150).IsRequired();
        builder.Property(e => e.Category).HasColumnName("category").HasMaxLength(100);
        builder.Property(e => e.MachineId).HasColumnName("machine_id");
        builder.Property(e => e.PartNumber).HasColumnName("part_number").HasMaxLength(50);
        builder.Property(e => e.Unit).HasColumnName("unit").HasMaxLength(20).IsRequired();
        builder.Property(e => e.MinimumStock).HasColumnName("minimum_stock").IsRequired();
        builder.Property(e => e.CurrentStock).HasColumnName("current_stock").IsRequired();
        builder.Property(e => e.VendorId).HasColumnName("vendor_id");
        builder.Property(e => e.StoreLocation).HasColumnName("store_location").HasMaxLength(100);
        builder.Property(e => e.UnitCost).HasColumnName("unit_cost").HasPrecision(18, 2);
        builder.Property(e => e.IsActive).HasColumnName("is_active").IsRequired();

        // Persisted computed column: never inserted/updated by EF, read back by SQL Server after every save.
        builder.Property(e => e.StockStatus).HasColumnName("stock_status").HasMaxLength(12).IsUnicode(false)
            .HasComputedColumnSql(
                "CASE WHEN [current_stock] <= 0 THEN 'Out of Stock' " +
                "WHEN [current_stock] <= [minimum_stock] THEN 'Low Stock' ELSE 'Available' END",
                stored: true);

        builder.HasIndex(e => e.SparePartCode).IsUnique();

        // FK_spare_part_master_machine / FK_spare_part_master_vendor (NO ACTION).
        builder.HasOne(e => e.Machine)
            .WithMany()
            .HasForeignKey(e => e.MachineId)
            .OnDelete(DeleteBehavior.NoAction);

        builder.HasOne(e => e.Vendor)
            .WithMany()
            .HasForeignKey(e => e.VendorId)
            .OnDelete(DeleteBehavior.NoAction);

        builder.ConfigureAuditColumns();
    }
}
