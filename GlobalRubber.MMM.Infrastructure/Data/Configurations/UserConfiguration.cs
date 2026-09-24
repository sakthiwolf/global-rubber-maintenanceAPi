using GlobalRubber.MMM.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace GlobalRubber.MMM.Infrastructure.Data.Configurations;

public class UserConfiguration : IEntityTypeConfiguration<User>
{
    public void Configure(EntityTypeBuilder<User> builder)
    {
        builder.ToTable("user_master", "security");

        builder.HasKey(e => e.UserId);
        builder.Property(e => e.UserId).HasColumnName("user_id").ValueGeneratedOnAdd();

        builder.Property(e => e.UserCode).HasColumnName("user_code").HasMaxLength(20).IsRequired();
        builder.Property(e => e.LoginId).HasColumnName("login_id").HasMaxLength(50)
            .UseCollation("Latin1_General_CI_AS").IsRequired();
        builder.Property(e => e.UserName).HasColumnName("user_name").HasMaxLength(100).IsRequired();
        builder.Property(e => e.PasswordHash).HasColumnName("password_hash").HasMaxLength(255).IsRequired();
        builder.Property(e => e.RoleId).HasColumnName("role_id").IsRequired();
        builder.Property(e => e.EmployeeId).HasColumnName("employee_id");
        builder.Property(e => e.DepartmentId).HasColumnName("department_id");
        builder.Property(e => e.Email).HasColumnName("email").HasMaxLength(150);
        builder.Property(e => e.Mobile).HasColumnName("mobile").HasMaxLength(15);
        builder.Property(e => e.MustChangePassword).HasColumnName("must_change_password").IsRequired();
        builder.Property(e => e.FailedLoginCount).HasColumnName("failed_login_count").IsRequired();
        builder.Property(e => e.LockoutEndAt).HasColumnName("lockout_end_at");
        builder.Property(e => e.LastLoginAt).HasColumnName("last_login_at");
        builder.Property(e => e.IsActive).HasColumnName("is_active").IsRequired();

        builder.HasIndex(e => e.UserCode).IsUnique();
        builder.HasIndex(e => e.LoginId).IsUnique();

        builder.HasOne(e => e.Role)
            .WithMany(e => e.Users)
            .HasForeignKey(e => e.RoleId)
            .OnDelete(DeleteBehavior.NoAction);

        builder.ConfigureAuditColumns();
    }
}
