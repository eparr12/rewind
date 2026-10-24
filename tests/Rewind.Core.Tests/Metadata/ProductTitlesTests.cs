using Rewind.Core.Metadata;

namespace Rewind.Core.Tests.Metadata;

public sealed class ProductTitlesTests
{
    // Build Plan 2.2's examples, and Tommy Boy as UPCitemdb names it.
    [Theory]
    [InlineData("Halloween (Widescreen DVD)", "Halloween")]
    [InlineData("Jurassic Park [DVD] [1993]", "Jurassic Park")]
    [InlineData("Toy Story 2 - Special Edition DVD", "Toy Story 2")]
    [InlineData("Full Metal Jacket (Widescreen)", "Full Metal Jacket")]
    [InlineData("Tommy Boy (DVD)", "Tommy Boy")]
    public void Clean_ProductTitle_LeavesTheMovies(string productTitle, string expected) =>
        Assert.Equal(expected, ProductTitles.Clean(productTitle));

    // Every edition word, as a whole word, in any case, with a space or a hyphen in a phrase
    // matching either.
    [Theory]
    [InlineData("Jaws DVD")]
    [InlineData("Jaws Blu-ray")]
    [InlineData("Jaws BLU-RAY")]
    [InlineData("Jaws Blu ray")]
    [InlineData("Jaws Widescreen")]
    [InlineData("Jaws Full Screen")]
    [InlineData("Jaws Full-Screen")]
    [InlineData("Jaws Fullscreen")]
    [InlineData("Jaws Two-Disc")]
    [InlineData("Jaws Two Disc")]
    [InlineData("Jaws 2-Disc")]
    [InlineData("Jaws Single Disc")]
    [InlineData("Jaws Unrated")]
    [InlineData("Jaws Remastered")]
    [InlineData("Jaws Director's Cut")]
    [InlineData("Jaws Director’s Cut")]
    [InlineData("Jaws Extended")]
    [InlineData("Jaws Theatrical")]
    [InlineData("Jaws Digital Copy")]
    [InlineData("Jaws Combo")]
    [InlineData("Jaws 4K")]
    [InlineData("Jaws UHD")]
    [InlineData("Jaws Ultra HD")]
    public void Clean_EditionWords_AreDropped(string productTitle) =>
        Assert.Equal("Jaws", ProductTitles.Clean(productTitle));

    // An edition word takes an "Edition", "Version" or "Cut" after it along.
    [Theory]
    [InlineData("Jaws Widescreen Edition")]
    [InlineData("Jaws Unrated Version")]
    [InlineData("Jaws Extended Cut")]
    [InlineData("Jaws Theatrical Version")]
    public void Clean_EditionWordAndWhatFollows_AreDropped(string productTitle) =>
        Assert.Equal("Jaws", ProductTitles.Clean(productTitle));

    // A kind of edition goes only before "Edition", one kind or more, with any ordinal before it.
    [Theory]
    [InlineData("Jaws Special Edition")]
    [InlineData("Jaws special   edition")]
    [InlineData("Jaws Collector's Edition")]
    [InlineData("Jaws Collector’s Edition")]
    [InlineData("Jaws Anniversary Edition")]
    [InlineData("Jaws 30th Anniversary Edition")]
    [InlineData("Jaws Special Collector's Edition")]
    [InlineData("Jaws 25th Anniversary")]
    [InlineData("Jaws Platinum Edition")]
    [InlineData("Jaws Deluxe Edition")]
    [InlineData("Jaws Limited Edition")]
    [InlineData("Jaws Ultimate Edition")]
    [InlineData("Jaws Signature Edition")]
    [InlineData("Jaws Definitive Edition")]
    [InlineData("Jaws Limited Collector's Edition")]
    public void Clean_EditionKinds_AreDropped(string productTitle) =>
        Assert.Equal("Jaws", ProductTitles.Clean(productTitle));

    // Only whole words and whole phrases go, and a kind of edition only before "Edition".
    [Theory]
    [InlineData("Full Metal Jacket")]
    [InlineData("Toy Story 2")]
    [InlineData("DVDs of Doom")]
    [InlineData("The Special")]
    [InlineData("The Anniversary Party")]
    [InlineData("Platinum Blonde")]
    [InlineData("Ultimate Avengers")]
    [InlineData("Signature Move")]
    [InlineData("Director's Choice")]
    public void Clean_PartOfAWordOrPhrase_IsKept(string productTitle) =>
        Assert.Equal(productTitle, ProductTitles.Clean(productTitle));

    [Theory]
    [InlineData("JAWS widescreen", "JAWS")]
    [InlineData("the matrix DVD", "the matrix")]
    public void Clean_WhatIsLeft_KeepsItsCase(string productTitle, string expected) =>
        Assert.Equal(expected, ProductTitles.Clean(productTitle));

    [Theory]
    [InlineData("Halloween (Widescreen", "Halloween")] // never closes, so the rest goes
    [InlineData("Halloween {Remastered} 1978", "Halloween 1978")]
    [InlineData("Halloween(Widescreen)1978", "Halloween 1978")]
    [InlineData("Halloween (Special [Collector's] Edition) DVD", "Halloween")]
    [InlineData("Halloween) 1978", "Halloween 1978")] // never opened, so only the bracket goes
    public void Clean_Brackets_DropWhatIsInThem(string productTitle, string expected) =>
        Assert.Equal(expected, ProductTitles.Clean(productTitle));

    // Including ones that brackets leave behind, which no word that goes was next to.
    [Theory]
    [InlineData("Widescreen: Jaws", "Jaws")]
    [InlineData("Jaws, Widescreen", "Jaws")]
    [InlineData("Jaws / DVD", "Jaws")]
    [InlineData("Jaws  -  DVD  -  Widescreen", "Jaws")]
    [InlineData("Jaws - (Widescreen)", "Jaws")]
    [InlineData("[Blu-ray] / Halloween:", "Halloween")]
    [InlineData("Star Wars: A New Hope DVD", "Star Wars: A New Hope")]
    public void Clean_SeparatorsLeftAtTheEnds_AreTrimmed(string productTitle, string expected) =>
        Assert.Equal(expected, ProductTitles.Clean(productTitle));

    // The separators right around a word that goes, go with it, wherever it is.
    [Theory]
    [InlineData("Jaws Blu-Ray/DVD Combo", "Jaws")]
    [InlineData("Jaws Blu-ray + DVD + Digital Copy", "Jaws")]
    [InlineData("Jaws - Widescreen - 1975", "Jaws 1975")]
    [InlineData("Jaws: Widescreen Edition", "Jaws")]
    public void Clean_SeparatorsAroundEditionWords_GoWithThem(string productTitle, string expected) =>
        Assert.Equal(expected, ProductTitles.Clean(productTitle));

    // Product lists file a title by its first word after an article, and TMDB knows it by its name.
    [Theory]
    [InlineData("Matrix, The", "The Matrix")]
    [InlineData("Matrix, The (DVD)", "The Matrix")]
    [InlineData("Matrix, The - Widescreen Edition", "The Matrix")]
    [InlineData("Christmas Story, A", "A Christmas Story")]
    [InlineData("American Werewolf in London, An", "An American Werewolf in London")]
    [InlineData("Good, the Bad and the Ugly, The", "The Good, the Bad and the Ugly")]
    [InlineData("matrix, the", "the matrix")]
    public void Clean_ArticleFiledAtTheEnd_GoesFirst(string productTitle, string expected) =>
        Assert.Equal(expected, ProductTitles.Clean(productTitle));

    // DATA-3: no title is null, never "".
    [Theory]
    [InlineData("DVD")]
    [InlineData("(Widescreen DVD)")]
    [InlineData(" - DVD - ")]
    [InlineData("Blu-ray + DVD + Digital Copy")]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void Clean_NothingLeft_IsNull(string? productTitle) =>
        Assert.Null(ProductTitles.Clean(productTitle));

    // SEC-5: however long the product's title, the movie's is capped.
    [Fact]
    public void Clean_LongTitle_IsCut() =>
        Assert.Equal(UpcItemDbClient.MaxTitleLength, ProductTitles.Clean(new string('x', UpcItemDbClient.MaxTitleLength + 1))?.Length);
}
