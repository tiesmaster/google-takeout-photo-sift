# Google Takeout Photo Sift — plan

A fast Windows viewer for reviewing one Google Photos Takeout batch (one year) before it goes to Immich
with immich-go. It replaces `C:\ties\takeout\scripts\rotate_viewer.py`, keeps that viewer's rules for
grouping, sidecars, rotating and deleting, and adds album-first review, a thumbnail strip, faster
navigation and Gmail/GitHub-style shortcuts.

Stack (decided): C# / .NET 11 (the latest release), WPF, Windows 11 only, WIC for decoding, ExifTool `-stay_open` for writing.

---

## 0. What I found on this machine

| | Status |
| --- | --- |
| .NET SDK | 8.0.400 is installed, but the plan targets **.NET 11**: `winget install Microsoft.DotNet.SDK.11` (11.0.100 in winget today). It's an STS release, supported 24 months, so until about November 2028, the same end date as .NET 10 LTS (`Microsoft.DotNet.SDK.10`, 10.0.404). If an 11 problem shows up, going back to 10 means changing two lines (`global.json`, `Directory.Build.props`). |
| ExifTool | 13.59 on `PATH` (`%LOCALAPPDATA%\Programs\ExifTool`) |
| HEIF Image Extensions | installed (1.2.65) |
| HEVC Video Extensions | not installed, and not needed: HEIC is decoded with **Magick.NET** (§2.2). WIC alone can't decode HEIC without this paid Store codec (€0.99, `9NMZLZ57R3T7`); if it's ever installed, WIC is tried first and is faster. |
| WebP / Raw extensions | installed (WebP covers the occasional `.webp`) |
| Display | a 3840×2160 monitor. "Screen size" here means up to 8 MP per decoded frame, which matters for the timing budget (§2.2) |

Batch facts used below, from `batches\2013`: 772 files in `Photos from 2013`, 8 albums with photos plus
2 albums with **no** media in this batch (only `metadata.json`). There are 2 album-only photos and no
photo in more than one album. The album title in `metadata.json` can differ from the folder name
(`DVR_ Drakenbootfestival [2013]` vs `"DVR: Drakenbootfestival [2013]"`), so the title is shown from
`metadata.json`. Albums from an earlier year turn up too (`Vakantie England [2012]` in the 2013 batch).

---

## 1. Repository and project layout

Repository: [tiesmaster/google-takeout-photo-sift](https://github.com/tiesmaster/google-takeout-photo-sift),
cloned to `C:\ties\src\google-takeout-photo-sift`.

Naming: the app is called Google Takeout Photo Sift ("Photo Sift" for short). Projects use the
CamelCase form of the repo name, `GoogleTakeoutPhotoSift.*`, and the exe is
`GoogleTakeoutPhotoSift.exe`. Namespaces get a `Tiesmaster.` prefix
(`Tiesmaster.GoogleTakeoutPhotoSift.Core.Index`), set once in `Directory.Build.props` as
`<RootNamespace>Tiesmaster.$(MSBuildProjectName)</RootNamespace>`.

Commits: no `Co-Authored-By` or "Generated with Claude Code" lines in commit messages or PR
descriptions. This is set in `~/.claude/settings.json` (`"attribution": {"commit": "", "pr": "",
"sessionUrl": false}`) and repeated in `CLAUDE.md` for sessions on other machines.

```
google-takeout-photo-sift/
├─ GoogleTakeoutPhotoSift.slnx     the XML solution format, the default since the .NET 10 SDK
├─ global.json                    SDK 11.0.100, rollForward: latestFeature; test runner: Microsoft.Testing.Platform
├─ Directory.Build.props          net11.0, RootNamespace Tiesmaster.$(MSBuildProjectName), Nullable, ImplicitUsings,
│                                 TreatWarningsAsErrors in Release only (so dotnet watch keeps going)
├─ Directory.Packages.props       central NuGet package versions
├─ .editorconfig
├─ .gitattributes                 * text=auto eol=lf, *.sln/*.cmd/*.ps1 eol=crlf, binary fixtures
├─ CLAUDE.md                      conventions for Claude Code sessions (naming, commits, build/test)
├─ .gitignore                     `dotnet new gitignore` + /local/ (golden data, see §3.3)
├─ LICENSE                        MIT
├─ README.md
├─ .github/workflows/build.yml
├─ src/
│  ├─ GoogleTakeoutPhotoSift.Core/ net11.0, no WPF reference: everything that a stack switch should keep
│  │  ├─ Batches/   BatchLocator, SidecarMatcher, Sidecar (JSON reading), MediaTypes
│  │  ├─ Index/     BatchIndex, Photo, MediaFile, Album, Section, ReviewOrder, Grouping
│  │  ├─ Files/     FileOpQueue, DeleteOperation, UndoStack, DeletedTree
│  │  ├─ Exif/      ExifToolProcess, Orientation (math), OrientationReader, PhotoInfo
│  │  ├─ Input/     KeyChord, Binding, Bindings (THE table), KeyDispatcher, CommandId
│  │  └─ Settings/  SiftSettings, ResumeState
│  └─ GoogleTakeoutPhotoSift.App/  net11.0-windows, UseWPF, WinExe
│     ├─ App.xaml, Program/CommandLine
│     ├─ Imaging/   IImageDecoder, WicDecoder, MagickDecoder, DecodeScheduler, FrameCache, ThumbnailCache, FileBytesCache
│     ├─ Views/     MainWindow, PhotoView, ThumbnailStrip, HeaderOverlay, HelpOverlay,
│     │             AlbumPicker, GoToBox, InfoPanel, PendingKeyHint, TimingOverlay
│     ├─ Input/     WpfKeyTranslator (KeyDown/TextInput → KeyChord)
│     └─ Diagnostics/ TimingLog
└─ tests/
   ├─ GoogleTakeoutPhotoSift.Core.Tests/  xUnit; FakeBatch builder; fixtures/ (tiny JPEG/HEIC/PNG files)
   └─ GoogleTakeoutPhotoSift.App.Tests/   (later, optional) decoder smoke tests on the fixtures, STA thread
```

Why the input dispatcher lives in Core: the binding table, sequences, repeat rules and the help text are
pure logic. With a neutral `KeyChord` ("j", "Shift+Right", "g a") they're testable without WPF, and a
stack switch keeps them. The App only translates WPF key events into chords.

**Dependencies** (few on purpose):
- Core: `MetadataExtractor` (Apache-2.0) to read EXIF Orientation, DateTimeOriginal, camera, the embedded
  thumbnail offsets, also from HEIC. JSON with `System.Text.Json`.
- Tests: `xunit.v3` only, running on Microsoft.Testing.Platform (opted in through `global.json`; the
  .NET 10+ SDK no longer runs xUnit v3 through VSTest, so `Microsoft.NET.Test.Sdk` and
  `xunit.runner.visualstudio` aren't needed). Plain `Assert` (I'm skipping
  FluentAssertions because v8 changed to a commercial licence).
- App: `Magick.NET-Q8-x64` (Apache-2.0; bundles ImageMagick with libheif and libde265, LGPL) for HEIC
  decoding (§2.2). It adds about 30 MB of native DLLs to the output. If WPF's `BitmapImage` scaling turns
  out too slow, go to WIC directly through `Vortice.WIC` or CsWin32 (§2.2).

**Build and run**

```powershell
dotnet build
dotnet test                                   # ExifTool tests skip themselves if exiftool isn't on PATH
dotnet run   --project src/GoogleTakeoutPhotoSift.App -- 2016
dotnet run   --project src/GoogleTakeoutPhotoSift.App -- C:\ties\takeout\batches\done\2012 --start 120
dotnet watch --project src/GoogleTakeoutPhotoSift.App -- 2016
dotnet publish src/GoogleTakeoutPhotoSift.App -c Release -r win-x64 --self-contained false -p:PublishSingleFile=true
```

`dotnet watch` and WPF: C# edits inside method bodies hot-reload. XAML edits, and "rude" edits (new
fields, changed signatures), make `dotnet watch` restart the app. XAML Hot Reload in the strict sense is
a Visual Studio feature. To make restarts painless, the app remembers the last position per batch
(`%LOCALAPPDATA%\GoogleTakeoutPhotoSift\state.json`) and opens there unless `--start` is given. The same feature
lets you resume a review on another day.

**Command line**: `GoogleTakeoutPhotoSift <year | batch path> [--start N] [--order name|python] [--timing]`.
A year is looked up as `batches\<year>`, then `batches\done\<year>` (as `find_batch` does). The batches
root defaults to `C:\ties\takeout\batches` and can be set in `settings.json`.

**Settings** (`%LOCALAPPDATA%\GoogleTakeoutPhotoSift\settings.json`, all optional): batches root, exiftool path,
strip thumbnails per side (default 5), cache budget in MB (default 512), prefetch ahead/behind (4/2),
album order, timing overlay on/off.

**README**: what it is, the review workflow (split_year → sift → immich-go), requirements (Windows 11,
.NET 11 Desktop Runtime, ExifTool on PATH; HEIC works without extra codecs), install/build, the shortcut table (generated
from the binding table by a small test, so it can't go stale; see §3.4), safety promises ("never deletes,
never re-encodes"), and the batch layout.

**CI** (`.github/workflows/build.yml`): `windows-latest`, `actions/setup-dotnet@v5` with `global-json-file: global.json`,
`dotnet build -c Release`, `dotnet test`. ExifTool can be added with `choco install exiftool -y` so the
rotation tests also run in CI. Optionally a second job on a `v*` tag runs `dotnet publish` and attaches
the single-file exe to a GitHub Release.

**Licence**: MIT. ExifTool isn't bundled (it has its own Perl licence); the README tells users to
install it.

---

## 2. Architecture

```
            ┌──────────── GoogleTakeoutPhotoSift.Core ────────────┐
 batch dir ─► BatchLocator ─► Scanner + SidecarMatcher ─► Grouping ─► BatchIndex ─► ReviewOrder
            │                                                         (photos, copies, albums, sections)
            │  KeyDispatcher ◄─ Bindings table          FileOpQueue ─► ExifToolProcess (stay_open)
            └─────────────────────────────────────────────────────────▲───────────────────────┘
                     ▲ KeyChord                              rotate / delete / undo jobs
 ┌───────── GoogleTakeoutPhotoSift.App (WPF) ─────────┐
 │ WpfKeyTranslator → dispatcher → ViewerController ─┼─► DecodeScheduler (N workers) ─► FrameCache
 │ MainWindow: PhotoView, ThumbnailStrip, overlays ◄─┼──────────── frozen BitmapSources ◄──┘
 └───────────────────────────────────────────────────┘
```

### 2.1 The index: photos, copies, albums and the review order

**Model** (immutable after the scan, except for per-photo state):

```csharp
record MediaFile(string Path, string Folder, string? SidecarPath, long Size, DateTimeOffset? TakenJson);
sealed class Photo {                     // one logical photo
    MediaFile Primary;                   // year-folder file, or the file in the photo's home album
    IReadOnlyList<MediaFile> Copies;     // album copies; every action applies to Primary + Copies
    IReadOnlyList<Album> Albums;         // all albums it's in, home album first
    Album? Home;                         // null → loose
    DateTimeOffset? Taken;               // sort key (see below)
    // mutable state: Orientation? (read lazily, then owned by us), Deleted flag
}
sealed class Album { string Folder; string Title; DateTimeOffset? FirstTaken; List<Photo> Members; }
sealed class Section { Album? Album; List<Photo> Photos; }   // Album == null → "Loose photos"
```

**Scan** (a port of `collect`):
1. Folders under `<batch>\Takeout\Google Photos`. Year folders match `^Photos from (\d{4})$`; all
   others are albums. An album's title comes from its `metadata.json` `title`, falling back to the
   folder name.
2. Per folder, list the names and run `SidecarMatcher` (an exact port of `match_sidecars`,
   `parse_json_name`, `media_keys`, including the `-edited`/`-bewerkt` suffixes). This yields
   `sidecars[media] = json` for **all** media, videos included, and `users[json] = {media…}`. Videos
   stay in `users` because a sidecar they share must be copied, not moved, as in Python.
3. Image extensions: `.jpg .jpeg .png .tif .tiff .webp .heic .heif`. Everything else is skipped
   (videos, `.gif` the same as now).
4. Year-folder photos: one `Photo` each, keyed in `byName[name.casefold]` (first wins when a batch has
   several year folders).
5. Album files, folders in ordinal order: when `byName` has a photo with the same name (ignoring case),
   it's a copy if the JSON `photoTakenTime` is equal and non-null, **or** the sizes are equal and the
   bytes identical (size check first, then a streaming compare). Otherwise it's a separate photo.
   **New:** album-only photos are keyed in `byName` too, so the same shared-album photo in two albums
   becomes one photo with a copy instead of two items (decision 3). The Python viewer would
   show it twice, which breaks requirement 2.
6. Take date: JSON `photoTakenTime`. If there is no JSON, EXIF `DateTimeOriginal` (read only for those
   few files). If neither exists, the photo sorts last in its section.

This is about 3,500 `stat`s plus a few JSON reads per batch. JSON parsing runs in parallel. The index
should be ready in well under a second; the byte compares only run for same-name files with a missing
or different date.

**Review order** (`ReviewOrder`, a pure function of the index and the settings):
1. Album order. **Proposed: by the date of the album's first photo** (`FirstTaken`, the earliest
   taken date among all its files in this batch), title as tie-breaker. Reviewing a year then reads as
   a timeline, and "Vakantie England [2012]" sorts in where its 2013 photos are. Alternative: by title
   (`StringComparer.CurrentCultureIgnoreCase`). Configurable.
2. Home album: a photo in several albums is reviewed in the **first album in that order**; the others
   list it as "also in". After assignment, an album can end up with no photos of its own. It gets no
   section but stays visible, greyed, in the album picker ("4 photos, shown in Weekendje Barcelona").
3. Within a section: by `Taken`, then file name (ordinal, ignoring case, like the Python casefold), then
   folder.
4. Loose photos last: every photo with no album, across all year folders, sorted the same way.
5. The flat review list is the concatenation. An `--order python` mode reproduces the old order (year
   folders by name, then album-only), so in M1 photo numbers match the Python viewer side by side.

**Deletes don't change the list.** A photo gets `Deleted = true` and navigation skips it. Undo clears
the flag, so the photo comes back exactly where it was, with no index bookkeeping (the Python version
stores an index and reinserts). Counters ("1203 / 3311") count non-deleted photos; with about 3,500
entries a recount after each delete or undo takes microseconds. Album numbering stays stable during a
session (decision 4).

**Positions for the header**: per photo, precomputed `(sectionIndex, indexInSection)`, recounted
after deletes. The header formats as:

- album: `Album 3/23 · Vakantie IJsland [2016] · 12/120`
- loose: `Loose photos · 412/1550`
- overall, top-left: `1203 / 3324`

### 2.2 Decode and cache pipeline

**Goal** (decision 13): under 50 ms from key to new frame on screen when cached. When not cached (12 MP
JPEG): a preview under 50 ms and the sharp frame under 200 ms. A held key flies through like IrfanView.

**Budget on this machine**: fitting a 4000×3000 JPEG into 3840×2160 gives 2880×2160 at a scale of 0.72.
That's above ½, so the JPEG decoder's DCT scaling (½, ¼, ⅛) can't help, and the sharp frame is close to a
full decode: an estimated 70–120 ms single-threaded with WIC, plus 5–10 ms to read 12 MB from SSD. So the
uncached target is reachable but tight, and the low-res-first step carries it:

| Stage | How | Expected |
| --- | --- | --- |
| read | whole file into memory, `FileShare.ReadWrite \| FileShare.Delete` | 5–10 ms |
| preview | EXIF thumbnail (160×120 JPEG, about 1 ms) if already in the thumbnail cache; otherwise ⅛ DCT decode (500×375) | 2–15 ms |
| sharp | WIC decode scaled to fit the window in physical pixels | 70–120 ms |
| show cached | swap `Image.Source` to a frozen bitmap, one render pass, texture upload | 5–15 ms |

So the plan measures two numbers: **first pixels** (preview or sharp) and **sharp**.

**Components** (App/Imaging):

- `WicDecoder` — stateless, runs on worker threads:
  - bytes → `MemoryStream` → `BitmapDecoder.Create(..., BitmapCreateOptions.IgnoreColorProfile |
    PreservePixelFormat, BitmapCacheOption.OnLoad)` → frame → `TransformedBitmap`/`BitmapImage` with
    `DecodePixelWidth` **or** `Height` (only the limiting one) → `Freeze()`.
  - `CacheOption.OnLoad` + `Freeze()` is essential. Without it WPF decodes lazily **on the UI thread**
    at first render, which is exactly the current viewer's problem.
  - It never opens files by URI. WPF keeps a URI-loaded file locked, and ExifTool's
    `-overwrite_original` (write temp, rename over) and the delete moves would then fail. Reading
    bytes with `FileShare.Delete` avoids that.
  - The target size uses the window's client area in **physical** pixels (per-monitor DPI v2 manifest,
    `VisualTreeHelper.GetDpi`), with width and height swapped for orientations 5–8. The `Image` gets
    exactly that size in DIPs, `UseLayoutRounding`, and no `Stretch` resampling, so the frame isn't
    scaled a second time.
  - WPF doesn't apply EXIF orientation itself, so the decoder returns stored pixels and `PhotoView`
    applies a `LayoutTransform` (rotate, plus flip for 2/4/5/7). A rotation is then an instant transform
    change that needs no re-decode.
  - **HEIC**: `IImageDecoder` has two implementations. `WicDecoder` handles everything WIC can decode
    and is tried first. When WIC reports a missing codec (`WINCODEC_ERR_COMPONENTNOTFOUND`, which is what
    happens without the HEVC extension), `MagickDecoder` decodes the file with Magick.NET instead, and
    the choice is remembered per extension for the rest of the session. MagickDecoder reads the bytes
    from memory (`MagickImage(byte[], settings)`), scales with `Thumbnail` to the target size (or decodes
    the embedded preview for the strip), converts to BGRA and wraps the buffer with
    `BitmapSource.Create(...).Freeze()`. Software HEVC decoding of a 12 MP HEIC is expected at about
    150–300 ms, so the preview step (embedded thumbnail first) matters more here; the timing log records
    the decoder per photo. The 762 HEIC files across all batches are about 2.5% of the photos.
    Orientation is always read with MetadataExtractor from the file, never from the decoder. libheif
    applies the HEIF `irot`/`imir` transforms while decoding by default, so the decoded pixels may already
    be upright; a fixture test (an iPhone HEIC with `irot` plus EXIF Orientation 6) pins down what
    MagickDecoder returns, and the decoder then compensates so `PhotoView` always gets stored pixels plus
    one orientation to apply (see the HEIC note in §2.4).
  - Plan B if the measured sharp decode is too slow: WIC directly (`IWICBitmapSourceTransform` /
    `IWICBitmapScaler` with `WICBitmapInterpolationModeFant` through Vortice.WIC), still producing a
    `BitmapSource` via `BitmapSource.Create` from the buffer.

- `FileBytesCache` — a small LRU of file contents (about 150 MB) so the preview and the sharp decode of
  the same file read the disk once.

- `FrameCache` — an LRU of sharp frames keyed by `(path, targetSize)`, with a memory budget (default
  512 MB, about 20 frames at 2880×2160×4 bytes). Preview frames go in a separate small LRU. After a resize
  or a full-screen toggle, an entry within 10% of the new size is still used and is re-decoded in the
  background.

- `ThumbnailCache` — strip thumbnails for the whole batch in memory, about 160 px on the long side. The
  source is the embedded EXIF thumbnail (`BitmapFrame.Thumbnail` or the MetadataExtractor offsets →
  tiny JPEG decode), falling back to the ⅛ DCT decode; PNG and HEIC without a thumbnail use a scaled
  decode. 3,500 × about 80 KB is about 280 MB at the worst, so there's an LRU of about 1,000. A disk
  cache can come later if it turns out to be needed.

- `DecodeScheduler` — a priority queue with `max(2, cores/2)` workers (decoding inside WIC is
  single-threaded per frame):

  | Priority | Request |
  | --- | --- |
  | 0 | sharp frame for the current photo |
  | 1 | preview for the current photo (if no preview is cached) |
  | 2 | sharp frames ahead, in the direction of travel (default 4) |
  | 3 | strip thumbnails in view |
  | 4 | sharp frames behind (default 2) |
  | 5 | thumbnails one strip-width beyond the view |

  Every navigation bumps a **generation** number. Queued requests outside the new window are dropped;
  a decode that's already running finishes (WIC can't be interrupted), but its result goes into the
  cache only if it's still in the window, and onto the screen only if it's still the current photo.

**Held key** (`KeyDown.IsRepeat`, about 30/s with the default Windows repeat rate): show whatever is
best for each photo **right now**: a cached sharp frame, else the preview or thumbnail, else keep the
previous frame with the new header. During a hold only cheap requests are scheduled (previews in the
direction of travel), and no sharp decodes that would be stale 33 ms later. On `KeyUp`, the current
photo's sharp decode is queued at priority 0 and the prefetch window is rebuilt around it.

**Threads**

| Thread | Does |
| --- | --- |
| UI (STA) | input, layout, swapping `Image.Source`, overlays. Never decodes or does file I/O. |
| Decode workers ×N | read bytes, WIC decode, freeze, then a `Dispatcher.BeginInvoke` to deliver |
| Index scan | once at startup (parallel JSON reads); the UI shows "indexing…" |
| File-op worker ×1 | ExifTool writes, delete/undo moves, in order (§2.4) |
| ExifTool stdout reader | belongs to the file-op worker's process wrapper |

**Timing** (`F12`, or `--timing`): a small overlay at the bottom right with the last navigation
`key→first pixels`, `key→sharp`, decode ms, read ms, and the cache hit kind (sharp / preview / miss),
plus p50/p95 over the last 100 navigations. The clock starts at the key event's timestamp and stops at
the first `CompositionTarget.Rendering` after the source swap. Every navigation is also appended to
`%LOCALAPPDATA%\GoogleTakeoutPhotoSift\timing.csv` (`time, photo, MP, bytes, cacheKind, firstMs, sharpMs,
decodeMs, readMs`), so the targets can be checked on a whole run through a batch.

### 2.3 UI layout

```
┌──────────────────────────────────────────────────────────────────────────────┐
│1203 / 3324        Album 3/23 · Vakantie IJsland [2016] · 12/120    saved ✓   │  header row (overlaid, shadowed text)
│                   Vakantie IJsland [2016] / IMG_1234.JPG                     │
│                   +1 copy · also in: Reykjavik                               │
│                                                                              │
│                              ┌──────────────┐                    ┌─────────┐ │
│                              │              │                    │ info    │ │  info panel (i), right
│                              │  main photo  │                    │ panel   │ │
│                              │              │                    └─────────┘ │
│                              └──────────────┘                                │
│  g …  a album · l loose · g first                          [timing overlay]  │  pending-key hint / F12 timing
├──────────────────────────────────────────────────────────────────────────────┤
│ ▢ ▢ ▢ ▢ ▢ │ ▢ ▢ [■] ▢ ▢ ▢                                                    │  thumbnail strip (t), 2N+1 cells
│      Weekend X │ Vakantie IJsland [2016]                                     │  album divider + label
└──────────────────────────────────────────────────────────────────────────────┘
```

- `MainWindow`: a black `Grid` with two rows, the photo (`*`) and the strip (`Auto`, collapsible). The
  overlays sit in the same cell above the photo, so they never resize it.
- Header text uses Segoe UI Semibold with a 2 px offset shadow. Draw it twice, as the Python viewer
  does, or use `DropShadowEffect` with `BlurRadius=0`; both are cheap. Top-left: overall counter plus a
  status line ("saving…", "saved", "deleted — z to undo", errors in red). Top-centre: album position,
  then folder/file, then copies and other albums.
- Crossing into another album shows its title as a centred banner for about one second.
- `ThumbnailStrip`: N before and N after (configurable, default 5), with deleted photos left out. The
  current cell is larger with a white border. Thumbnails show their orientation and update right after a
  rotation. A vertical divider marks an album boundary and names the album. Clicking a thumbnail jumps
  there. The mouse wheel on the photo goes next/previous.
- Overlays, each modal for the keyboard while open: **Help** (`?`), **Album picker** (`g a`), **Go to**
  (`:`), and **Info** (`i`, non-modal, follows the current photo).
- Full screen: `WindowStyle=None` + `Maximized`, with a remembered normal bounds. Sizes are taken from
  the monitor the window is on.
- Quit (Esc, or closing the window): if file ops are pending, show "saving 3 changes…" and close when
  the queue is empty. ExifTool gets `-stay_open False` and the app waits for it to exit.

### 2.4 The background file-op worker (ExifTool + moves)

One queue, one worker thread, for **every** change to the disk: rotations, deletes and undos. The order
is then automatic. A delete queued after a rotation of the same photo can't move files before the write
finishes (the Python `jobs.join()`), and the UI never waits.

**ExifToolProcess** (Core):
- `exiftool -stay_open True -@ -`, with `CreateNoWindow`, `StandardInputEncoding = new UTF8Encoding(false)`
  (no BOM, or ExifTool reads the first argument wrongly), stdout/stderr read as UTF-8.
- Every command: `-charset filename=utf8`, the arguments, then `-execute<id>`. The reader collects lines
  up to `{ready<id>}`, so answers can't be mixed up. Paths with `[ ] & é` and spaces are covered by tests.
- Success: a line `N image files updated` with N equal to the number of paths, and no `Error`/`weren't
  updated` (the Python check, made stricter).
- Rotate command: `-Orientation#=<n> -P -overwrite_original <primary> <copies…>`.

**Rotate flow**:
1. On the UI: read the orientation. It's cached per photo; the first read is lazy via MetadataExtractor,
   on a worker, before the photo is first shown. Mirrored 2/4/5/7 → status "mirrored orientation (5),
   not rotated". Otherwise compute the new value with the same table as Python
   (`{1:0, 6:90, 3:180, 8:270}`), set it on the photo, update the transform and the thumbnail at once, and
   enqueue `Rotate(photo, n)`.
2. Coalescing: if a rotate for the same photo is still waiting in the queue, it's replaced (`r r r`
   becomes one write of the final value).
3. Worker: run ExifTool, then report `saved` or the error to the UI thread. On an error, the photo's
   orientation is read back from disk and the screen corrects itself.

**Delete flow** (port of `delete`/`move`/`revert`):
1. On the UI: mark the photo `Deleted`, move to the next photo, show "deleted — z to undo". Push an undo
   entry and enqueue `Delete(photo)`.
2. Worker, for the primary and each copy: move the media to
   `batches\deleted\<batch name>\Takeout\Google Photos\<same relative path>`. Then its sidecar: remove
   this media from `users[json]`. If other users remain (an `-edited` copy or a video), **copy**,
   otherwise **move**. If the destination exists: identical bytes → for a move, `dedup` (delete the
   source, record it), for a copy, `none`; different bytes → fail. `File.Move` and `File.Copy` keep the
   modified time, matching `shutil.move`/`copy2`.
3. If any step fails, revert the steps done so far in reverse, restore `users`, clear the `Deleted`
   flag, and show "delete failed: …" on the photo.
4. Records are `(kind: move|copy|dedup|none, src, dst)`, the same as Python.
5. **Live Photo video** (decision 10): a video next to the primary or a copy with the same stem
   (`IMG_1234.HEIC` ↔ `IMG_1234.MOV`/`.MP4`, case-insensitive) is moved along with its sidecar, by the
   same rules, so it comes back on undo. The index records these pairs during the scan, and the info
   panel shows them.

**Undo** (`z`/`Ctrl+Z`): pop the last undo entry, clear `Deleted` and jump to the photo at once, then
enqueue `Undo(records)`. The worker reverts in reverse order: `copy` → delete dst, `dedup` → copy dst
back to src, `move` → move back, restoring `users`. If the undo fails, the photo is marked deleted again
and an error is shown.

**Journal** (decision 8): append each completed delete and undo, with its records, to
`batches\deleted\<batch>\sift-journal.jsonl`. That gives an audit trail, and at startup the undo stack
is rebuilt from the deletes that weren't undone, so `z` works across restarts.

**HEIC note.** The Python viewer read `original_orientation` because pillow-heif hides it. In .NET,
MetadataExtractor reads the EXIF Orientation straight from the HEIC's Exif item. HEIF also has its own
container rotation (`irot`/`imir`), which libheif, Windows Photos and possibly Immich apply **instead
of** EXIF. So M2 includes a check with a real iPhone HEIC from a batch: rotate it in Photo Sift, then look
at it in Immich (after an upload to a test album) and in Photo Sift itself after a restart. If the EXIF tag is ignored there,
HEIC rotation needs a different approach (decision 9). Until that's settled, rotating a HEIC shows a
warning.

### 2.5 Keyboard dispatcher with sequences

**Input translation** (App): `KeyDown` handles the non-character keys (arrows, Home/End, PageUp/Down,
Delete, Backspace, Enter, Esc, F-keys) and Ctrl/Alt chords. `TextInput` handles printable characters
(`j`, `J`, `?`, `d`, `:`, `[`), which are layout-correct whatever your keyboard layout is. `TextInput`
has no repeat flag, so the translator remembers `IsRepeat` from the preceding `KeyDown` and passes it
along. Each event becomes `KeyChord("J", repeat: true)` and goes to the dispatcher. When a `KeyDown`
produced a chord it's marked handled, so WPF doesn't also act on it (focus moves with arrows, Alt menu
activation).

Avoid **dead keys** under US-International (`'`, `"`, `` ` ``, `~`, `^`). They swallow the next key,
so none of them is bound.

**Binding table** (Core/Input/Bindings.cs), the only place where keys are defined:

```csharp
public static readonly Binding[] All =
[
    new(Cmd.Next,        ["Right", "j", "Space", "PageDown"], Group.Navigate, "Next photo",          Repeat.Allow),
    new(Cmd.Previous,    ["Left", "k", "Backspace", "PageUp"],  Group.Navigate, "Previous photo",      Repeat.Allow),
    new(Cmd.NextAlbum,   ["Shift+Right", "J", "]"],             Group.Albums,   "First photo of next album", Repeat.Allow),
    // ...
    new(Cmd.Delete,      ["d"],                                  Group.Edit,     "Delete photo and all copies", Repeat.Ignore),
    new(Cmd.AlbumPicker, ["g a"],                               Group.Albums,   "Album picker",        Repeat.Ignore),
];
```

The help overlay and the README table are generated from this table.

**Dispatcher state machine**:
- `Idle`: a chord that's a whole binding executes. A chord that's the first key of a sequence (`g`)
  goes to `Pending("g")`, and the hint appears: `g …  g first · a albums · l loose`.
- `Pending`: the next chord completes a known sequence → execute. An unknown key → cancel, with a brief
  hint "g x — no shortcut", and **x is not executed** (as on GitHub, so `g` then `r` doesn't rotate). Esc
  cancels. After 1.5 s with no key it times out.
- **Repeat rules**: `Repeat.Allow` for navigation. `Repeat.Ignore` for everything else, deletes and
  toggles above all; while `IsRepeat` is set those chords do nothing, and repeats never start or
  complete a sequence. So holding `d` deletes exactly one photo.
- **Modal overlays** get the chords first (album picker filter text, go-to digits, Esc to close) and
  only pass through what they don't handle.
- **Table validation** (unit test): no chord bound twice; no single-key binding that's also a sequence
  prefix (`g` alone is never bound); every `Cmd` has at least one key and a help text; no dead keys.

---

## 3. Carrying over the existing rules, and the test plan

### 3.1 Rule by rule

| Python | C# | Notes |
| --- | --- | --- |
| `match_sidecars`, `parse_json_name`, `media_keys`, `EDITED_SUFFIXES`, `DUP_RE` | `SidecarMatcher` | literal port, with `casefold` → `ToLowerInvariant` plus `StringComparison.OrdinalIgnoreCase`; the golden test checks they agree (§3.3) |
| `taken_json_time` | `Sidecar.TakenTime` | `photoTakenTime.timestamp` as a string or number, ≤0 → null, unreadable → null |
| `find_batch` | `BatchLocator` | path → `batches\<arg>` → `batches\done\<arg>`; with a clear error |
| `collect` grouping | `Grouping` | same rule; new: album-only photos grouped across albums (decision 3) |
| `read_orientation` | `OrientationReader` | MetadataExtractor; HEIC through its Exif item, see §2.4 |
| `CW_DEGREES`/`FROM_DEGREES` | `Orientation.Rotate(o, ±90/180)` | mirrored → null |
| `ExifTool` class | `ExifToolProcess` | `-execute<id>`/`{ready<id>}`, UTF-8 without BOM |
| `writer`/`jobs.join()` | `FileOpQueue` | one ordered queue for rotate, delete and undo |
| `delete`/`move`/`revert` | `DeleteOperation` | same kinds: move, copy, dedup, none |
| `undo_delete` | `UndoStack` + `Deleted` flag | the photo comes back in its own place |
| `quit` waits | `MainWindow.Closing` | waits for the queue, then closes ExifTool |
| counter/name with shadow, F11 | `HeaderOverlay`, `ToggleFullScreen` | plus the album line |

### 3.2 Tests (xUnit, Core.Tests)

**FakeBatch builder**: creates a real batch tree in a temp folder and deletes it afterwards, like the
small fake batches the Python viewer was tested with:

```csharp
using var b = new FakeBatch("2016")
    .Year("IMG_1.jpg", taken: T(1))                  // + IMG_1.jpg.supplemental-metadata.json
    .Year("IMG_2.jpg", taken: T(2), json: "IMG_2.jpg.supplemental-me.json")
    .Year("IMG_2-edited.jpg", sharesSidecarWith: "IMG_2.jpg")
    .Album("Vakantie IJsland [2016]", title: "Vakantie IJsland [2016]")
        .Copy("IMG_1.jpg")                           // identical bytes + same date
        .File("IMG_9.jpg", taken: T(9));             // album-only
```

File bytes come from tiny fixture images in `tests/fixtures` (8×8 JPEG with EXIF and an orientation, a
PNG, a HEIC sample, made once with ExifTool and checked in). "Same name, different photo" uses a second
fixture.

**Sidecar matching**
- plain `IMG.jpg` ↔ `IMG.jpg.supplemental-metadata.json`; old style `IMG.jpg.json`
- cut-off names: `…supplemental-me.json`, `…supp.json`, `….s.json`, and cut-off media prefixes on long names
- numbering: `IMG(1).jpg` ↔ `IMG.jpg.supplemental-metadata(1).json`; `IMG.jpg` doesn't take the `(1)` one
- `IMG-edited.jpg` and `IMG-bewerkt.jpg` share `IMG.jpg…json`; `IMG-edited(1).jpg`
- longest prefix wins; case-insensitive; `metadata.json` never counts as a sidecar; no sidecar → null

**Grouping and order**
- same name + same date → copy; same name, no JSON, identical bytes → copy; same name, different date
  and different bytes → two photos; same name, different case → copy
- album-only photo in two albums → one photo, home = the first album; a year photo in two albums →
  home = the first album in order, `Albums` lists both
- an album whose photos all live elsewhere → no section, still in the picker; an album without media
  (only `metadata.json`) → ignored
- videos skipped, but a sidecar shared with a video counts as "in use"
- album order by first date, tie by title; within a section by date then name; no date → last
- `--order python` gives exactly the Python order (also covered by the golden test)

**Delete / undo**
- delete moves the primary, all copies and their sidecars to the mirrored deleted tree, keeping mtimes
- shared sidecar: delete the original while `-edited` remains → JSON **copied**; then delete `-edited`
  → JSON exists and is identical → **dedup**, the source is gone. Undo both in reverse: everything back,
  and the deleted tree is empty again. The same in the other order (`-edited` first).
- destination exists with different bytes → the delete fails, the steps already done are reverted, and
  nothing changed on disk (a hash of the whole tree before and after)
- three deletes then three undos → the tree is byte-identical to the start
- a delete queued after a rotate of the same photo moves the **rotated** file (queue order)

**Rotate** (`[Trait("Needs", "exiftool")]`, skipped when ExifTool isn't found)
- orientation math: right/left/180 from 1/3/6/8; mirrored → refused
- after rotating a photo with two copies: Orientation is the new value in all three, **SHA-256 identical
  across all copies**, mtime unchanged, image data unchanged (hashes after `exiftool -all=` on temp copies
  match the original's)
- `r r r` coalesces into one write with the final value
- Unicode and bracket paths (`Café & co [2016]\IMG 1.jpg`)
- an ExifTool error (read-only file) → reported, the on-disk orientation re-read

**Dispatcher**: every binding fires its command; `g a`, `g g`, `g l`; the 1.5 s timeout (injected
clock); unknown second key cancels without running it; `d` with `repeat: true` does nothing; `j`
with repeat navigates; an overlay captures input; the table validation (§2.5).

### 3.3 Golden tests against the Python viewer

`tools/export_golden.py` (in the repo; it imports `split_year` and `rotate_viewer` from
`C:\ties\takeout\scripts`) writes, for each batch in `batches` and `batches\done`:
- per folder: `names → match_sidecars` output (file names only)
- `collect()` output: the ordered groups as relative paths

to `local/golden/<batch>.json` (git-ignored: these are your file names). The C# test `GoldenTests`
runs the matcher and the grouping in `--order python` mode on the same input and must give the same
result. It's skipped when `local/golden` doesn't exist, so CI stays green. This is the strongest parity
check there is: every real quirk in your Takeout.

### 3.4 Docs test

A test regenerates the shortcut table from `Bindings.All` and compares it with the block between
`<!-- shortcuts -->` markers in the README. It fails with the new text when the two differ.

---

## 4. Shortcut scheme

The rule of thumb: single lower-case keys for frequent actions, Shift for the bigger version, `g` + key
for "go to", and arrow/IrfanView keys alongside.

| Group | Keys | Action | Repeats |
| --- | --- | --- | --- |
| **Navigate** | `→` `j` `Space` `PageDown` | next photo | yes |
| | `←` `k` `Backspace` `PageUp` | previous photo | yes |
| | `Home` `g g` | first photo of the batch | |
| | `End` `G` | last photo of the batch | |
| | `:` `Ctrl+G` | go to photo number (box: type `1203` + Enter) | |
| | mouse wheel | next / previous photo | |
| **Albums** | `Shift+→` `J` `]` | first photo of the next album (loose photos count as the last album) | yes |
| | `Shift+←` `K` `[` | first photo of the previous album; mid-album: start of this album first (decision 5) | yes |
| | `g a` | album picker: type to filter, `↑`/`↓`, Enter to jump, Esc to close | |
| | `g l` | first loose photo | |
| | `g e` | last photo of the current album ("end") | |
| **Edit** | `r` | rotate right 90° | |
| | `l` | rotate left 90° | |
| | `R` | rotate 180° | |
| | `d` | delete the photo and all its copies (to `batches\deleted`) | **never** |
| | `z` `Ctrl+Z` | undo the last delete | |
| **View** | `t` | show/hide the thumbnail strip | |
| | `T` | cycle strip size: 5 → 8 → 3 per side | |
| | `i` | info panel: taken date (JSON and EXIF), camera, size, orientation, copies (identical?), albums, sidecar (shared?) | |
| | `f` `F11` `Enter` | full screen (Enter is IrfanView's habit) | |
| | `F12` | timing overlay | |
| **Other** | `o` | show the file in Explorer (selected) | |
| | `y` | copy the file path | |
| | `?` | this help | |
| | `Esc` | close the overlay → cancel a pending `g` → leave full screen → quit (after pending writes) | |

What changed compared with your starting point:
- **`g` + number dropped**: it needs an end key or a timeout and clashes with `g g`/`g l`. `:` (and
  `Ctrl+G`) open a small go-to box instead.
- **Added**: `Space`/`Backspace`/`PageDown`/`PageUp`/`Enter` (IrfanView), `R` (Shift = bigger: 180°),
  `T` (bigger strip), `g e`, `o`, `y`, `F12`.
- **Delete is `d` only.** The `Delete` key and `#` are unbound; `Delete`/`Shift+Delete` show the hint
  "delete is d (moves to batches\deleted; Photo Sift never deletes permanently)", so an old habit doesn't
  silently do nothing.
- **`j` = next, `k` = previous**, as in Gmail and vim; Shift makes it an album jump (`J` next album,
  `K` previous album).
- Esc leaves full screen before it quits, as is usual for a viewer.

**Help overlay** (`?`): a dark translucent panel in five columns, *Navigate · Albums · Edit · View ·
Other*, each row as key caps plus a description, generated from the table. Footer: "`g` starts a
sequence: press `g`, then the second key · Holding d deletes one photo · Esc closes". The overlay
also shows the current batch path and the deleted folder.

**Pending-key hint**: after `g`, the bottom-left shows `g …  g first · a albums · l loose · e album end`
until the sequence completes, is cancelled, or times out after 1.5 s.

---

## 5. Milestones

Each milestone ends with something to try on a real batch.

**M0 — Skeleton (½ day)**
Solution, projects, `global.json`, `Directory.Build.props`, `Directory.Packages.props`, README stub,
`.gitignore`, `.gitattributes`, MIT licence, CI workflow, and the first real tests (`Orientation.Rotate`).
*Try:* `dotnet build`, `dotnet test`, and CI green on GitHub.

**M1 — Index plus fast navigation of one batch**
- Core: `BatchLocator`, `SidecarMatcher`, `Sidecar`, `Grouping`, `BatchIndex`, review order in
  `--order python` mode only, `OrientationReader`, `Bindings`/`KeyDispatcher` with single keys only.
- Tests: sidecar and grouping tests, golden test with `export_golden.py`.
- App: window, header with shadow (counter, folder/file, +copies), orientation shown read-only, the full
  decode pipeline (bytes cache, preview then sharp, prefetch, generations, held-key mode), F11 / `f`,
  Esc, `--start`, resume position, timing overlay and CSV.
*Try:* open 2013 and 2016 next to the Python viewer, with the same numbers on the same photos; hold
`→` through 200 photos; read p50/p95 for cached and uncached from F12. **Targets** (decision 13):
cached under 50 ms, uncached preview under 50 ms and sharp under 200 ms (p95). If sharp misses, try
plan B (direct WIC).

*Status (2026-11-18):* done. As built:
- Golden tests agree with Python on all 63,211 files of the unsplit Takeout (sidecars) and all 9 real
  batches (groups and order); the orientation reader agrees with ExifTool on 2,736 real images
  (762 HEIC). Orientation values outside 1–8 (182 JPEGs have 0) count as upright, so unlike the Python
  viewer they can be rotated.
- `--bench` replaces driving the window with synthetic key presses (Windows won't give a background
  process focus): it runs a fixed script through the normal command path and logs the timing.
- After a jump, the current photo is decoded alone and prefetching starts once it's sharp; starting
  everything at once made the current photo compete with its neighbours (sharp p95 218 → 89 ms).
- Measured on 2013 (8–16 MP JPEGs, 3440×1369 box, 4 decode threads), WPF `BitmapImage` path, no
  plan B needed: cached 1.7 ms p50 / 5 ms p95; held key 61 of 61 photos shown from a cached preview
  (1–3 ms); uncached jumps first pixels 21–49 ms, sharp 36–89 ms; the first photo after start-up
  ~150 ms (cold start).
- HEIC shows a "decoder arrives in M2" message until `MagickDecoder` lands.

**M2 — Feature parity with the Python viewer**
- `ExifToolProcess`, `FileOpQueue`, rotate (with coalescing), delete (with Live Photo videos), undo,
  the delete journal with undo across restarts, quit waits, status messages, error paths.
- `MagickDecoder` and the HEIC check from §2.4.
- Tests: delete/undo, shared sidecars, rotate and byte-identical copies.
*Try:* review a copy of a small batch (or `done\2004`) completely with Photo Sift. Compare the result with
the Python viewer on another copy doing the same actions: the same files in `deleted\`, the same
hashes in the batch. Then upload a test album with immich-go and check the rotation and the pairing.

*Status (2026-11-18):* done, apart from the Immich checks, which need you (below). As built:
- Rotate (`r`/`l`/`R`), delete (`d`), undo (`z`/`Ctrl+Z`), the Delete key hint and quitting after pending
  writes, all through one ordered `FileOpQueue`; repeated rotations of a photo become one write.
- Delete moves the photo, its album copies, their sidecars and the Live Photo video (decision 10);
  `sift-journal.jsonl` lets undo reach deletes from earlier sessions (decision 8; the batch is read again
  after such an undo).
- Checked on a sandbox copy of `done\2011` with `--keys` (keys replayed through the dispatcher): after
  `r r l` photo 9 and its album copy have orientation 6 and identical bytes, the deleted tree mirrors the
  batch, and after undoing (also in a new session) the sandbox differs from the original only in the
  two rotated files. ExifTool's `-P` keeps the modified time to about a microsecond (as in Python).
- **HEIC never goes through WIC** (this changes decision 16): without the HEVC extension WIC returns a
  blank 1×1 image instead of failing, and with it, WIC applies the HEIF `irot` rotation to the pixels,
  so the EXIF orientation would be applied twice. Magick.NET with `heic:preserve-orientation` returns
  the stored pixels (tested on real iPhone photos: without the define they come out portrait).
- HEIC costs about 1 s per photo (libheif decodes single-threaded, ~750 ms for 24 MP, plus ~300 ms
  scaling with `Scale`; Magick's thread setting makes no difference). At a reviewing pace prefetching
  hides it; when it doesn't, the old photo is dimmed with "decoding…" after 150 ms.

*Still to check by hand:* upload a few rotated photos (a JPEG and a HEIC) with immich-go to a test
album, and see whether Immich shows the new rotation and pairs the album copies (decision 9).

**M3 — The new requirements**, in this order:
1. **M3a Review order**: albums first, home album, loose photos last, the header line
   `Album 3/23 · … · 12/120`, the album banner. `--order python` stays available.
2. **M3b Album jumps** (`J`/`K`/`]`/`[`/`Shift+arrows`, `g l`, `g e`) and the sequence dispatcher with the
   pending hint and table validation.
3. **M3c Thumbnail strip**: `ThumbnailCache`, strip with album dividers, `t`/`T`, click to jump.
4. **M3d Overlays**: help (`?`, generated), album picker (`g a`), go-to (`:`), info panel (`i`), `o`/`y`.
5. **M3e Optional**: README shortcut table test, release workflow.

*Try after each one:* review the next real year with it. After M3 the Python viewer is retired.

*Status (2026-11-18):* M3a–M3d done, and from M3e the README table test; the release workflow is
left for later. As built:
- `Review` (Core) makes the album-first order the default; `--order python` keeps the old one.
  Albums whose photos all show in an earlier album get no section but are listed (greyed) in the
  picker; empty album folders are left out.
- `KeyDispatcher` does two-key sequences with a 1.5 s timeout and the pending-key hint; an unknown
  second key cancels without running. The table check rejects a key that both starts a sequence and is
  bound on its own.
- The strip rebuilds its 2N+1 cells per step; thumbnails come from a separate lowest-priority thread
  (EXIF thumbnail, else a small WIC decode; HEIC from the pipeline's decoded frame).
- The help overlay and the README table (`<!-- shortcuts -->` block, kept in sync by a test; run the
  tests with `UPDATE_README=1` to rewrite it) are both generated from `Bindings.All`.
- `--keys` also logs the header after each key to `keys.log`, and types into overlays (`type:…`);
  scripted runs (`--bench`, `--keys`) don't move the resume position.
- Measured again on 2013 with the strip on: cached 4.2 ms p50 / 7.6 ms p95 (was 1.7 / 5: the strip
  rebuild), held key 3 ms from previews, uncached first pixels 36 ms p50, sharp 56 ms p50.

---

## 6. Decisions

Answered on 2026-11-18. The numbers are referred to elsewhere in the plan as "decision N".

1. **Album order**: by the date of the album's first photo in the batch, title as tie-breaker.
2. **Photos in several albums**: reviewed in the first album in review order; the other albums are
   shown as "also in".
3. **The same album-only photo in two shared albums**: one photo with a copy, grouped by the same rule
   as year copies (same name, and the same JSON date or identical bytes).
4. **Album numbers after deletes**: stable for the session. An album whose photos were all deleted is
   skipped by jumps but still counted.
5. **`K` / `[` in the middle of an album**: first to the start of the current album; at the start, to
   the previous album.
6. **Undo scope**: deletes only. A rotation is undone by rotating back.
7. **Copies with the same date but different bytes**: rotate each file (as now) and flag "copies differ"
   in the header and the info panel.
8. **Delete journal**: yes. `batches\deleted\<batch>\sift-journal.jsonl`, so undo survives a restart
   (part of M2).
9. **HEIC rotation**: M2 checks first whether Immich honours the EXIF orientation of a HEIC; until that's settled, rotating a HEIC shows a warning. The approach is decided after the check.
10. **Live Photos**: the paired video (same stem, `.MOV`/`.MP4`, in the same folder) and its sidecar
    move to `deleted` with the photo, and come back on undo.
11. **Loose photos in multi-year batches**: one loose section, sorted by date.
12. **Licence**: MIT.
13. **Timing target at 4K**: preview under 50 ms, sharp under 200 ms (uncached); cached stays under 50 ms.
14. **Colour profiles**: ignored for speed (`BitmapCreateOptions.IgnoreColorProfile`).
15. **Album folders directly in `batches\done\`**: known leftovers; ignored.
16. **HEIC decoding**: Magick.NET, so the paid HEVC Video Extensions aren't needed (decided after
    decisions 1–15). Changed in M2 from "fallback after WIC" to "always": WIC returns blank images
    without the codec and applies `irot` with it (see the M2 status).
