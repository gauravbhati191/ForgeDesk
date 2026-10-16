using ForgeDesk.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ForgeDesk.Infrastructure;

internal sealed class ProjectConfiguration : IEntityTypeConfiguration<Project>
{
    public void Configure(EntityTypeBuilder<Project> builder)
    {
        builder.ToTable("Projects");
        builder.HasKey(project => project.Id);
        builder.Property(project => project.Name).HasMaxLength(200).IsRequired();
        builder.Property(project => project.RootPath).HasMaxLength(4096).IsRequired();
        builder.Property(project => project.NormalizedRootPath).HasMaxLength(4096).IsRequired();
        builder.Property(project => project.ProjectType).HasConversion<string>().HasMaxLength(32).IsRequired();
        builder.Property(project => project.Status).HasConversion<string>().HasMaxLength(32).IsRequired();
        builder.Property(project => project.Description).HasMaxLength(2000);
        builder.Property(project => project.CreatedAtUtc).IsRequired();
        builder.Property(project => project.UpdatedAtUtc).IsRequired();
        builder.Property(project => project.IsFavorite).IsRequired();
        builder.HasIndex(project => project.NormalizedRootPath).IsUnique();
        builder.HasIndex(project => new { project.IsFavorite, project.LastOpenedAtUtc });
        builder.HasMany(project => project.Scans)
            .WithOne(scan => scan.Project)
            .HasForeignKey(scan => scan.ProjectId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
