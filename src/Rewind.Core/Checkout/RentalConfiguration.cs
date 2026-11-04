using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Rewind.Core.Members;
using Rewind.Core.MovieNights;

namespace Rewind.Core.Checkout;

/// <summary>
/// The database holds the rentals' rules it can (DATA-11): enough cash for the total, and change that's
/// exactly the difference. That the total is what the items and lines come to crosses tables, so a CHECK
/// can't hold it, and Core works it out.
/// </summary>
public sealed class RentalConfiguration : IEntityTypeConfiguration<Rental>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<Rental> builder)
    {
        builder.HasOne<Member>().WithMany().HasForeignKey(r => r.MemberId);
        builder.HasOne<BusinessDay>().WithMany().HasForeignKey(r => r.BusinessDayId);
        builder.ToTable(t =>
        {
            t.HasCheckConstraint("CK_Rentals_TotalCents", "\"TotalCents\" >= 0");
            t.HasCheckConstraint("CK_Rentals_CashTenderedCents", "\"CashTenderedCents\" >= \"TotalCents\"");
            t.HasCheckConstraint("CK_Rentals_ChangeCents", "\"ChangeCents\" = \"CashTenderedCents\" - \"TotalCents\"");
        });
    }
}
