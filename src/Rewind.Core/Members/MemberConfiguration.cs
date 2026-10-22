using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Rewind.Core.Members;

/// <summary>
/// The database holds the members' rules it can: one member per card (DATA-11).
/// </summary>
public sealed class MemberConfiguration : IEntityTypeConfiguration<Member>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<Member> builder) =>
        builder.HasIndex(m => m.CardBarcode).IsUnique();
}
