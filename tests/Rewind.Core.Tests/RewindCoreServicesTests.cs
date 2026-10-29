using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Rewind.Core.Concessions;
using Rewind.Core.Labels;
using Rewind.Core.Members;
using Rewind.Core.Metadata;
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
