using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Rewind.Core.Catalog;
using Rewind.Core.Checkout;
using Rewind.Core.CloseOut;
using Rewind.Core.Concessions;
using Rewind.Core.Data;
using Rewind.Core.Labels;
using Rewind.Core.LateFees;
using Rewind.Core.Members;
using Rewind.Core.Metadata;
using Rewind.Core.MovieNights;
using Rewind.Core.Returns;
using Rewind.Core.Shelves;
using Rewind.Core.StoreManager;

namespace Rewind.Core.Tests;

public sealed class RewindCoreServicesTests
{
    private const string Token = "eyJ.test-token.signature";

    // SEC-7: the HTTP client factory writes each request's headers to the log at Trace. The host
    // holds that log to warnings, but the token stays out of it even if someone lifts that.
    [Fact]
    public async Task AddRewindIntake_LoggingAtTrace_KeepsTheTokenOutOfTheLog()
    {
        using var tmdb = new FakeTmdb();
        tmdb.AnswerJson(FakeTmdb.SearchFor("Halloween"), TmdbJson.HalloweenSearch);
        var log = new ListLoggerProvider();
        var services = new ServiceCollection();
        services.AddLogging(logging => logging.SetMinimumLevel(LogLevel.Trace).AddProvider(log));
        services.AddRewindIntake(new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { [$"{TmdbOptions.SectionName}:AccessToken"] = Token })
            .Build());
        services.AddHttpClient<TmdbClient>().ConfigurePrimaryHttpMessageHandler(() => tmdb);
        await using var provider = services.BuildServiceProvider();

        await provider.GetRequiredService<TmdbClient>().SearchAsync("Halloween", TestContext.Current.CancellationToken);

        Assert.Contains(log.Entries, entry => entry.Message.Contains("Authorization", StringComparison.Ordinal));
        Assert.DoesNotContain(log.Entries, entry => entry.Message.Contains(Token, StringComparison.Ordinal));
    }

    // The Store Manager injects it. A host without it would only fail once F5 opened.
    [Fact]
    public async Task AddRewindCore_AnyHost_ProvidesLabelSheets()
    {
        // Resolving it checks Paths:Root, and the default, C:\Rewind, is a full path only on Windows,
        // while CI runs on Linux. Nothing is opened under the temp folder.
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddRewindCore(new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { [$"{RewindPaths.SectionName}:Root"] = Path.GetTempPath() })
            .Build());
        await using var provider = services.BuildServiceProvider();

        Assert.IsType<LabelSheets>(provider.GetRequiredService<ILabelSheets>());
    }

    // The Store Manager's Members tab will inject it, and a host without it would only fail once
    // that tab opened.
    [Fact]
    public async Task AddRewindCore_AnyHost_ProvidesTheMemberRoster()
    {
        // A full path on Linux too, as for the label sheets above.
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddRewindCore(new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { [$"{RewindPaths.SectionName}:Root"] = Path.GetTempPath() })
            .Build());
        await using var provider = services.BuildServiceProvider();

        Assert.IsType<MemberRoster>(provider.GetRequiredService<IMemberRoster>());
    }

    // The Store Manager's Prices tab will inject it, and a host without it would only fail once that
    // tab opened.
    [Fact]
    public async Task AddRewindCore_AnyHost_ProvidesTheStorePrices()
    {
        // A full path on Linux too, as for the label sheets above.
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddRewindCore(new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { [$"{RewindPaths.SectionName}:Root"] = Path.GetTempPath() })
            .Build());
        await using var provider = services.BuildServiceProvider();

        Assert.IsType<StorePrices>(provider.GetRequiredService<IStorePrices>());
    }

    // The Store Manager's Snacks tab will inject it, and a host without it would only fail once that tab
    // opened.
    [Fact]
    public async Task AddRewindCore_AnyHost_ProvidesTheConcessionBar()
    {
        // A full path on Linux too, as for the label sheets above.
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddRewindCore(new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { [$"{RewindPaths.SectionName}:Root"] = Path.GetTempPath() })
            .Build());
        await using var provider = services.BuildServiceProvider();

        Assert.IsType<ConcessionBar>(provider.GetRequiredService<IConcessionBar>());
    }

    // The Store Manager's Shelves tab will inject it, and a host without it would only fail once that tab opened. It
    // holds no state, so one serves every screen. Making it reads no night: it asks the business days only when called.
    [Fact]
    public async Task AddRewindCore_AnyHost_ProvidesOneDisplayShelves()
    {
        // A full path on Linux too, as for the label sheets above.
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddRewindCore(new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { [$"{RewindPaths.SectionName}:Root"] = Path.GetTempPath() })
            .Build());
        await using var provider = services.BuildServiceProvider();

        var shelves = provider.GetRequiredService<IDisplayShelves>();

        Assert.IsType<DisplayShelves>(shelves);
        Assert.Same(shelves, provider.GetRequiredService<IDisplayShelves>());
    }

    // The night's first member card at Checkout and Close Out need it, and a host without it would only fail then.
    [Fact]
    public async Task AddRewindCore_AnyHost_ProvidesTheBusinessDays()
    {
        // A full path on Linux too, as for the label sheets above.
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddRewindCore(new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { [$"{RewindPaths.SectionName}:Root"] = Path.GetTempPath() })
            .Build());
        await using var provider = services.BuildServiceProvider();

        Assert.IsType<BusinessDays>(provider.GetRequiredService<IBusinessDays>());
    }

    // Checkout will inject it, and a host without it would only fail once a card was scanned.
    [Fact]
    public async Task AddRewindCore_AnyHost_ProvidesTheLateFeeDraws()
    {
        // A full path on Linux too, as for the label sheets above.
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddRewindCore(new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { [$"{RewindPaths.SectionName}:Root"] = Path.GetTempPath() })
            .Build());
        await using var provider = services.BuildServiceProvider();

        Assert.IsType<LateFeeDraws>(provider.GetRequiredService<ILateFeeDraws>());
    }

    // Checkout will inject it, and a host without it would only fail once F1 opened.
    [Fact]
    public async Task AddRewindCore_AnyHost_ProvidesTheCheckoutCounter()
    {
        // A full path on Linux too, as for the label sheets above.
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddRewindCore(new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { [$"{RewindPaths.SectionName}:Root"] = Path.GetTempPath() })
            .Build());
        await using var provider = services.BuildServiceProvider();

        Assert.IsType<CheckoutCounter>(provider.GetRequiredService<ICheckoutCounter>());
    }

    // Return will inject it, and a host without it would only fail once F2 opened.
    [Fact]
    public async Task AddRewindCore_AnyHost_ProvidesTheReturnDesk()
    {
        // A full path on Linux too, as for the label sheets above.
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddRewindCore(new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { [$"{RewindPaths.SectionName}:Root"] = Path.GetTempPath() })
            .Build());
        await using var provider = services.BuildServiceProvider();

        Assert.IsType<ReturnDesk>(provider.GetRequiredService<IReturnDesk>());
    }

    // Ask the Clerk injects it, and a host without it would only fail once F3 opened. It reads the posters, which
    // AddRewindCore adds too, so it needs no AddRewindIntake. Making it reads no folder.
    [Fact]
    public async Task AddRewindCore_AnyHost_ProvidesTheCatalogSearch()
    {
        // A full path on Linux too, as for the label sheets above.
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddRewindCore(new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { [$"{RewindPaths.SectionName}:Root"] = Path.GetTempPath() })
            .Build());
        await using var provider = services.BuildServiceProvider();

        Assert.IsType<CatalogSearch>(provider.GetRequiredService<ICatalogSearch>());
    }

    // Close Out will inject it, and a host without it would only fail at closing time. It holds no state, so one
    // serves every Close Out.
    [Fact]
    public async Task AddRewindCore_AnyHost_ProvidesOnePhoneCatalog()
    {
        // A full path on Linux too, as for the label sheets above.
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddRewindCore(new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { [$"{RewindPaths.SectionName}:Root"] = Path.GetTempPath() })
            .Build());
        await using var provider = services.BuildServiceProvider();

        Assert.Same(provider.GetRequiredService<PhoneCatalog>(), provider.GetRequiredService<PhoneCatalog>());
    }

    // The status bar injects it, and hears of each backup from it, so it has to be the backups the startup and Close Out
    // make theirs with: a second one would never raise a thing. Making it reads no folder.
    [Fact]
    public async Task AddRewindCore_AnyHost_ProvidesTheBackupsAsTheLastBackup()
    {
        // A full path on Linux too, as for the label sheets above.
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddRewindCore(new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { [$"{RewindPaths.SectionName}:Root"] = Path.GetTempPath() })
            .Build());
        await using var provider = services.BuildServiceProvider();

        Assert.Same(provider.GetRequiredService<Backups>(), provider.GetRequiredService<ILastBackup>());
    }

    // Close Out will inject it, and a host without it would only fail at closing time. It holds no state, so one serves
    // every Close Out. Making it looks at no drive: the PC's drives are listed only when a copy asks for them.
    [Fact]
    public async Task AddRewindCore_AnyHost_ProvidesOneBackupDrive()
    {
        // A full path on Linux too, as for the label sheets above.
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddRewindCore(new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { [$"{RewindPaths.SectionName}:Root"] = Path.GetTempPath() })
            .Build());
        await using var provider = services.BuildServiceProvider();

        Assert.Same(provider.GetRequiredService<BackupDrive>(), provider.GetRequiredService<BackupDrive>());
    }

    // The Close Out Register button will inject it, and a host without it would only fail at closing time. It's one for
    // the whole register, so a second Close Out waits for the first, from whichever screen. Making it looks at no drive.
    [Fact]
    public async Task AddRewindCore_AnyHost_ProvidesOneCloseOutRegister()
    {
        // A full path on Linux too, as for the label sheets above.
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddRewindCore(new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { [$"{RewindPaths.SectionName}:Root"] = Path.GetTempPath() })
            .Build());
        await using var provider = services.BuildServiceProvider();

        var closeOut = provider.GetRequiredService<ICloseOutRegister>();

        Assert.IsType<CloseOutRegister>(closeOut);
        Assert.Same(closeOut, provider.GetRequiredService<ICloseOutRegister>());
    }

    // The services that use it are singletons, so two scans can reach it at once. A Random of its own wouldn't
    // be safe across threads, and could quietly start giving nothing but zeros.
    [Fact]
    public async Task AddRewindCore_AnyHost_ProvidesTheSharedRandom()
    {
        // A full path on Linux too, as for the label sheets above.
        var services = new ServiceCollection();
        services.AddRewindCore(new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { [$"{RewindPaths.SectionName}:Root"] = Path.GetTempPath() })
            .Build());
        await using var provider = services.BuildServiceProvider();

        Assert.Same(Random.Shared, provider.GetRequiredService<Random>());
    }

    // UPCitemdb's limits count every lookup the register makes, whichever screen asks.
    [Fact]
    public async Task AddRewindIntake_EveryScreen_SharesOnePacer()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(TimeProvider.System);
        services.AddRewindIntake(new ConfigurationBuilder().Build());
        await using var provider = services.BuildServiceProvider();

        Assert.Same(provider.GetRequiredService<UpcItemDbPacer>(), provider.GetRequiredService<UpcItemDbPacer>());
    }
}
