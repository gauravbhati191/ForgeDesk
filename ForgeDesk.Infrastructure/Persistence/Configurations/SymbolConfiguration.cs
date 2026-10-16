using ForgeDesk.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ForgeDesk.Infrastructure;

internal sealed class SymbolConfiguration : IEntityTypeConfiguration<Symbol>
{
    public void Configure(EntityTypeBuilder<Symbol> builder)
    {
        builder.ToTable("Symbols");
        builder.HasKey(symbol => symbol.Id);
        builder.Property(symbol => symbol.SymbolKey).HasMaxLength(2000).IsRequired();
        builder.Property(symbol => symbol.Name).HasMaxLength(512).IsRequired();
        builder.Property(symbol => symbol.Kind).HasConversion<string>().HasMaxLength(32).IsRequired();
        builder.HasIndex(symbol => new { symbol.ScanId, symbol.SymbolKey }).IsUnique();
        builder.HasIndex(symbol => new { symbol.ScanId, symbol.SourceFileId, symbol.Kind });
    }
}
