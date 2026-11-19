using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Rewind.Core.Catalog;
using Rewind.Core.Checkout;
using Rewind.Core.CloseOut;
using Rewind.Core.Concessions;
using Rewind.Core.Data;
using Rewind.Core.Devices;
using Rewind.Core.Intake;
using Rewind.Core.Labels;
using Rewind.Core.LateFees;
using Rewind.Core.Members;
using Rewind.Core.Metadata;
using Rewind.Core.MovieNights;
using Rewind.Core.Returns;
using Rewind.Core.StoreManager;

namespace Rewind.Core;

/// <summary>
/// Registers Rewind.Core with the host.
/// </summary>
public static class RewindCoreServices
{
    /// <summary>
    /// Adds Rewind's folders, the database, its backups, with the last one for the status bar, and the startup that
    /// prepares it, the sheets of store labels and member cards, whose numbers the database keeps, the members, the
    /// prices, the concession bar's snacks, the business days, the late-fee draws, the checkout counter, the return
    /// desk, the movies' posters, the catalog search, the phone catalog's writer, the copy to the backup drive with the
    /// PC's drives it looks among, and Close Out, which runs them. Call this before adding any other hosted service:
    /// hosted services start in the order they're added, and everything else needs the database.
    /// </summary>
    public static IServiceCollection AddRewindCore(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<RewindPaths>()
            .Bind(configuration.GetSection(RewindPaths.SectionName))
            .Validate(paths => Path.IsPathFullyQualified(paths.Root), "Paths:Root must be a full path, such as C:\\Rewind.")
            .ValidateOnStart(); // NET-11

        services.AddSingleton(TimeProvider.System); // NET-8
        services.AddSingleton(Random.Shared); // NET-8. The shared one is safe across threads, and it's a singleton here.

        // DATA-6: every read and write asks the factory for its own short-lived context.
        services.AddDbContextFactory<RewindDb>((provider, options) =>
        {
            var paths = provider.GetRequiredService<IOptions<RewindPaths>>().Value;
            options.UseSqlite(RewindDb.ConnectionStringFor(paths.Database));
        });

        // One for the whole register: the status bar listens to it as its last backup, so it has to be the one that
        // makes them.
        services.AddSingleton<Backups>();
        services.AddSingleton<ILastBackup>(provider => provider.GetRequiredService<Backups>());
        services.AddHostedService<DatabaseStartup>();

        // One of each for every screen: they hold no state, and ask the factory for each context (DATA-6).
        services.AddSingleton<ILabelSheets, LabelSheets>();
        services.AddSingleton<IMemberRoster, MemberRoster>();
        services.AddSingleton<IStorePrices, StorePrices>();
        services.AddSingleton<IConcessionBar, ConcessionBar>();
        services.AddSingleton<IBusinessDays, BusinessDays>();
        services.AddSingleton<ILateFeeDraws, LateFeeDraws>();
        services.AddSingleton<ICheckoutCounter, CheckoutCounter>();
        services.AddSingleton<IReturnDesk, ReturnDesk>();

        // The posters on the register's disk, which intake saves and the search shows. They're here, not with intake,
        // so a host without intake can search too. They hold no state either.
        services.AddSingleton<Posters>();
        services.AddSingleton<ICatalogSearch, CatalogSearch>();

        // Close Out's, which hold no state either: the phone catalog asks the factory for its context too, and the
        // copy looks for the backup drive each time it's called.
        services.AddSingleton<PhoneCatalog>();
        services.AddSingleton<IDrives, SystemDrives>();
        services.AddSingleton<BackupDrive>();

        // One for the whole register, since it does hold state: a second Close Out waits for the first.
        services.AddSingleton<ICloseOutRegister, CloseOutRegister>();
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
    /// Adds intake: TMDB's and UPCitemdb's settings and clients, and the services the Intake
    /// screen finds titles and movies with, which also need the database and the posters that
    /// <see cref="AddRewindCore"/> adds. TMDB's token is a secret, so the host reads it from
    /// somewhere outside the repo (SEC-1). UPCitemdb's free plan needs none.
    /// </summary>
    public static IServiceCollection AddRewindIntake(this IServiceCollection services, IConfiguration configuration)
    {
        // A missing token is allowed: only intake needs TMDB, and it says so (GEN-7).
        services.AddOptions<TmdbOptions>()
            .Bind(configuration.GetSection(TmdbOptions.SectionName))
            .Validate(tmdb => TmdbOptions.IsWellFormed(tmdb.AccessToken), "Tmdb:AccessToken must be TMDB's API Read Access Token alone, with no spaces, line breaks or \"Bearer\" in front.")
            .ValidateOnStart(); // NET-11
        services.AddOptions<UpcItemDbOptions>()
            .Bind(configuration.GetSection(UpcItemDbOptions.SectionName))
            .Validate(upcItemDb => UpcItemDbOptions.IsValidBaseUrl(upcItemDb.BaseUrl), "UpcItemDb:BaseUrl must be a full https address that ends in a slash, such as https://api.upcitemdb.com/prod/trial/.")
            .ValidateOnStart(); // NET-11

        // TmdbClient sets its own timeout and cap on answers. SEC-7: the HTTP client factory logs
        // every header at Trace, and the host holds it to warnings. The factory hides the headers'
        // values by default, and this keeps them all hidden whatever a later version's default, so
        // the token stays out of the log even if someone lifts that limit.
        services.AddHttpClient<TmdbClient>()
            .RedactLoggedHeaders(_ => true);

        // UpcItemDbClient sets its own timeout and cap too, and sends nothing secret. UPCitemdb's
        // limits count every lookup the register makes, so one pacer serves every screen.
        services.AddHttpClient<UpcItemDbClient>();
        services.AddSingleton<UpcItemDbPacer>();

        // Transient, like the clients they hold, so each screen gets its own, and the HTTP client
        // factory can renew its connections between them.
        services.AddTransient<IMovieIntake, MovieIntake>();
        services.AddTransient<IBarcodeTitles, BarcodeTitles>();
        return services;
    }
}
