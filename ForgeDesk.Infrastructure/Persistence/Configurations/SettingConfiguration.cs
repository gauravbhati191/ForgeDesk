using ForgeDesk.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ForgeDesk.Infrastructure;

internal sealed class SettingConfiguration : IEntityTypeConfiguration<Setting>
{
    public void Configure(EntityTypeBuilder<Setting> builder)
    {
        builder.ToTable("Settings");
        builder.HasKey(setting => setting.Key);
        builder.Property(setting => setting.Key).HasMaxLength(200).IsRequired();
        builder.Property(setting => setting.Value).HasMaxLength(4000).IsRequired();
        builder.Property(setting => setting.UpdatedAtUtc).IsRequired();
    }
}
