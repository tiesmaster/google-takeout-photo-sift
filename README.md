# Google Takeout Photo Sift

A fast Windows viewer for reviewing a Google Photos Takeout one batch (year) at a time before it is
uploaded to [Immich](https://immich.app) with [immich-go](https://github.com/simulot/immich-go):
step through every photo once, fix the rotation and move out the ones you don't want.

> Work in progress. The design and milestones are in [PLAN.md](PLAN.md).

## Safety

- Photos are never deleted: a delete moves the photo, its album copies and their sidecars to
  `batches\deleted\<batch>\`, in the same folder structure, and can be undone.
- Photos are never re-encoded: rotating only changes the EXIF Orientation tag, through ExifTool, and
  keeps the file's modified date. Album copies get the same change, so they stay byte-identical and
  immich-go still pairs them.

## Requirements

- Windows 11
- [.NET 11 SDK](https://dotnet.microsoft.com/download) (to build): `winget install Microsoft.DotNet.SDK.11`
- [ExifTool](https://exiftool.org) on `PATH` (for rotating)

HEIC photos are decoded with Magick.NET, so no extra Windows codecs are needed. A HEIC takes about a
second to decode (prefetching usually hides that); JPEGs take well under 100 ms.

## Build and run

```powershell
dotnet build
dotnet test
dotnet run   --project src/GoogleTakeoutPhotoSift.App -- 2016
dotnet watch --project src/GoogleTakeoutPhotoSift.App -- 2016
```

A year is looked up in `C:\ties\takeout\batches\<year>`, then `batches\done\<year>`; a folder path
works too. Options: `--start N` (photo number), `--order albums|python`, `--timing` (show the timing
overlay), `--batches-root PATH`, `--bench` (see below), `--keys "r d z"` (play keys and quit, for
scripted checks). The app reopens a batch where you left it.

## Review order

Albums come first, ordered by the date of their first photo, then the photos that are in no album
("loose photos"). A photo in several albums is reviewed once, in the first of them, and the header
lists the others ("also in"). Within an album, photos are ordered by date taken. The header shows
`Album 3/23 · Vakantie IJsland [2016] · 12/120`, and the strip below the photo shows its neighbours
with album boundaries marked. `--order python` gives the old viewer's order instead.

## Keys

`?` shows all of them in the app. `g` starts a two-key sequence: a hint shows what can follow.
Delete is `d`; holding it deletes one photo, and the `Delete` key only explains that.
Every delete and undo is logged in `batches\deleted\<batch>\sift-journal.jsonl`, so undo also works
after a restart.

<!-- shortcuts -->
| Group | Keys | Action | Held key repeats |
| --- | --- | --- | --- |
| **Navigate** | `→` `j` `Space` `PgDn` | Next photo | yes |
|  | `←` `k` `Backspace` `PgUp` | Previous photo | yes |
|  | `Home` `g g` | First photo of the batch |  |
|  | `End` `G` | Last photo of the batch |  |
|  | `:` `Ctrl+G` | Go to photo number |  |
| **Albums** | `Shift+→` `J` `]` | First photo of the next album (the loose photos count as the last album) | yes |
|  | `Shift+←` `K` `[` | Start of this album; at its start, the previous album | yes |
|  | `g e` | Last photo of this album |  |
|  | `g l` | First loose photo (in no album) |  |
|  | `g a` | Album picker: type to filter, Up/Down, Enter to jump |  |
| **Edit** | `r` | Rotate right 90° |  |
|  | `l` | Rotate left 90° |  |
|  | `R` | Rotate 180° |  |
|  | `d` | Delete the photo and all its copies (to batches\deleted) |  |
|  | `Delete` `Shift+Delete` | Shows that delete is d |  |
|  | `z` `Ctrl+Z` | Undo the last delete |  |
| **View** | `s` | Show or hide the album list |  |
|  | `t` | Show or hide the thumbnail strip |  |
|  | `T` | More or fewer thumbnails: 5, 8 or 3 each side |  |
|  | `f` `F11` `Enter` | Full screen |  |
|  | `F12` | Timing overlay |  |
|  | `i` | Info panel: dates, camera, copies, albums, sidecar |  |
| **Other** | `o` | Show the file in Explorer |  |
|  | `y` | Copy the file path |  |
|  | `?` | This help |  |
|  | `Esc` | Close overlay, leave full screen, else quit (after pending writes) |  |
<!-- /shortcuts -->

## Removing twins

Some photos are in a batch twice under different names: the camera original (`IMG_0576.JPG`, stored
landscape with an EXIF rotation) and a copy some app turned upright and saved again
(`2013-09-13 21.05.57.jpg`). The command-line tool finds them: same taken time to the second, one with
orientation 3/6/8 and the other upright with matching size, in the same albums, and the same picture
by fingerprint. It removes the re-encoded copy:

```powershell
dotnet run --project src/GoogleTakeoutPhotoSift.Cli -- dedupe 2013            # dry run: lists what it would do
dotnet run --project src/GoogleTakeoutPhotoSift.Cli -- dedupe 2013 --apply    # close Photo Sift first
dotnet run --project src/GoogleTakeoutPhotoSift.Cli -- undo-dedupe 2013       # puts the last run back
```

Removed twins go to `batches\deleted` like any delete, with their album copies and sidecars, and
are journalled, so `z` in the viewer can also bring them back one by one.

## Measuring speed

`F12` shows the time from key press to photo on screen, with p50/p95 for cached and uncached photos.
Every navigation is also logged to `%LOCALAPPDATA%\GoogleTakeoutPhotoSift\timing.csv`.

`--bench` runs a fixed script (single steps, a held key, release, steps back, far jumps) through the
normal navigation code, saves snapshots next to `timing.csv`, and quits:

```powershell
dotnet run --project src/GoogleTakeoutPhotoSift.App -c Release -- 2013 --start 1 --bench
```

## Settings

Optional, in `%LOCALAPPDATA%\GoogleTakeoutPhotoSift\settings.json` (comments allowed):

```jsonc
{
  "batchesRoot": "C:\\ties\\takeout\\batches",
  "prefetchAhead": 4,      // sharp frames decoded ahead
  "prefetchBehind": 2,     // and kept behind
  "cacheMegabytes": 512,   // decoded frames
  "fileCacheMegabytes": 150,
  "showTiming": false,
  "showStrip": true,
  "stripPerSide": 5        // thumbnails each side of the current photo
}
```

## Batch layout

```
batches\<year>\Takeout\Google Photos\
    Photos from <year>\      every photo of that year + a .json sidecar per photo
    <Album name>\            copies of year photos, album-only photos, metadata.json
batches\done\<year>\         batches already uploaded
batches\deleted\<year>\      photos deleted in the viewer (same layout)
```

## Licence

[MIT](LICENSE)
