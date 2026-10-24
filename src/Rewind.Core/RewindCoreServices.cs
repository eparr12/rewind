using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Rewind.Core.Data;
using Rewind.Core.Devices;
using Rewind.Core.Intake;
using Rewind.Core.Metadata;

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

    /// <summary>
    /// Adds the barcode scanner: its settings, the hub the screens listen to, the serial ports,
    /// and the reader that keeps it connected. Call this after <see cref="AddRewindCore"/>, so
    /// the database is ready first. The host adds the <see cref="IDeviceLocator"/> and the sound
    /// player it uses.
    /// </summary>
    public static IServiceCollection AddRewindScanner(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<ScannerOptions>()
            .Bind(configuration.GetSection(ScannerOptions.SectionName))
            .Validate(scanner => ScannerOptions.IsUsbId(scanner.VendorId), "Scanner:VendorId must be four hex digits from Device Manager, such as 0483.")
            .Validate(scanner => ScannerOptions.IsUsbId(scanner.ProductId), "Scanner:ProductId must be four hex digits from Device Manager, such as 374B.")
            .Validate(scanner => scanner.BaudRate > 0, "Scanner:BaudRate must be a positive number, such as 115200.")
            .ValidateOnStart(); // NET-11

        services.AddSingleton<ScannerHub>();
        services.AddSingleton<IScanner>(provider => provider.GetRequiredService<ScannerHub>());
        services.AddSingleton<ISerialPorts, SystemSerialPorts>();
        services.AddHostedService<SerialScanner>();
        return services;
    }

    /// <summary>
    /// Adds intake: TMDB's settings and client, the posters folder, and the service the Intake
    /// screen finds and adds movies with. TMDB's token is a secret, so the host reads it from
    /// somewhere outside the repo (SEC-1).
    /// </summary>
    public static IServiceCollection AddRewindIntake(this IServiceCollection services, IConfiguration configuration)
    {
        // A missing token is allowed: only intake needs TMDB, and it says so (GEN-7).
        services.AddOptions<TmdbOptions>()
            .Bind(configuration.GetSection(TmdbOptions.SectionName))
            .Validate(tmdb => TmdbOptions.IsWellFormed(tmdb.AccessToken), "Tmdb:AccessToken must be TMDB's API Read Access Token alone, with no spaces, line breaks or \"Bearer\" in front.")
            .ValidateOnStart(); // NET-11

        // TmdbClient sets its own timeout and cap on answers. SEC-7: the HTTP client factory logs
        // every header at Trace, and the host holds it to warnings. The factory hides the headers'
        // values by default, and this keeps them all hidden whatever a later version's default, so
        // the token stays out of the log even if someone lifts that limit.
        services.AddHttpClient<TmdbClient>()
            .RedactLoggedHeaders(_ => true);
        services.AddSingleton<Posters>();

        // Transient, like the TmdbClient it holds, so each screen gets its own, and the HTTP
        // client factory can renew its connections between them.
        services.AddTransient<IMovieIntake, MovieIntake>();
        return services;
    }
}
