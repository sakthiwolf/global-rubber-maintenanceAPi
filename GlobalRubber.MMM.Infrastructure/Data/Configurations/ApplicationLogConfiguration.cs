using GlobalRubber.MMM.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace GlobalRubber.MMM.Infrastructure.Data.Configurations;

public class ApplicationLogConfiguration : IEntityTypeConfiguration<ApplicationLog>
{
    public void Configure(EntityTypeBuilder<ApplicationLog> builder)
    {
        builder.ToTable("application_log", "audit");

        builder.HasKey(e => e.ApplicationLogId);
        builder.Property(e => e.ApplicationLogId).HasColumnName("application_log_id").ValueGeneratedOnAdd();

        builder.Property(e => e.LogLevel).HasColumnName("log_level").HasMaxLength(15).IsRequired();
        builder.Property(e => e.LogDateTime).HasColumnName("log_date_time").IsRequired();
        builder.Property(e => e.Message).HasColumnName("message").HasMaxLength(2000).IsRequired();
        builder.Property(e => e.Exception).HasColumnName("exception");
        builder.Property(e => e.Source).HasColumnName("source").HasMaxLength(200);
        builder.Property(e => e.RequestPath).HasColumnName("request_path").HasMaxLength(500);
        builder.Property(e => e.HttpMethod).HasColumnName("http_method").HasMaxLength(10);
        builder.Property(e => e.UserId).HasColumnName("user_id");
        builder.Property(e => e.CorrelationId).HasColumnName("correlation_id").HasMaxLength(50);
        builder.Property(e => e.MachineName).HasColumnName("machine_name").HasMaxLength(100);
        builder.Property(e => e.CreatedAt).HasColumnName("created_at").IsRequired();
    }
}
