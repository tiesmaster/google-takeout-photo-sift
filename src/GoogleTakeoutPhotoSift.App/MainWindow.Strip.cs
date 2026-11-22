using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Tiesmaster.GoogleTakeoutPhotoSift.App.Imaging;
using Tiesmaster.GoogleTakeoutPhotoSift.Core.Index;

namespace Tiesmaster.GoogleTakeoutPhotoSift.App;

// The thumbnail strip: the photos before and after the current one, with album boundaries marked and
// WhatsApp photos framed in green and partner photos in pink.
public partial class MainWindow
{
    private static readonly int[] StripSizes = [5, 8, 3];
    private static readonly Brush CellBackground = Frozen(new SolidColorBrush(Color.FromRgb(0x22, 0x22, 0x22)));
    private static readonly Brush CurrentBorder = Frozen(new SolidColorBrush(Colors.White));
    private static readonly Brush DividerBrush = Frozen(new SolidColorBrush(Color.FromArgb(0x99, 0xFF, 0xFF, 0xFF)));
    private static readonly Brush LabelBrush = Frozen(new SolidColorBrush(Color.FromRgb(0xBB, 0xBB, 0xBB)));

    private readonly HashSet<string> _stripPaths = new(StringComparer.OrdinalIgnoreCase);
    private ThumbnailCache? _thumbnails;
    private int _stripPerSide;

    private static Brush Frozen(Brush brush)
    {
        brush.Freeze();
        return brush;
    }

    private void InitStrip()
    {
        _stripPerSide = Math.Clamp(_settings.StripPerSide, 1, 20);
        Strip.Visibility = _settings.ShowStrip ? Visibility.Visible : Visibility.Collapsed;
        _thumbnails = new ThumbnailCache(path => _pipeline.GetStandIn(path),
            thumbnail => Dispatcher.BeginInvoke(() =>
            {
                if (_stripPaths.Contains(thumbnail.Path))
                    UpdateStrip();
            }));
    }

    private void ToggleStrip()
    {
        Strip.Visibility = Strip.Visibility == Visibility.Visible ? Visibility.Collapsed : Visibility.Visible;
        UpdateStrip();
    }

    private void CycleStripSize()
    {
        var at = Array.IndexOf(StripSizes, _stripPerSide);
        _stripPerSide = StripSizes[(at + 1) % StripSizes.Length];
        if (Strip.Visibility != Visibility.Visible)
            Strip.Visibility = Visibility.Visible;
        UpdateStrip();
        SetStatus($"strip: {_stripPerSide} photos each side");
    }

    /// <summary>Rebuilds the strip around the current photo (a handful of elements, cheap enough per step).</summary>
    private void UpdateStrip()
    {
        StripCells.Children.Clear();
        _stripPaths.Clear();
        if (Strip.Visibility != Visibility.Visible || _review is not { } review || CurrentPhoto is null || _thumbnails is null)
            return;

        var before = new List<int>();
        var p = (int?)_current;
        for (var i = 0; i < _stripPerSide && (p = Step(p!.Value, -1)) is not null; i++)
            before.Insert(0, p.Value);
        var after = new List<int>();
        p = _current;
        for (var i = 0; i < _stripPerSide && (p = Step(p!.Value, 1)) is not null; i++)
            after.Add(p.Value);

        int? previousSection = null;
        foreach (var index in before.Append(_current).Concat(after))
        {
            var photo = review.Photos[index];
            var section = review.SectionIndexOf(photo);
            var newAlbum = !review.IsPython && previousSection is not null && section != previousSection
                ? review.Sections[section].Title
                : null;
            StripCells.Children.Add(MakeCell(index, photo, index == _current, newAlbum));
            _stripPaths.Add(photo.Primary.Path);
            previousSection = section;
        }

        // Thumbnails nearest the current photo first, then one strip-width further on each side.
        var wanted = new List<string>();
        var ahead = (int?)_current;
        var behind = (int?)_current;
        wanted.Add(review.Photos[_current].Primary.Path);
        for (var i = 0; i < 2 * _stripPerSide; i++)
        {
            if (ahead is not null && (ahead = Step(ahead.Value, 1)) is int a)
                wanted.Add(review.Photos[a].Primary.Path);
            if (behind is not null && (behind = Step(behind.Value, -1)) is int b)
                wanted.Add(review.Photos[b].Primary.Path);
        }
        _thumbnails.Request(wanted.Where(path => !BatchIndex.IsVideo(path)).ToList());
        _stills.Request(wanted.Where(BatchIndex.IsVideo));
    }

    private FrameworkElement MakeCell(int index, Photo photo, bool current, string? newAlbum)
    {
        var (width, height) = current ? (150.0, 104.0) : (118.0, 82.0);
        var path = photo.Primary.Path;
        var box = new Grid { Width = width, Height = height, Background = CellBackground, ClipToBounds = true };
        if (photo.IsVideo)
        {
            // The still (when Windows has one) with a ▶ on top.
            if (_stills.TryGet(path, out var still) && still is not null)
                box.Children.Add(new Image { Source = still, Stretch = Stretch.Uniform });
            box.Children.Add(new TextBlock
            {
                Text = "▶",
                Foreground = Brushes.White,
                FontSize = current ? 34 : 26,
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Center,
                Effect = new System.Windows.Media.Effects.DropShadowEffect { ShadowDepth = 1, BlurRadius = 4, Opacity = 0.9 },
            });
        }
        else if (_thumbnails!.Get(path) is { } thumbnail)
        {
            box.Children.Add(new Image
            {
                Source = thumbnail.Bitmap,
                Stretch = Stretch.Uniform,
                LayoutTransform = OrientationTransform(_orientations.GetValueOrDefault(path, thumbnail.Orientation)),
            });
        }
        else
        {
            box.Children.Add(new TextBlock
            {
                Text = Path.GetFileName(path),
                Foreground = LabelBrush,
                FontSize = 10,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(4),
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Center,
            });
        }
        var cell = new Border
        {
            Child = box,
            BorderThickness = new Thickness(3),
            BorderBrush = SourceBrush(photo) ?? (current ? CurrentBorder : Brushes.Transparent),
            Margin = new Thickness(2, 0, 2, 0),
            Cursor = Cursors.Hand,
            ToolTip = $"{photo.Primary.Folder} / {photo.Primary.Name}",
        };
        cell.MouseLeftButtonUp += (_, _) => GoTo(index, index >= _current ? 1 : -1, repeat: false, Stopwatch.GetTimestamp());

        var column = new StackPanel { VerticalAlignment = VerticalAlignment.Bottom };
        column.Children.Add(new TextBlock
        {
            Text = newAlbum ?? "",
            Foreground = LabelBrush,
            FontSize = 11,
            MaxWidth = width + 4,
            TextTrimming = TextTrimming.CharacterEllipsis,
            Margin = new Thickness(2, 0, 0, 2),
        });
        column.Children.Add(cell);
        if (newAlbum is null)
            return column;
        // An album starts here: a line on the left, and its title above.
        return new Border
        {
            Child = column,
            BorderBrush = DividerBrush,
            BorderThickness = new Thickness(2, 0, 0, 0),
            Padding = new Thickness(6, 0, 0, 0),
            Margin = new Thickness(6, 0, 0, 0),
            ToolTip = newAlbum,
        };
    }
}
