using ForgeDesk.Domain;
using Microsoft.EntityFrameworkCore;

namespace ForgeDesk.Infrastructure;

/// <summary>Central EF Core context for all local ForgeDesk state.</summary>
public sealed class ForgeDeskDbContext : DbContext
{
    public ForgeDeskDbContext(DbContextOptions<ForgeDeskDbContext> options) : base(options)
    {
    }

    public DbSet<Project> Projects => Set<Project>();
    public DbSet<Scan> Scans => Set<Scan>();
    public DbSet<SourceFile> SourceFiles => Set<SourceFile>();
    public DbSet<Symbol> Symbols => Set<Symbol>();
    public DbSet<Dependency> Dependencies => Set<Dependency>();
    public DbSet<Finding> Findings => Set<Finding>();
    public DbSet<Setting> Settings => Set<Setting>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(ForgeDeskDbContext).Assembly);
        base.OnModelCreating(modelBuilder);
    }
}
