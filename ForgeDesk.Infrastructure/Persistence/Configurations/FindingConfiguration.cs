using ForgeDesk.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ForgeDesk.Infrastructure;

internal sealed class FindingConfiguration : IEntityTypeConfiguration<Finding>
{
    public void Configure(EntityTypeBuilder<Finding> builder)
    {
        builder.ToTable("Findings");
        builder.HasKey(finding => finding.Id);
        builder.Property(finding => finding.RuleId).HasMaxLength(256).IsRequired();
        builder.Property(finding => finding.Severity).HasConversion<string>().HasMaxLength(32).IsRequired();
        builder.Property(finding => finding.Message).HasMaxLength(4000).IsRequired();
        builder.Property(finding => finding.CreatedAtUtc).IsRequired();
        builder.HasIndex(finding => new { finding.ScanId, finding.Severity });
        builder.HasOne(finding => finding.Symbol)
            .WithMany()
            .HasForeignKey(finding => finding.SymbolId)
            .OnDelete(DeleteBehavior.SetNull);
    }
}
