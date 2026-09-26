using GlobalRubber.MMM.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace GlobalRubber.MMM.Infrastructure.Data.Configurations;

public class UserRefreshTokenConfiguration : IEntityTypeConfiguration<UserRefreshToken>
{
    public void Configure(EntityTypeBuilder<UserRefreshToken> builder)
    {
        builder.ToTable("user_refresh_token", "security");

        builder.HasKey(e => e.RefreshTokenId);
        builder.Property(e => e.RefreshTokenId).HasColumnName("refresh_token_id").ValueGeneratedOnAdd();
        builder.Property(e => e.UserId).HasColumnName("user_id").IsRequired();
        // UQ_user_refresh_token_token_hash - the raw token is never stored.
        builder.Property(e => e.TokenHash).HasColumnName("token_hash").HasMaxLength(128).IsUnicode(false).IsRequired();
        builder.Property(e => e.ExpiresAt).HasColumnName("expires_at").HasColumnType("datetime2(0)").IsRequired();
        builder.Property(e => e.CreatedAt).HasColumnName("created_at").HasColumnType("datetime2(0)").IsRequired();
        builder.Property(e => e.RevokedAt).HasColumnName("revoked_at").HasColumnType("datetime2(0)");
        builder.Property(e => e.ReplacedByTokenHash).HasColumnName("replaced_by_token_hash").HasMaxLength(128).IsUnicode(false);
        builder.Property(e => e.CreatedByIp).HasColumnName("created_by_ip").HasMaxLength(45).IsUnicode(false);
        builder.Property(e => e.RevokedByIp).HasColumnName("revoked_by_ip").HasMaxLength(45).IsUnicode(false);

        builder.HasIndex(e => e.TokenHash).IsUnique();
        builder.HasIndex(e => e.UserId);
        builder.HasIndex(e => e.ExpiresAt);

        // FK_user_refresh_token_user (NO ACTION) - no navigation needed.
        builder.HasOne<User>().WithMany().HasForeignKey(e => e.UserId).OnDelete(DeleteBehavior.NoAction);
    }
}
