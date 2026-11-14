using Rewind.Core.Members;

namespace Rewind.Core.Returns;

/// <summary>A rental item that's back, with what the screen asks who watched it with.</summary>
/// <param name="RentalItemId">The rental item, which the screen hands back with who watched.</param>
/// <param name="StoreLabel">The store label on the case, such as RV-00042.</param>
/// <param name="Title">The title of the movie picked from the case, such as Halloween.</param>
/// <param name="Year">The year that movie came out. Null when it isn't known (DATA-3).</param>
/// <param name="RentedBy">The name of the member who rented it, as the register shows it, active or not.</param>
/// <param name="NightOpenedAt">
/// When the night it was rented on opened, in UTC (DATA-8). The screen names the night by this, not by the
/// rental's own time, since a night can run past midnight.
/// </param>
/// <param name="People">The members to ask: the active ones, in the roster's order.</param>
public sealed record ReturnedRental(
    int RentalItemId,
    string StoreLabel,
    string Title,
    int? Year,
    string RentedBy,
    DateTime NightOpenedAt,
    IReadOnlyList<MemberRow> People);
