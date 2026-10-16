using ForgeDesk.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ForgeDesk.Infrastructure;

internal sealed class ScanConfiguration : IEntityTypeConfiguration<Scan>
{
    public void Configure(EntityTypeBuilder<Scan> builder)
    {
        builder.ToTable("Scans");
        builder.HasKey(scan => scan.Id);
        builder.Property(scan => scan.Status).HasConversion<string>().HasMaxLength(32).IsRequired();
        builder.Property(scan => scan.QueuedAtUtc).IsRequired();
        builder.Property(scan => scan.ToolVersion).HasMaxLength(128).IsRequired();
        builder.Property(scan => scan.FilesDiscovered).IsRequired();
        builder.Property(scan => scan.FilesProcessed).IsRequired();
        builder.Property(scan => scan.CancellationRequested).IsRequired();
        builder.Property(scan => scan.ErrorMessage).HasMaxLength(4000);
        builder.HasIndex(scan => new { scan.ProjectId, scan.QueuedAtUtc });
        builder.HasMany(scan => scan.SourceFiles)
            .WithOne(sourceFile => sourceFile.Scan)
            .HasForeignKey(sourceFile => sourceFile.ScanId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.HasMany(scan => scan.Symbols)
            .WithOne(symbol => symbol.Scan)
            .HasForeignKey(symbol => symbol.ScanId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.HasMany(scan => scan.Dependencies)
            .WithOne(dependency => dependency.Scan)
            .HasForeignKey(dependency => dependency.ScanId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.HasMany(scan => scan.Findings)
            .WithOne(finding => finding.Scan)
            .HasForeignKey(finding => finding.ScanId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
