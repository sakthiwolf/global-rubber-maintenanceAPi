using GlobalRubber.MMM.Domain.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace GlobalRubber.MMM.Infrastructure.Data.Configurations;

/// <summary>Shared column mapping for the created_at/by, updated_at/by, row_version columns.</summary>
internal static class AuditableEntityConfigurationExtensions
{
    public static void ConfigureAuditColumns<TEntity>(this EntityTypeBuilder<TEntity> builder)
        where TEntity : AuditableEntity
    {
        builder.Property(e => e.CreatedAt).HasColumnName("created_at").IsRequired();
        builder.Property(e => e.CreatedBy).HasColumnName("created_by");
        builder.Property(e => e.UpdatedAt).HasColumnName("updated_at");
        builder.Property(e => e.UpdatedBy).HasColumnName("updated_by");
        builder.Property(e => e.RowVersion).HasColumnName("row_version").IsRowVersion();
    }
}
