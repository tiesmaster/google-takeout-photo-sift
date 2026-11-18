using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Tiesmaster.GoogleTakeoutPhotoSift.App.Diagnostics;
using Tiesmaster.GoogleTakeoutPhotoSift.App.Imaging;
using Tiesmaster.GoogleTakeoutPhotoSift.App.Input;
using Tiesmaster.GoogleTakeoutPhotoSift.Core.Exif;
using Tiesmaster.GoogleTakeoutPhotoSift.Core.Files;
using Tiesmaster.GoogleTakeoutPhotoSift.Core.Index;
using Tiesmaster.GoogleTakeoutPhotoSift.Core.Input;
using Tiesmaster.GoogleTakeoutPhotoSift.Core.Settings;
using CommandLine = Tiesmaster.GoogleTakeoutPhotoSift.Core.Settings.CommandLine;

namespace Tiesmaster.GoogleTakeoutPhotoSift.App;

public partial class MainWindow : Window
{
    private static readonly Brush StatusInfo = new SolidColorBrush(Color.FromRgb(0xDD, 0xDD, 0xDD));
    private static readonly Brush StatusError = new SolidColorBrush(Color.FromRgb(0xFF, 0x70, 0x70));

    private readonly CommandLine _commandLine;
    private readonly SiftSettings _settings;
    private readonly string _batchPath;
    private readonly KeyDispatcher _dispatcher = new();
    private readonly WpfKeyTranslator _keys = new();
    private readonly FramePipeline _pipeline;
    private readonly TimingLog _timingLog = new(Path.Combine(SiftSettings.AppDataFolder, "timing.csv"));
    private readonly DispatcherTimer _saveTimer;
    private readonly DispatcherTimer _resizeTimer;
    private readonly DispatcherTimer _slowTimer;
    private readonly ResumeState _resume = ResumeState.Load();
    private readonly FileOpQueue _fileOps;

    // The orientation of each photo as shown: read from the file once, then changed by rotations.
    private readonly Dictionary<string, int> _orientations = new(StringComparer.OrdinalIgnoreCase);

    private BatchIndex? _index;
    private Review? _review;
    private Deletions? _deletions;
    private DecodedFrame? _shownFrame;
    private int? _shownSection;
    private int _current;
    private int _direction = 1;
    private bool _holding;
    private (string Path, FrameKind Kind)? _shown;
    private NavigationTiming? _timing;
    private bool _firstShownPending;
    private bool _sharpShownPending;
    private bool _fullScreen;
    private WindowState _windowStateBeforeFullScreen = WindowState.Maximized;

    public MainWindow(CommandLine commandLine, SiftSettings settings, string batchPath)
    {
        InitializeComponent();
        _commandLine = commandLine;
        _settings = settings;
        _batchPath = batchPath;
        _pipeline = new FramePipeline(settings, frame => Dispatcher.BeginInvoke(() => OnFrame(frame)));
        _fileOps = new FileOpQueue(() => new ExifToolProcess(settings.ExifToolPath),
            (op, error) => Dispatcher.BeginInvoke(() => OnFileOpCompleted(op, error)));
        _saveTimer = new DispatcherTimer(TimeSpan.FromSeconds(1), DispatcherPriority.Background, (_, _) => SaveResume(), Dispatcher)
            { IsEnabled = false };
        _resizeTimer = new DispatcherTimer(TimeSpan.FromMilliseconds(120), DispatcherPriority.Normal, (_, _) => OnResized(), Dispatcher)
            { IsEnabled = false };
        _slowTimer = new DispatcherTimer(TimeSpan.FromMilliseconds(150), DispatcherPriority.Normal, (_, _) => OnSlowFrame(), Dispatcher)
            { IsEnabled = false };
        Timing.Visibility = settings.ShowTiming || commandLine.Timing ? Visibility.Visible : Visibility.Collapsed;
        Title = $"Google Takeout Photo Sift — {batchPath}";
        Counter.Text = "indexing…";
        Caption.Text = batchPath;
        Loaded += async (_, _) => await LoadIndexAsync();
        PhotoArea.SizeChanged += (_, _) =>
        {
            _resizeTimer.Stop();
            _resizeTimer.Start();
        };
    }

    private Photo? CurrentPhoto =>
        _review is { Photos.Count: > 0 } review && !review.Photos[_current].Deleted ? review.Photos[_current] : null;

    private string BatchesRoot => _commandLine.BatchesRoot ?? _settings.BatchesRoot;

    private async Task LoadIndexAsync()
    {
        UpdateBox();
        var watch = Stopwatch.StartNew();
        if (!await ReloadIndexAsync())
            return;
        var copies = _review!.Photos.Sum(p => p.CopyCount);
        var undo = _deletions!.UndoStack.Count;
        SetStatus($"{_review!.Photos.Count} photos, {copies} album copies linked, indexed in {watch.ElapsedMilliseconds} ms"
                  + (undo > 0 ? $" · {undo} earlier deletes can be undone (z)" : ""));
        if (_review!.Photos.Count == 0)
        {
            ShowEmpty();
            return;
        }
        GoTo(StartIndex(), direction: 1, repeat: false, Stopwatch.GetTimestamp(), keepStatus: true);
        if (_commandLine.Bench)
            await RunBenchmarkAsync();
        else if (_commandLine.Keys is { } keys)
            await PlayKeysAsync(keys);
    }

    /// <summary>
    /// <c>--keys</c>: plays keys through the dispatcher like real key presses, with a snapshot after
    /// each (keys-NN.png next to timing.csv), then quits once pending writes are done.
    /// </summary>
    private async Task PlayKeysAsync(IReadOnlyList<string> keys)
    {
        await Task.Delay(1000);
        for (var i = 0; i < keys.Count; i++)
        {
            if (keys[i] == "wait")
            {
                await Task.Delay(1000);
                continue;
            }
            if (_dispatcher.Dispatch(KeyChord.Parse(keys[i]), isRepeat: false) is { } command)
                Execute(command, repeat: false, Stopwatch.GetTimestamp());
            await Task.Delay(400);
            Snapshot($"keys-{i + 1:00}");
        }
        Close();
    }

    /// <summary>
    /// <c>--bench</c>: single steps, a held key, the release and a few steps back, through the same
    /// command path as real keys. The timing goes to timing.csv, snapshots next to it, then it quits.
    /// </summary>
    private async Task RunBenchmarkAsync()
    {
        Timing.Visibility = Visibility.Visible;
        await Task.Delay(1500);
        for (var i = 0; i < 30; i++)
        {
            Execute(Command.Next, repeat: false, Stopwatch.GetTimestamp());
            await Task.Delay(500);
        }
        Snapshot("bench-1-steps");
        // A held key: the first press, then repeats at about the Windows default rate.
        var until = DateTime.UtcNow.AddSeconds(3);
        Execute(Command.Next, repeat: false, Stopwatch.GetTimestamp());
        while (DateTime.UtcNow < until)
        {
            await Task.Delay(33);
            Execute(Command.Next, repeat: true, Stopwatch.GetTimestamp());
        }
        Snapshot("bench-2-holding");
        EndHold();
        await Task.Delay(1500);
        Snapshot("bench-3-released");
        for (var i = 0; i < 8; i++)
        {
            Execute(Command.Previous, repeat: false, Stopwatch.GetTimestamp());
            await Task.Delay(400);
        }
        Snapshot("bench-4-back");
        // Jumps far enough that nothing is prefetched: the uncached path.
        for (var i = 0; i < 10; i++)
        {
            GoTo(_current + 37, 1, repeat: false, Stopwatch.GetTimestamp());
            await Task.Delay(700);
        }
        Snapshot("bench-5-jumps");
        Close();
    }

    private void Snapshot(string name)
    {
        var (dpiX, dpiY) = DeviceScale();
        var bitmap = new RenderTargetBitmap((int)(PhotoArea.ActualWidth * dpiX / 2), (int)(PhotoArea.ActualHeight * dpiY / 2),
            96 * dpiX / 2, 96 * dpiY / 2, PixelFormats.Pbgra32);
        bitmap.Render(PhotoArea);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var file = File.Create(Path.Combine(SiftSettings.AppDataFolder, $"{name}.png"));
        encoder.Save(file);
    }

    /// <summary>(Re)reads the batch from disk; the undo stack carries over. False when it can't be read.</summary>
    private async Task<bool> ReloadIndexAsync()
    {
        try
        {
            var python = _commandLine.Order == ReviewOrderMode.Python;
            var (index, review) = await Task.Run(() =>
            {
                var loaded = BatchIndex.Load(_batchPath, python ? IndexOptions.Python : new IndexOptions());
                return (loaded, python ? Review.Python(loaded) : Review.AlbumFirst(loaded));
            });
            _index = index;
            _review = review;
            if (_deletions is null)
                _deletions = new Deletions(index, BatchesRoot);
            else
                _deletions.Use(index);
            _current = Math.Clamp(_current, 0, Math.Max(0, index.Photos.Count - 1));
            return true;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            Counter.Text = "";
            SetStatus($"can't read the batch: {e.Message}", error: true);
            return false;
        }
    }

    private int StartIndex()
    {
        var count = _review!.Photos.Count;
        if (_commandLine.Start is int start)
            return Math.Clamp(start - 1, 0, count - 1);
        if (_resume.Batches.TryGetValue(_batchPath, out var position))
        {
            for (var i = 0; i < count; i++)
            {
                if (string.Equals(Relative(_review!.Photos[i].Primary.Path), position.RelativePath, StringComparison.OrdinalIgnoreCase))
                    return i;
            }
            return Math.Clamp(position.Index, 0, count - 1);
        }
        return 0;
    }

    // ---- input ----

    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        if (_keys.FromKeyDown(e) is { } chord)
        {
            if (_dispatcher.IsBound(chord))
            {
                e.Handled = true;
                if (_dispatcher.Dispatch(chord, e.IsRepeat) is { } command)
                    Execute(command, e.IsRepeat, Stopwatch.GetTimestamp());
            }
        }
        base.OnPreviewKeyDown(e);
    }

    protected override void OnPreviewTextInput(TextCompositionEventArgs e)
    {
        if (_keys.FromTextInput(e) is var (chord, isRepeat) && _dispatcher.IsBound(chord))
        {
            e.Handled = true;
            if (_dispatcher.Dispatch(chord, isRepeat) is { } command)
                Execute(command, isRepeat, Stopwatch.GetTimestamp());
        }
        base.OnPreviewTextInput(e);
    }

    protected override void OnPreviewKeyUp(KeyEventArgs e)
    {
        EndHold();
        base.OnPreviewKeyUp(e);
    }

    private void EndHold()
    {
        if (!_holding)
            return;
        // The held key came up: decode the photo we stopped on, sharp, and rebuild the window.
        _holding = false;
        ShowBest();
        ScheduleAround();
    }

    private void Execute(Command command, bool repeat, long timestamp)
    {
        if (_review is null)
            return;
        switch (command)
        {
            case Command.Next when Step(_current, 1) is int next:
                GoTo(next, 1, repeat, timestamp);
                break;
            case Command.Previous when Step(_current, -1) is int previous:
                GoTo(previous, -1, repeat, timestamp);
                break;
            case Command.First when Step(-1, 1) is int first:
                GoTo(first, 1, repeat, timestamp);
                break;
            case Command.Last when Step(_review!.Photos.Count, -1) is int last:
                GoTo(last, -1, repeat, timestamp);
                break;
            case Command.RotateRight:
                Rotate(90);
                break;
            case Command.RotateLeft:
                Rotate(-90);
                break;
            case Command.Rotate180:
                Rotate(180);
                break;
            case Command.Delete:
                Delete();
                break;
            case Command.DeleteKeyHint:
                SetStatus("delete is d (it moves the photo to batches\\deleted; Photo Sift never deletes permanently)");
                break;
            case Command.Undo:
                Undo();
                break;
            case Command.ToggleFullScreen:
                ToggleFullScreen();
                break;
            case Command.ToggleTiming:
                Timing.Visibility = Timing.Visibility == Visibility.Visible ? Visibility.Collapsed : Visibility.Visible;
                UpdateTimingText();
                break;
            case Command.Escape:
                if (_fullScreen)
                    ToggleFullScreen();
                else
                    Close();
                break;
        }
    }

    // ---- navigation and display ----

    /// <summary>
    /// The photo <paramref name="steps"/> places from <paramref name="from"/> in
    /// <paramref name="direction"/>, skipping deleted photos; null past either end.
    /// </summary>
    private int? Step(int from, int direction, int steps = 1)
    {
        var photos = _review!.Photos;
        var i = from;
        for (var n = 0; n < steps; n++)
        {
            do
            {
                i += direction;
                if (i < 0 || i >= photos.Count)
                    return null;
            }
            while (photos[i].Deleted);
        }
        return i;
    }

    private void GoTo(int index, int direction, bool repeat, long timestamp, bool keepStatus = false)
    {
        if (_review is not { Photos.Count: > 0 } batch)
            return;
        index = Math.Clamp(index, 0, batch.Photos.Count - 1);
        if (batch.Photos[index].Deleted)
            return;
        if (index == _current && _shown is not null)
            return;
        if (_timing is not null)
            _timingLog.Complete(_timing);

        _current = index;
        _direction = direction;
        _holding = repeat;
        var photo = batch.Photos[index];
        _timing = new NavigationTiming(index + 1, Relative(photo.Primary.Path), timestamp);
        if (!keepStatus)
            SetStatus("");
        UpdateHeader();
        ShowBest();
        ScheduleAround();
        _saveTimer.Stop();
        _saveTimer.Start();
    }

    /// <summary>Shows the best frame we already have for the current photo, without waiting.</summary>
    private void ShowBest()
    {
        if (CurrentPhoto is not { } photo)
            return;
        var path = photo.Primary.Path;
        if (_pipeline.GetSharp(path) is { } sharp)
        {
            Show(sharp, CacheHit.Sharp);
        }
        else if (_pipeline.GetStandIn(path) is { } standIn)
        {
            Show(standIn, CacheHit.Preview);
        }
        else
        {
            // The previous frame stays up (with the new header) until a frame arrives; if that takes
            // a while (a HEIC, about a second), dim it so it isn't mistaken for this photo.
            _slowTimer.Stop();
            _slowTimer.Start();
        }
    }

    private void OnSlowFrame()
    {
        _slowTimer.Stop();
        if (CurrentPhoto is { } photo && _shown?.Path != photo.Primary.Path)
        {
            Photo.Opacity = 0.25;
            if (string.IsNullOrEmpty(Status.Text))
                SetStatus("decoding…");
        }
    }

    private void ScheduleAround()
    {
        if (_review is not { Photos.Count: > 0 } batch)
            return;
        var wanted = new List<FrameRequest>();
        var path = batch.Photos[_current].Primary.Path;
        wanted.Add(new FrameRequest(path, FrameKind.Preview, 0));
        if (!_holding && _pipeline.GetSharp(path) is null)
        {
            // The current photo first, on its own: prefetching now would compete with it for the CPU
            // and memory bandwidth. OnFrame schedules the neighbours once it's in.
            wanted.Add(new FrameRequest(path, FrameKind.Sharp, 1));
            _pipeline.Schedule(wanted);
            return;
        }
        var ahead = (int?)_current;
        for (var i = 1; i <= _settings.PrefetchAhead && (ahead = Step(ahead!.Value, _direction)) is not null; i++)
        {
            // While a key is held, only cheap previews: a sharp decode would be stale before it's done.
            wanted.Add(new FrameRequest(batch.Photos[ahead.Value].Primary.Path, _holding ? FrameKind.Preview : FrameKind.Sharp, 1 + i));
        }
        if (!_holding)
        {
            var behind = (int?)_current;
            for (var i = 1; i <= _settings.PrefetchBehind && (behind = Step(behind!.Value, -_direction)) is not null; i++)
                wanted.Add(new FrameRequest(batch.Photos[behind.Value].Primary.Path, FrameKind.Sharp, 10 + i));
        }
        _pipeline.Schedule(wanted);
    }

    private void OnFrame(DecodedFrame frame)
    {
        if (CurrentPhoto is not { } photo || !string.Equals(photo.Primary.Path, frame.Path, StringComparison.OrdinalIgnoreCase))
            return;
        if (frame.Kind == FrameKind.Sharp && FrameGeometry.BoxMatches(frame.Box, _pipeline.Box))
        {
            Show(frame, CacheHit.Miss);
            // The current photo is sharp: now prefetch its neighbours.
            ScheduleAround();
        }
        else if (frame.Kind == FrameKind.Preview && !IsShowingSharp(frame.Path))
        {
            Show(frame, CacheHit.Miss);
        }
        else if (frame.Error is not null)
        {
            Show(frame, CacheHit.Miss);
        }
    }

    private bool IsShowingSharp(string path) =>
        _shown is { } shown && shown.Kind == FrameKind.Sharp && string.Equals(shown.Path, path, StringComparison.OrdinalIgnoreCase);

    private void Show(DecodedFrame frame, CacheHit hit)
    {
        if (frame.Error is not null)
        {
            Photo.Source = null;
            _shown = (frame.Path, FrameKind.Sharp);
            SetStatus(frame.Error, error: true);
            CompleteTiming(frame, hit, sharp: true);
            return;
        }
        _orientations.TryAdd(frame.Path, frame.Orientation);
        _slowTimer.Stop();
        Photo.Opacity = 1;
        if (Status.Text == "decoding…")
            SetStatus("");
        Photo.Source = frame.Bitmap;
        _shownFrame = frame;
        Fit();
        _shown = (frame.Path, frame.Kind);
        CompleteTiming(frame, hit, frame.Kind == FrameKind.Sharp && FrameGeometry.BoxMatches(frame.Box, _pipeline.Box));
    }

    /// <summary>Sizes and turns the shown frame for its current orientation (also right after a rotation).</summary>
    private void Fit()
    {
        if (_shownFrame is not { Bitmap: not null } frame)
            return;
        var orientation = _orientations.GetValueOrDefault(frame.Path, frame.Orientation);
        Photo.LayoutTransform = OrientationTransform(orientation);
        var (dpiX, dpiY) = DeviceScale();
        var box = _pipeline.Box;
        var scale = FrameGeometry.FitScale(frame.StoredWidth, frame.StoredHeight, orientation, box.W, box.H);
        // Width and height before the layout transform: the stored image's size on screen, in DIPs.
        Photo.Width = Math.Round(frame.StoredWidth * scale) / dpiX;
        Photo.Height = Math.Round(frame.StoredHeight * scale) / dpiY;
    }

    /// <summary>No photo to show: everything is deleted, or the batch is empty.</summary>
    private void ShowEmpty()
    {
        Photo.Source = null;
        _shownFrame = null;
        _shown = null;
        Counter.Text = "no photos left";
        Caption.Text = _batchPath;
        SubCaption.Visibility = Visibility.Collapsed;
    }

    private static Transform OrientationTransform(int orientation) => orientation switch
    {
        2 => new ScaleTransform(-1, 1),
        3 => new RotateTransform(180),
        4 => new ScaleTransform(1, -1),
        5 => new TransformGroup { Children = { new ScaleTransform(-1, 1), new RotateTransform(270) } },
        6 => new RotateTransform(90),
        7 => new TransformGroup { Children = { new ScaleTransform(-1, 1), new RotateTransform(90) } },
        8 => new RotateTransform(270),
        _ => Transform.Identity,
    };

    private void UpdateHeader()
    {
        if (CurrentPhoto is not { } photo)
            return;
        var photos = _review!.Photos;
        var position = 1;
        for (var i = 0; i < _current; i++)
        {
            if (!photos[i].Deleted)
                position++;
        }
        Counter.Text = $"{position} / {photos.Count(p => !p.Deleted)}";

        // Album 3/23 · Vakantie IJsland [2016] · 12/120, counting photos that aren't deleted.
        var review = _review;
        var sectionIndex = review.SectionIndexOf(photo);
        var section = review.Sections[sectionIndex];
        var inSection = 1;
        for (var i = section.Start; i < _current; i++)
        {
            if (!photos[i].Deleted)
                inSection++;
        }
        var sectionCount = section.Photos.Count(p => !p.Deleted);
        AlbumLine.Text = review.IsPython ? ""
            : section.Album is null ? $"Loose photos · {inSection}/{sectionCount}"
            : $"Album {sectionIndex + 1}/{review.AlbumCount} · {section.Title} · {inSection}/{sectionCount}";
        AlbumLine.Visibility = review.IsPython ? Visibility.Collapsed : Visibility.Visible;
        if (_shownSection is int previous && previous != sectionIndex && !review.IsPython)
            ShowBanner(section.Title);
        _shownSection = sectionIndex;

        Caption.Text = $"{photo.Primary.Folder} / {photo.Primary.Name}";
        var notes = new List<string>();
        if (photo.CopyCount == 1)
            notes.Add($"+1 album copy: {photo.Files[1].Folder}");
        else if (photo.CopyCount > 1)
            notes.Add($"+{photo.CopyCount} album copies: {string.Join(", ", photo.Files.Skip(1).Select(f => f.Folder))}");
        var alsoIn = review.AlbumsOf(photo).Skip(review.IsPython ? 0 : 1).Select(a => a.Title).ToList();
        if (alsoIn.Count > 0)
            notes.Add((review.IsPython ? "in: " : "also in: ") + string.Join(", ", alsoIn));
        if (_index!.LivePhotoVideos.TryGetValue(photo.Primary.Path, out var video))
            notes.Add($"Live Photo: {Path.GetFileName(video)}");
        SubCaption.Text = string.Join("   ·   ", notes);
        SubCaption.Visibility = notes.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
    }

    /// <summary>The album title in the middle of the screen for a second, fading out.</summary>
    private void ShowBanner(string title)
    {
        AlbumBannerText.Text = title;
        var fade = new DoubleAnimationUsingKeyFrames();
        fade.KeyFrames.Add(new DiscreteDoubleKeyFrame(1, KeyTime.FromTimeSpan(TimeSpan.Zero)));
        fade.KeyFrames.Add(new DiscreteDoubleKeyFrame(1, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(900))));
        fade.KeyFrames.Add(new LinearDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(1300))));
        AlbumBanner.BeginAnimation(OpacityProperty, fade);
    }

    private void SetStatus(string text, bool error = false)
    {
        Status.Text = text;
        Status.Foreground = error ? StatusError : StatusInfo;
    }

    // ---- timing ----

    private void CompleteTiming(DecodedFrame frame, CacheHit hit, bool sharp)
    {
        if (_timing is not { Completed: false } timing || !string.Equals(Relative(frame.Path), timing.Path, StringComparison.OrdinalIgnoreCase))
            return;
        if (timing.FirstHit is null)
        {
            timing.FirstHit = hit;
            _firstShownPending = true;
        }
        if (sharp)
        {
            timing.ReadMs = frame.ReadMs;
            timing.DecodeMs = frame.DecodeMs;
            timing.FileBytes = frame.FileBytes;
            timing.Megapixels = frame.StoredWidth * (double)frame.StoredHeight / 1e6;
            _sharpShownPending = true;
        }
        // The frame is on screen once WPF renders it; measure up to the next render pass.
        CompositionTarget.Rendering -= OnRendering;
        CompositionTarget.Rendering += OnRendering;
    }

    private void OnRendering(object? sender, EventArgs e)
    {
        CompositionTarget.Rendering -= OnRendering;
        if (_timing is not { Completed: false } timing)
            return;
        var ms = Stopwatch.GetElapsedTime(timing.StartTimestamp).TotalMilliseconds;
        if (_firstShownPending)
        {
            timing.FirstMs = ms;
            _firstShownPending = false;
        }
        if (_sharpShownPending)
        {
            timing.SharpMs = ms;
            _sharpShownPending = false;
            _timingLog.Complete(timing);
        }
        UpdateTimingText();
    }

    private void UpdateTimingText()
    {
        if (Timing.Visibility == Visibility.Visible)
            Timing.Text = _timingLog.Summary($"box {_pipeline.Box.W}×{_pipeline.Box.H}  {_pipeline.WorkerCount} decode threads");
    }

    // ---- window ----

    private (double X, double Y) DeviceScale()
    {
        var matrix = PresentationSource.FromVisual(this)?.CompositionTarget?.TransformToDevice;
        return matrix is { } m ? (m.M11, m.M22) : (1, 1);
    }

    private void UpdateBox()
    {
        var (dpiX, dpiY) = DeviceScale();
        var width = (int)Math.Round(PhotoArea.ActualWidth * dpiX);
        var height = (int)Math.Round(PhotoArea.ActualHeight * dpiY);
        if (width > 0 && height > 0)
            _pipeline.Box = (width, height);
    }

    private void OnResized()
    {
        _resizeTimer.Stop();
        UpdateBox();
        if (CurrentPhoto is null)
            return;
        // Re-fit what's shown now, and decode sharp frames for the new size.
        _shown = null;
        ShowBest();
        ScheduleAround();
    }

    private void ToggleFullScreen()
    {
        if (!_fullScreen)
        {
            _windowStateBeforeFullScreen = WindowState;
            WindowStyle = WindowStyle.None;
            ResizeMode = ResizeMode.NoResize;
            // Going through Normal makes the maximized window cover the taskbar.
            WindowState = WindowState.Normal;
            WindowState = WindowState.Maximized;
        }
        else
        {
            WindowStyle = WindowStyle.SingleBorderWindow;
            ResizeMode = ResizeMode.CanResize;
            WindowState = _windowStateBeforeFullScreen;
        }
        _fullScreen = !_fullScreen;
    }

    private string Relative(string path) =>
        Path.GetRelativePath(_index?.PhotosRoot ?? _batchPath, path).Replace('\\', '/');

    private void SaveResume()
    {
        _saveTimer.Stop();
        if (CurrentPhoto is not { } photo)
            return;
        _resume.Batches[_batchPath] = new ResumeState.Position(Relative(photo.Primary.Path), _current);
        try
        {
            _resume.Save();
        }
        catch (IOException)
        {
            // Not worth bothering the user about; the next save tries again.
        }
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        if (!ReadyToClose())
        {
            e.Cancel = true;
            return;
        }
        SaveResume();
        if (_timing is not null)
            _timingLog.Complete(_timing);
        base.OnClosing(e);
    }

    protected override void OnClosed(EventArgs e)
    {
        _fileOps.Dispose();
        _pipeline.Dispose();
        _timingLog.Dispose();
        base.OnClosed(e);
    }
}
