using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Rewind.Core.Catalog;
using Rewind.Core.Concessions;
using Rewind.Core.Members;
using Rewind.Core.Metadata;
using Rewind.Core.StoreManager;

namespace Rewind.Core.Data;

/// <summary>
/// The family's database: one SQLite file on the register (DATA-1). Get a context from
/// <see cref="IDbContextFactory{TContext}"/> for each operation and dispose it (DATA-6).
/// </summary>
public sealed class RewindDb(DbContextOptions<RewindDb> options) : DbContext(options)
{
    /// <summary>The store's single row of prices and settings.</summary>
    public DbSet<StoreSettings> StoreSettings => Set<StoreSettings>();

    /// <summary>The family members who rent movies.</summary>
    public DbSet<Member> Members => Set<Member>();

    /// <summary>The snacks and drinks sold at the concession bar.</summary>
    public DbSet<ConcessionItem> ConcessionItems => Set<ConcessionItem>();

    /// <summary>The films in the collection, with TMDB's details (DATA-9).</summary>
    public DbSet<Movie> Movies => Set<Movie>();

    /// <summary>Each movie's top-billed cast, from TMDB.</summary>
    public DbSet<CastMember> MovieCast => Set<CastMember>();

    /// <summary>The genres from TMDB.</summary>
    public DbSet<Genre> Genres => Set<Genre>();

    /// <summary>Which genres each movie has.</summary>
    public DbSet<MovieGenre> MovieGenres => Set<MovieGenre>();

    /// <summary>The physical cases, each with its store label.</summary>
    public DbSet<Case> Cases => Set<Case>();

    /// <summary>Which movies each case holds.</summary>
    public DbSet<CaseMovie> CaseMovies => Set<CaseMovie>();

    /// <summary>UPCitemdb's answers, so each barcode is looked up once.</summary>
    public DbSet<UpcLookup> UpcLookups => Set<UpcLookup>();

    /// <summary>
    /// The connection string for a Rewind database file. Foreign Keys=True sends
    /// PRAGMA foreign_keys = 1 each time a connection opens, outside any transaction (DATA-1).
    /// </summary>
    public static string ConnectionStringFor(string databaseFile) =>
        new SqliteConnectionStringBuilder
        {
            DataSource = databaseFile,
            ForeignKeys = true,
        }.ToString();

    /// <inheritdoc />
    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        // DATA-8: SQLite hands DateTimes back as Unspecified, so every one is marked UTC. EF Core
        // applies a value type's conventions to its nullable form too, so this covers DateTime?.
        configurationBuilder.Properties<DateTime>().HaveConversion<UtcConverter>();
    }

    /// <inheritdoc />
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(RewindDb).Assembly);

        // DATA-4: EF Core cascades required relationships by default, so one deleted movie
        // would erase its watch log. This runs last, so it covers every foreign key.
        foreach (var foreignKey in modelBuilder.Model.GetEntityTypes().SelectMany(e => e.GetForeignKeys()))
        {
            foreignKey.DeleteBehavior = DeleteBehavior.Restrict;
        }
    }
}
