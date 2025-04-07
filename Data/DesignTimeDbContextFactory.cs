using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using TicTacToeBlazor.Data; // Make sure this using matches your DbContext namespace

public class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<ApplicationDbContext>
{
    public ApplicationDbContext CreateDbContext(string[] args)
    {
        // Build configuration manually to read appsettings.json
        // We need to do this because the design-time tools don't run the full app host
        IConfigurationRoot configuration = new ConfigurationBuilder()
            .SetBasePath(Directory.GetCurrentDirectory()) // Assumes tools run from project root
            .AddJsonFile("appsettings.json")
            .Build();

        // Create DbContextOptionsBuilder
        var builder = new DbContextOptionsBuilder<ApplicationDbContext>();

        // Get connection string from configuration
        var connectionString = configuration.GetConnectionString("DefaultConnection");

        // Configure the DbContext to use SQLite (or your chosen provider)
        builder.UseSqlite(connectionString);

        // Return the new DbContext instance
        return new ApplicationDbContext(builder.Options);
    }
}