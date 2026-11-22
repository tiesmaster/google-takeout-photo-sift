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

### Videos

Videos are reviewed between the photos, by date, but not played here: you see the still frame
Windows makes for Explorer, with a ▶ and a bar with the length, frame size and file size (a card with
the same when Windows has no still). The strip shows the stills too. `p` plays the video in your
default player. `d` and `z` work as for photos (album copies and
sidecar included); rotating doesn't. A Live Photo's video (`IMG_1.MOV` next to `IMG_1.JPG`) isn't
reviewed on its own: it goes along when its photo is deleted.

### WhatsApp photos

Photos received through WhatsApp come last, in a "WhatsApp" section after the loose photos, so the
batch's own photos come first. WhatsApp strips the camera data and shrinks the picture, and on an
iPhone a saved one is just `IMG_1234.JPG`, so they're recognised by their traits: no camera make, no
EXIF date, and a long side of at most 1600 px. Videos count too: Google Photos re-encoded them and
dropped their camera data, so a video with a long side of at most 640 px (WhatsApp sent 640 × 360,
cameras film 720p or more) is taken as WhatsApp. A WhatsApp photo that is in an album stays in that
album. Every WhatsApp photo gets a green frame, on screen and in the strip, and a "WhatsApp" label
next to the counter. `g w` jumps to the section, and `W` puts them back between the loose photos by
date (still marked).

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
| **Albums** | `Shift+→` `J` `]` | First photo of the next album (the loose and WhatsApp photos count as the last albums) | yes |
|  | `Shift+←` `K` `[` | First photo of the previous album | yes |
|  | `g e` | Last photo of this album |  |
|  | `g l` | First loose photo (in no album) |  |
|  | `g w` | First WhatsApp photo (the loose ones, grouped last) |  |
|  | `W` | WhatsApp photos grouped last, or back between the loose photos by date |  |
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
|  | `p` | Open in its default app: plays a video |  |
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

## Removing -edited copies

Google Photos, Picasa and the iPhone saved their edits next to the original: `IMG_1372.JPG` and
`IMG_1372-edited.JPG`. Most are an automatic enhance, a few percent brighter and saved again at lower
quality, so the original is the one to keep. `drop-edited` compares each pair as shown, shrunk to
32×32: a copy with the same shape and a mean difference under 25/255 goes. A cropped or turned copy, or
one that clearly looks different (a filter or effect), is listed and left for you to judge. So is an
edited copy in an album its original isn't in.

```powershell
dotnet run --project src/GoogleTakeoutPhotoSift.Cli -- drop-edited 2014            # dry run
dotnet run --project src/GoogleTakeoutPhotoSift.Cli -- drop-edited 2014 --apply    # close Photo Sift first
dotnet run --project src/GoogleTakeoutPhotoSift.Cli -- undo-drop-edited 2014       # puts the last run back
```

The original keeps the sidecar it shares with the edited copy.

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
