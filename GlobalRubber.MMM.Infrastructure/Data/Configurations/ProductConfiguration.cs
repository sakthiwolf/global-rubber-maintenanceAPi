using GlobalRubber.MMM.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace GlobalRubber.MMM.Infrastructure.Data.Configurations;

public class ProductConfiguration : IEntityTypeConfiguration<Product>
{
    public void Configure(EntityTypeBuilder<Product> builder)
    {
        builder.ToTable("product_master", "masters");

        builder.HasKey(e => e.ProductId);
        builder.Property(e => e.ProductId).HasColumnName("product_id").ValueGeneratedOnAdd();

        builder.Property(e => e.ProductCode).HasColumnName("product_code").HasMaxLength(20).IsUnicode(false).IsRequired();
        builder.Property(e => e.ProductName).HasColumnName("product_name").HasMaxLength(150).IsRequired();
        builder.Property(e => e.Category).HasColumnName("category").HasMaxLength(100);
        builder.Property(e => e.UnitOfMeasure).HasColumnName("unit_of_measure").HasMaxLength(20).IsRequired();
        // DECIMAL(8,2), CK_product_master_standard_cycle_time_sec: NULL or > 0.
        builder.Property(e => e.StandardCycleTimeSec).HasColumnName("standard_cycle_time_sec").HasPrecision(8, 2);
        builder.Property(e => e.Remarks).HasColumnName("remarks").HasMaxLength(500);
        builder.Property(e => e.IsActive).HasColumnName("is_active").IsRequired();

        builder.HasIndex(e => e.ProductCode).IsUnique();

        builder.ConfigureAuditColumns();
    }
}
