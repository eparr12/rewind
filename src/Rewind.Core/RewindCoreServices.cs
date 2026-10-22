using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Rewind.Core.Data;

namespace Rewind.Core;

/// <summary>
/// Registers Rewind.Core with the host.
/// </summary>
public static class RewindCoreServices
{
    /// <summary>
    /// Adds Rewind's folders, the database and the startup that prepares it. Call this before
    /// adding any other hosted service: hosted services start in the order they're added, and
    /// everything else needs the database.
    /// </summary>
    public static IServiceCollection AddRewindCore(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<RewindPaths>()
            .Bind(configuration.GetSection(RewindPaths.SectionName))
            .Validate(paths => Path.IsPathFullyQualified(paths.Root), "Paths:Root must be a full path, such as C:\\Rewind.")
            .ValidateOnStart(); // NET-11

        services.AddSingleton(TimeProvider.System); // NET-8

        // DATA-6: every read and write asks the factory for its own short-lived context.
        services.AddDbContextFactory<RewindDb>((provider, options) =>
        {
            var paths = provider.GetRequiredService<IOptions<RewindPaths>>().Value;
            options.UseSqlite(RewindDb.ConnectionStringFor(paths.Database));
        });

        services.AddSingleton<Backups>();
        services.AddHostedService<DatabaseStartup>();
        return services;
    }
}
