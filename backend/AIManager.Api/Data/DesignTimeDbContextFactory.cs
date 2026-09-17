using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace AIManager.Api.Data;

/// <summary>
/// Lets `dotnet ef` build the context at design time without booting the web host.
/// The connection string only needs to be valid for the provider — no live DB is required
/// to add/script migrations.
/// </summary>
public class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<AppDbContext>
{
    public AppDbContext CreateDbContext(string[] args)
    {
        var cs = Environment.GetEnvironmentVariable("ConnectionStrings__DefaultConnection")
                 ?? "Host=localhost;Port=5434;Database=aimanager;Username=aimanager;Password=aimanager";
        var options = new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(cs).Options;
        return new AppDbContext(options);
    }
}
