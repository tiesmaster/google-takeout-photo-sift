using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Tiesmaster.GoogleTakeoutPhotoSift.App.Diagnostics;
using Tiesmaster.GoogleTakeoutPhotoSift.App.Imaging;
using Tiesmaster.GoogleTakeoutPhotoSift.App.Input;
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
    private readonly ResumeState _resume = ResumeState.Load();

    private BatchIndex? _index;
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
        _saveTimer = new DispatcherTimer(TimeSpan.FromSeconds(1), DispatcherPriority.Background, (_, _) => SaveResume(), Dispatcher)
            { IsEnabled = false };
        _resizeTimer = new DispatcherTimer(TimeSpan.FromMilliseconds(120), DispatcherPriority.Normal, (_, _) => OnResized(), Dispatcher)
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

    private Photo? CurrentPhoto => _index is { Photos.Count: > 0 } index ? index.Photos[_current] : null;

    private async Task LoadIndexAsync()
    {
        UpdateBox();
        var watch = Stopwatch.StartNew();
        try
        {
            // M1 only has the Python order; the album-first order arrives in milestone 3.
            _index = await Task.Run(() => BatchIndex.Load(_batchPath, IndexOptions.Python));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            Counter.Text = "";
            SetStatus($"can't read the batch: {e.Message}", error: true);
            return;
        }
        if (_index.Photos.Count == 0)
        {
            Counter.Text = "no photos";
            Caption.Text = _batchPath;
            return;
        }
        var copies = _index.Photos.Sum(p => p.CopyCount);
        SetStatus($"{_index.Photos.Count} photos, {copies} album copies linked, indexed in {watch.ElapsedMilliseconds} ms");
        GoTo(StartIndex(), direction: 1, repeat: false, Stopwatch.GetTimestamp(), keepStatus: true);
        if (_commandLine.Bench)
            await RunBenchmarkAsync();
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

    private int StartIndex()
    {
        var count = _index!.Photos.Count;
        if (_commandLine.Start is int start)
            return Math.Clamp(start - 1, 0, count - 1);
        if (_resume.Batches.TryGetValue(_batchPath, out var position))
        {
            for (var i = 0; i < count; i++)
            {
                if (string.Equals(Relative(_index.Photos[i].Primary.Path), position.RelativePath, StringComparison.OrdinalIgnoreCase))
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
        switch (command)
        {
            case Command.Next:
                GoTo(_current + 1, 1, repeat, timestamp);
                break;
            case Command.Previous:
                GoTo(_current - 1, -1, repeat, timestamp);
                break;
            case Command.First:
                GoTo(0, 1, repeat, timestamp);
                break;
            case Command.Last:
                GoTo((_index?.Photos.Count ?? 1) - 1, -1, repeat, timestamp);
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

    private void GoTo(int index, int direction, bool repeat, long timestamp, bool keepStatus = false)
    {
        if (_index is not { Photos.Count: > 0 } batch)
            return;
        index = Math.Clamp(index, 0, batch.Photos.Count - 1);
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
        // Otherwise the previous frame stays up (with the new header) until a frame arrives.
    }

    private void ScheduleAround()
    {
        if (_index is not { Photos.Count: > 0 } batch)
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
        for (var i = 1; i <= _settings.PrefetchAhead; i++)
        {
            var ahead = _current + _direction * i;
            if (ahead >= 0 && ahead < batch.Photos.Count)
            {
                // While a key is held, only cheap previews: a sharp decode would be stale before it's done.
                wanted.Add(new FrameRequest(batch.Photos[ahead].Primary.Path, _holding ? FrameKind.Preview : FrameKind.Sharp, 1 + i));
            }
        }
        if (!_holding)
        {
            for (var i = 1; i <= _settings.PrefetchBehind; i++)
            {
                var behind = _current - _direction * i;
                if (behind >= 0 && behind < batch.Photos.Count)
                    wanted.Add(new FrameRequest(batch.Photos[behind].Primary.Path, FrameKind.Sharp, 10 + i));
            }
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
        Photo.Source = frame.Bitmap;
        Photo.LayoutTransform = OrientationTransform(frame.Orientation);
        var (dpiX, dpiY) = DeviceScale();
        var box = _pipeline.Box;
        var scale = FrameGeometry.FitScale(frame.StoredWidth, frame.StoredHeight, frame.Orientation, box.W, box.H);
        // Width and height before the layout transform: the stored image's size on screen, in DIPs.
        Photo.Width = Math.Round(frame.StoredWidth * scale) / dpiX;
        Photo.Height = Math.Round(frame.StoredHeight * scale) / dpiY;
        _shown = (frame.Path, frame.Kind);
        CompleteTiming(frame, hit, frame.Kind == FrameKind.Sharp && FrameGeometry.BoxMatches(frame.Box, box));
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
        Counter.Text = $"{_current + 1} / {_index!.Photos.Count}";
        Caption.Text = $"{photo.Primary.Folder} / {photo.Primary.Name}";
        SubCaption.Text = photo.CopyCount switch
        {
            0 => "",
            1 => $"+1 album copy: {photo.Files[1].Folder}",
            var n => $"+{n} album copies: {string.Join(", ", photo.Files.Skip(1).Select(f => f.Folder))}",
        };
        SubCaption.Visibility = photo.CopyCount == 0 ? Visibility.Collapsed : Visibility.Visible;
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
        SaveResume();
        if (_timing is not null)
            _timingLog.Complete(_timing);
        base.OnClosing(e);
    }

    protected override void OnClosed(EventArgs e)
    {
        _pipeline.Dispose();
        _timingLog.Dispose();
        base.OnClosed(e);
    }
}
