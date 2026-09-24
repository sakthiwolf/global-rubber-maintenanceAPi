using GlobalRubber.MMM.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace GlobalRubber.MMM.Infrastructure.Data.Configurations;

public class VendorConfiguration : IEntityTypeConfiguration<Vendor>
{
    public void Configure(EntityTypeBuilder<Vendor> builder)
    {
        builder.ToTable("vendor_master", "masters");

        builder.HasKey(e => e.VendorId);
        builder.Property(e => e.VendorId).HasColumnName("vendor_id").ValueGeneratedOnAdd();

        builder.Property(e => e.VendorCode).HasColumnName("vendor_code").HasMaxLength(20).IsUnicode(false).IsRequired();
        builder.Property(e => e.VendorName).HasColumnName("vendor_name").HasMaxLength(150).IsRequired();
        builder.Property(e => e.Category).HasColumnName("category").HasMaxLength(100);
        builder.Property(e => e.ContactPerson).HasColumnName("contact_person").HasMaxLength(100);
        builder.Property(e => e.Mobile).HasColumnName("mobile").HasMaxLength(15).IsUnicode(false);
        builder.Property(e => e.Email).HasColumnName("email").HasMaxLength(150).IsUnicode(false);
        builder.Property(e => e.Address).HasColumnName("address").HasMaxLength(500);
        builder.Property(e => e.IsActive).HasColumnName("is_active").IsRequired();

        builder.HasIndex(e => e.VendorCode).IsUnique();

        builder.ConfigureAuditColumns();
    }
}
