using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Rewind.Core.Data;
using Rewind.Core.Devices;

namespace Rewind.Core.Tests;

/// <summary>
/// A throwaway Rewind folder for one test. Rewind.Core starts the way Program.cs starts it,
/// over real SQLite files (TEST-3), and the clock is fake (TEST-4), and so are the PC's drives
/// (TEST-5). Every test makes its own, so tests never share a database (TEST-7).
/// </summary>
internal sealed class TempRewind : IAsyncDisposable
{
    // Movie night: 10:15 PM on October 23, 2026, on a register clock four hours behind UTC.
    private static readonly DateTimeOffset MovieNight = new(2026, 10, 24, 2, 15, 0, TimeSpan.Zero);
    private static readonly TimeZoneInfo RegisterZone = TimeZoneInfo.CreateCustomTimeZone(
        "Register", TimeSpan.FromHours(-4), "Register", "Register");

    private readonly List<IHost> _hosts = [];

    public TempRewind()
    {
        Time.SetLocalTimeZone(RegisterZone);
        Drives = new(Path.Combine(Paths.Root, "drives"));
    }

    public RewindPaths Paths { get; } = new() { Root = Path.Combine(Path.GetTempPath(), $"rewind-test-{Guid.NewGuid():N}") };

    public WatchedTimeProvider Time { get; } = new(MovieNight);

    public Random Random { get; } = new(42); // TEST-4: seeded, so chance goes the same way on every run

    /// <summary>
    /// The PC's drives, each a folder under this root, which the host is given in place of the real
    /// ones: a backup drive left plugged in during dotnet test is never even looked at.
    /// </summary>
    public FakeDrives Drives { get; }

    public Backups Backups => new(Options.Create(Paths), Time, NullLogger<Backups>.Instance);

    /// <summary>Starts Rewind.Core the way Program.cs does, so DatabaseStartup runs.</summary>
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        var builder = Host.CreateEmptyApplicationBuilder(settings: null);
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            [$"{RewindPaths.SectionName}:Root"] = Paths.Root,
        });
        builder.Services.AddRewindCore(builder.Configuration);
        builder.Services.AddSingleton<TimeProvider>(Time);
        builder.Services.AddSingleton(Random);
        builder.Services.AddSingleton<IDrives>(Drives);

        var host = builder.Build();
        _hosts.Add(host);
        await host.StartAsync(cancellationToken);
    }

    /// <summary>The database factory that the Rewind.Core StartAsync started hands to services (DATA-6).</summary>
    public IDbContextFactory<RewindDb> DbFactory => _hosts[^1].Services.GetRequiredService<IDbContextFactory<RewindDb>>();

    /// <summary>The drives that the Rewind.Core StartAsync started hands to services, which are <see cref="Drives"/>.</summary>
    public IDrives HostDrives => _hosts[^1].Services.GetRequiredService<IDrives>();

    /// <summary>Opens the live database, or another file such as a backup, as the register does.</summary>
    public RewindDb OpenDb(string? databaseFile = null) =>
        new(new DbContextOptionsBuilder<RewindDb>()
            .UseSqlite(RewindDb.ConnectionStringFor(databaseFile ?? Paths.Database))
            .Options);

    public string[] BackupFiles() =>
        Directory.Exists(Paths.Backups) ? Directory.GetFiles(Paths.Backups) : [];

    public async ValueTask DisposeAsync()
    {
        foreach (var host in _hosts)
        {
            await host.StopAsync();
            host.Dispose();
        }

        SqliteConnection.ClearAllPools(); // pooled connections keep the files open on Windows
        if (Directory.Exists(Paths.Root))
        {
            Directory.Delete(Paths.Root, recursive: true);
        }
    }
}
