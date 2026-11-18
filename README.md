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

HEIC photos are decoded with Magick.NET when Windows has no HEVC codec, so no extra codecs are needed.

## Build and run

```powershell
dotnet build
dotnet test
dotnet run   --project src/GoogleTakeoutPhotoSift.App -- 2016
dotnet watch --project src/GoogleTakeoutPhotoSift.App -- 2016
```

A year is looked up in `C:\ties\takeout\batches\<year>`, then `batches\done\<year>`; a folder path
works too. Options: `--start N` (photo number), `--timing` (show the timing overlay),
`--batches-root PATH`, `--bench` (see below). The app reopens a batch where you left it.

## Keys (so far)

| Keys | Action |
| --- | --- |
| `→` `j` `Space` `PageDown` | next photo |
| `←` `k` `Backspace` `PageUp` | previous photo |
| `Home` / `End` `G` | first / last photo |
| `f` `F11` `Enter` | full screen |
| `F12` | timing overlay |
| `Esc` | leave full screen, else quit |

Rotate, delete, albums and the rest follow in the next milestones (see [PLAN.md](PLAN.md) §4).

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
  "showTiming": false
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
