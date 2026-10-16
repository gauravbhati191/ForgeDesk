using ForgeDesk.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ForgeDesk.Infrastructure;

internal sealed class DependencyConfiguration : IEntityTypeConfiguration<Dependency>
{
    public void Configure(EntityTypeBuilder<Dependency> builder)
    {
        builder.ToTable("Dependencies");
        builder.HasKey(dependency => dependency.Id);
        builder.Property(dependency => dependency.RelationshipType).HasMaxLength(128).IsRequired();
        builder.Property(dependency => dependency.IsResolved).IsRequired();
        builder.HasIndex(dependency => new { dependency.ScanId, dependency.SourceSymbolId, dependency.TargetSymbolId, dependency.RelationshipType }).IsUnique();
        builder.HasOne(dependency => dependency.SourceSymbol)
            .WithMany()
            .HasForeignKey(dependency => dependency.SourceSymbolId)
            .OnDelete(DeleteBehavior.SetNull);
        builder.HasOne(dependency => dependency.TargetSymbol)
            .WithMany()
            .HasForeignKey(dependency => dependency.TargetSymbolId)
            .OnDelete(DeleteBehavior.SetNull);
    }
}
