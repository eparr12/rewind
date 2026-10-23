# Sounds

Rewind plays these five WAV files (UI-7). They aren't in Git (GIT-6), so keep the originals somewhere safe and copy them here on a new PC. Every build and publish copies them next to `Rewind.Register.exe`.

| File | Plays when | Source |
| --- | --- | --- |
| `Scan.wav` | a barcode Rewind knows is read | |
| `Chime.wav` | a sale is complete | |
| `Error.wav` | an error or warning dialog opens, an unknown barcode or a factory UPC at checkout is scanned, or an overdue alert pops up | |
| `Drop.wav` | a DVD drops into the return box | |
| `Tick.wav` | the Can't Decide spinner moves on one title | |

Use plain PCM WAV files. A missing or broken file only logs a warning, and Rewind keeps running without that sound (GEN-7).
