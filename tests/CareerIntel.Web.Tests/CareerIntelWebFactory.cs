using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using CareerIntel.Persistence;

namespace CareerIntel.Web.Tests;

/// <summary>
/// Custom WebApplicationFactory that configures the app for integration testing.
/// Uses an isolated temp data directory and in-process SQLite so tests don't affect real data.
/// Auth is disabled so pages are accessible without login.
/// </summary>
public sealed class CareerIntelWebFactory : WebApplicationFactory<Program>
{
    private readonly string _tempDataDir = Path.Combine(Path.GetTempPath(), $"ci-test-{Guid.NewGuid():N}");

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        Directory.CreateDirectory(_tempDataDir);

        // Auth disabled → all pages accessible without login
        var authConfigPath = Path.Combine(_tempDataDir, "auth-config.json");
        File.WriteAllText(authConfigPath, """{"PasswordHash":"","Enabled":false,"MustChangePassword":false}""");

        builder.UseEnvironment("Development");

        builder.ConfigureServices(services =>
        {
            // Disable authentication so all pages are accessible without login
            var authDescriptor = services.FirstOrDefault(d => d.ServiceType == typeof(AuthConfig));
            if (authDescriptor != null) services.Remove(authDescriptor);
            services.AddSingleton(new AuthConfig { Enabled = false });

            // Replace DataDirectoryConfig with isolated temp dir
            var dataDirDescriptor = services.FirstOrDefault(d => d.ServiceType == typeof(DataDirectoryConfig));
            if (dataDirDescriptor != null) services.Remove(dataDirDescriptor);
            services.AddSingleton(new DataDirectoryConfig { Path = _tempDataDir });

            // Replace DbContext to use isolated temp SQLite
            var dbDescriptor = services.FirstOrDefault(d => d.ServiceType == typeof(DbContextOptions<CareerIntelDbContext>));
            if (dbDescriptor != null) services.Remove(dbDescriptor);

            var testDbPath = Path.Combine(_tempDataDir, "test.db");
            services.AddDbContext<CareerIntelDbContext>(options =>
                options.UseSqlite($"Data Source={testDbPath}"));
        });
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        try { Directory.Delete(_tempDataDir, recursive: true); } catch { /* cleanup best-effort */ }
    }
}
