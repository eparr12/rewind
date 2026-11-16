# catalog.json

The phone catalog's file, and the only thing the phone app shares with Rewind (ARCH-8). Close Out writes it to the outbox, `C:\Rewind\outbox`, beside a `thumbs` folder of small posters (Build Plan 3.5). From Build Plan 7.2, Close Out uploads both to the NAS's catalog folder, and the phone app reads them from there. [PhoneCatalog.cs](PhoneCatalog.cs) writes them.

## Example

```json
{
  "version": 1,
  "generatedAt": "2026-10-23T22:15:00Z",
  "movies": [
    { "title": "Jaws", "year": 1975, "rating": "PG",
      "minutes": 124, "genres": ["Adventure", "Horror"],
      "thumb": "thumbs/578.jpg", "upcs": ["012345678905"] }
  ]
}
```

## Fields

Unknown is null, never 0, an empty string or NR (DATA-3). A list is never null: it's empty when there's nothing in it.

| Field | Type | Null means |
| --- | --- | --- |
| `version` | number | Never null. The format's version, 1 for now; see [Versions](#versions). |
| `generatedAt` | string | Never null. When Close Out wrote the file, in UTC to the second, ending in Z. The app shows it as the catalog's date, so you know how fresh it is. |
| `movies` | array of movies | Never null. Empty when no movie is in the catalog. |

Each movie has these seven fields, and nothing else:

| Field | Type | Null means |
| --- | --- | --- |
| `title` | string | Never null or blank. |
| `year` | number or null | The year it came out isn't known. |
| `rating` | string or null | Its US rating isn't known. Otherwise it's G, PG, PG-13, R or NC-17, written just so. |
| `minutes` | number or null | How long it runs isn't known. |
| `genres` | array of strings | Never null. TMDB's genre names, such as Horror. Empty for a movie with none, such as one entered by hand. |
| `thumb` | string or null | It has no thumbnail: it was entered by hand, or its poster never downloaded. Otherwise it's the thumbnail's path from catalog.json, with a forward slash, such as `thumbs/578.jpg`. |
| `upcs` | array of strings | Never null. The factory barcodes of its cases that aren't retired, each once. Empty when none of those cases came with one. |

## Which movies are in

A movie is in the catalog when at least one of its cases isn't retired. The catalog answers "Do we already own this?" in a thrift store, and a movie whose only case is retired isn't ours any more. A case that's checked out still counts. A movie in no case isn't in, and a retired case's barcode isn't listed.

The catalog holds movies only (SEC-2): no member's name or card, no prices, and no watch history or stars.

## UPCs

A UPC names an edition, not a movie. Two editions of one movie have a UPC each, two copies of one edition share theirs, and a box set's UPC is on every movie in it.

Each is 12 or 13 digits, in the form the register saves: a 13-digit EAN that starts with 0 is the same product as its 12-digit UPC-A, so it's listed as the 12 digits. An 8-digit UPC-E is never listed. Expand a scanned UPC-E to its UPC-A and normalize it the same way before looking it up (Build Plan 7.4).

## Order

Movies come in order of title, then year, with an unknown year first, then the order they were added to Rewind. Titles compare character by character, by each character's code, so capitals come before lower case, and "home movies" comes after "Jaws". Each movie's genres and UPCs are in the same order. So the same collection always writes the same file, apart from `generatedAt`. An app that wants another order sorts the movies itself.

## Thumbnails

The `thumbs` folder holds JPEG posters 154 pixels wide, each named for its movie's TMDB ID, such as `578.jpg`, never for its title (SEC-5). Close Out copies a movie's thumbnail there from the register's `data\thumbs` when the outbox doesn't have it, or has different bytes under its name, and writes catalog.json last, so the file never names a thumbnail that isn't there yet. Build Plan 7.2 uploads them in the same order. A thumbnail keeps its name when its movie's poster changes, so a file can change under the same name.

Nothing is ever deleted from the outbox, so the folder can hold thumbnails the catalog no longer names, such as a retired movie's. They're harmless.

## Versions

An app reads the fields it knows and ignores the rest, so adding a field never changes `version`. It goes up only for a change that an app built for the old version would misread: a field removed or renamed, a field whose type changes, or a value that comes to mean something else, such as `minutes` becoming seconds, or null coming to mean something other than unknown. A change to the order leaves it alone. An app that finds a version it doesn't know should say it needs updating, rather than guess.
