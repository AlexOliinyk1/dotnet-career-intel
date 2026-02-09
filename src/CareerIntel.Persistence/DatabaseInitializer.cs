using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace CareerIntel.Persistence;

/// <summary>
/// Provides static initialization for the CareerIntel SQLite database.
/// Creates the database file and schema if they do not already exist.
/// </summary>
public static class DatabaseInitializer
{
    /// <summary>
    /// Default relative path for the SQLite database file.
    /// </summary>
    public const string DefaultDbPath = "data/career-intel.db";

    /// <summary>
    /// Ensures the SQLite database is created at the specified path.
    /// Creates the parent directory if it does not exist.
    /// Uses EnsureCreated for schema initialization (no migrations required).
    /// </summary>
    /// <param name="dbPath">
    /// Optional path to the database file. Defaults to <see cref="DefaultDbPath"/>
    /// relative to the current working directory.
    /// </param>
    /// <param name="logger">Optional logger for initialization diagnostics.</param>
    /// <returns>The configured <see cref="CareerIntelDbContext"/> ready for use.</returns>
    public static async Task<CareerIntelDbContext> InitializeAsync(
        string? dbPath = null,
        ILogger? logger = null)
    {
        logger ??= NullLogger.Instance;

        var resolvedPath = dbPath ?? DefaultDbPath;
        var fullPath = Path.GetFullPath(resolvedPath);
        var directory = Path.GetDirectoryName(fullPath);

        if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
        {
            Directory.CreateDirectory(directory);
            logger.LogInformation("Created database directory: {Directory}", directory);
        }

        var connectionString = $"Data Source={fullPath}";

        var options = new DbContextOptionsBuilder<CareerIntelDbContext>()
            .UseSqlite(connectionString)
            .Options;

        var context = new CareerIntelDbContext(options);

        var created = await context.Database.EnsureCreatedAsync();

        if (created)
        {
            logger.LogInformation("Created new CareerIntel database at: {Path}", fullPath);
        }
        else
        {
            logger.LogInformation("CareerIntel database already exists at: {Path}", fullPath);
        }

        return context;
    }

    /// <summary>
    /// Creates a DbContext configured for the specified SQLite database path.
    /// Does not initialize or create the database — use <see cref="InitializeAsync"/> for that.
    /// </summary>
    public static CareerIntelDbContext CreateContext(string? dbPath = null)
    {
        var resolvedPath = dbPath ?? DefaultDbPath;
        var fullPath = Path.GetFullPath(resolvedPath);
        var connectionString = $"Data Source={fullPath}";

        var options = new DbContextOptionsBuilder<CareerIntelDbContext>()
            .UseSqlite(connectionString)
            .Options;

        return new CareerIntelDbContext(options);
    }
}
