using Rewind.Core.Catalog;
using Rewind.Core.Checkout;
using Rewind.Core.Concessions;
using Rewind.Core.Data;
using Rewind.Core.LateFees;
using Rewind.Core.Members;
using Rewind.Core.MovieNights;

namespace Rewind.Core.Tests.Checkout;

/// <summary>
/// The rows a sale points at, saved before a test: a member, a night, a snack, a double feature, and a movie
/// that's in no case. There's no draw unless a test adds one, so a refusal to delete the member or the night
/// can only come from a rental.
/// </summary>
internal sealed record SaleParts(int MemberId, int NightId, int SnackId, int CaseId, int MovieId, int SecondMovieId, int MovieInNoCaseId)
{
    /// <summary>A key no row has.</summary>
    public const int NoSuchId = 999;

    private static readonly DateTime MovieNight = new(2026, 10, 24, 2, 15, 0, DateTimeKind.Utc);

    public static async Task<SaleParts> AddAsync(RewindDb db, CancellationToken cancellationToken)
    {
        var member = new Member { Name = "Dad", CardBarcode = "MEM-001" };
        var night = new BusinessDay { OpenedAt = MovieNight };
        var snack = new ConcessionItem { Name = "Popcorn", Code = "CON-POPCORN", PriceCents = 100 };
        var movie = new Movie { Title = "Halloween", Year = 1978 };
        var secondMovie = new Movie { Title = "Halloween II", Year = 1981 };
        var movieInNoCase = new Movie { Title = "Tommy Boy", Year = 1995 };
        var doubleFeature = new Case { StoreLabel = "RV-00042" };
        doubleFeature.Movies.Add(movie);
        doubleFeature.Movies.Add(secondMovie);
        db.AddRange(member, night, snack, movieInNoCase, doubleFeature);
        await db.SaveChangesAsync(cancellationToken);
        return new(member.Id, night.Id, snack.Id, doubleFeature.Id, movie.Id, secondMovie.Id, movieInNoCase.Id);
    }

    /// <summary>An Overdue draw, for a fee line to point at.</summary>
    public async Task<int> AddDrawAsync(RewindDb db, CancellationToken cancellationToken)
    {
        var draw = new LateFeeDraw { MemberId = MemberId, BusinessDayId = NightId, Card = Card.Overdue, DrawnAt = MovieNight };
        db.LateFeeDraws.Add(draw);
        await db.SaveChangesAsync(cancellationToken);
        return draw.Id;
    }

    /// <summary>A new rental for this member and night, whose change is the cash less the total.</summary>
    public Rental NewRental(int totalCents = 0, int cashTenderedCents = 0) =>
        new()
        {
            MemberId = MemberId,
            BusinessDayId = NightId,
            RentedAt = MovieNight,
            TotalCents = totalCents,
            CashTenderedCents = cashTenderedCents,
            ChangeCents = cashTenderedCents - totalCents,
        };

    /// <summary>A saved rental, for an item or a line to point at.</summary>
    public async Task<int> AddRentalAsync(RewindDb db, CancellationToken cancellationToken)
    {
        var rental = NewRental();
        db.Rentals.Add(rental);
        await db.SaveChangesAsync(cancellationToken);
        return rental.Id;
    }
}
