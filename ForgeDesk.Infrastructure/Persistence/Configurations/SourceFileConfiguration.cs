using ForgeDesk.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ForgeDesk.Infrastructure;

internal sealed class SourceFileConfiguration : IEntityTypeConfiguration<SourceFile>
{
    public void Configure(EntityTypeBuilder<SourceFile> builder)
    {
        builder.ToTable("SourceFiles");
        builder.HasKey(sourceFile => sourceFile.Id);
        builder.Property(sourceFile => sourceFile.RelativePath).HasMaxLength(4096).IsRequired();
        builder.Property(sourceFile => sourceFile.NormalizedPath).HasMaxLength(4096).IsRequired();
        builder.Property(sourceFile => sourceFile.FileName).HasMaxLength(512).IsRequired();
        builder.Property(sourceFile => sourceFile.Extension).HasMaxLength(32).IsRequired();
        builder.Property(sourceFile => sourceFile.ContentHash).HasMaxLength(128);
        builder.Property(sourceFile => sourceFile.FileSizeBytes).IsRequired();
        builder.Property(sourceFile => sourceFile.LastModifiedAtUtc).IsRequired();
        builder.Property(sourceFile => sourceFile.Language).HasMaxLength(128).IsRequired();
        builder.Property(sourceFile => sourceFile.DiscoveryStatus).HasConversion<string>().HasMaxLength(32).IsRequired();
        builder.HasIndex(sourceFile => new { sourceFile.ScanId, sourceFile.RelativePath }).IsUnique();
        builder.HasIndex(sourceFile => new { sourceFile.ScanId, sourceFile.Extension });
        builder.HasMany(sourceFile => sourceFile.Symbols)
            .WithOne(symbol => symbol.SourceFile)
            .HasForeignKey(symbol => symbol.SourceFileId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.HasMany(sourceFile => sourceFile.Findings)
            .WithOne(finding => finding.SourceFile)
            .HasForeignKey(finding => finding.SourceFileId)
            .OnDelete(DeleteBehavior.SetNull);
    }
}
