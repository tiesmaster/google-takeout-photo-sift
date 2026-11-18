using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;

namespace Tiesmaster.GoogleTakeoutPhotoSift.App;

// The album list on the left: every section in review order with its photo count; the current album
// has an accent bar, a tinted background and bold text. Click an album to jump to its first photo.
public partial class MainWindow
{
    private static readonly Brush AccentBrush = Frozen(new SolidColorBrush(Color.FromRgb(0x4F, 0xA3, 0xFF)));
    private static readonly Brush CurrentAlbumBackground = Frozen(new SolidColorBrush(Color.FromRgb(0x1C, 0x2B, 0x3D)));
    private static readonly Brush AlbumBrush = Frozen(new SolidColorBrush(Color.FromRgb(0xC8, 0xC8, 0xC8)));
    private static readonly Brush CountBrush = Frozen(new SolidColorBrush(Color.FromRgb(0x8A, 0x8A, 0x8A)));
    private static readonly Brush EmptyAlbumBrush = Frozen(new SolidColorBrush(Color.FromRgb(0x55, 0x55, 0x55)));

    // What the list shows now, so it's only rebuilt when the album or the counts change.
    private (int Section, int Remaining)? _albumListState;

    private void InitAlbumList() =>
        AlbumList.Visibility = _settings.ShowAlbumList ? Visibility.Visible : Visibility.Collapsed;

    private void ToggleAlbumList()
    {
        AlbumList.Visibility = AlbumList.Visibility == Visibility.Visible ? Visibility.Collapsed : Visibility.Visible;
        _albumListState = null;
        UpdateAlbumList();
    }

    private void UpdateAlbumList()
    {
        if (_review is not { } review || AlbumList.Visibility != Visibility.Visible)
            return;
        if (review.IsPython)
        {
            // One section only: nothing to list.
            AlbumList.Visibility = Visibility.Collapsed;
            return;
        }
        var current = CurrentPhoto is { } photo ? review.SectionIndexOf(photo) : -1;
        var remaining = review.Photos.Count(p => !p.Deleted);
        if (_albumListState == (current, remaining))
            return;
        _albumListState = (current, remaining);

        AlbumListTitle.Text = $"{review.AlbumCount} ALBUMS · {remaining} PHOTOS";
        AlbumListItems.Children.Clear();
        FrameworkElement? currentRow = null;
        for (var i = 0; i < review.Sections.Count; i++)
        {
            var section = review.Sections[i];
            var count = section.Photos.Count(p => !p.Deleted);
            var row = MakeAlbumRow(i, section.Title, count, i == current, isLoose: section.Album is null);
            AlbumListItems.Children.Add(row);
            if (i == current)
                currentRow = row;
        }
        // Keep the current album in view once the list is laid out.
        if (currentRow is not null)
            Dispatcher.BeginInvoke(() => currentRow.BringIntoView(), System.Windows.Threading.DispatcherPriority.Loaded);
    }

    private FrameworkElement MakeAlbumRow(int sectionIndex, string title, int count, bool current, bool isLoose)
    {
        var name = new TextBlock
        {
            Text = title,
            Foreground = count == 0 ? EmptyAlbumBrush : current ? Brushes.White : AlbumBrush,
            FontWeight = current ? FontWeights.SemiBold : FontWeights.Normal,
            FontStyle = isLoose ? FontStyles.Italic : FontStyles.Normal,
            TextTrimming = TextTrimming.CharacterEllipsis,
            VerticalAlignment = VerticalAlignment.Center,
        };
        var number = new TextBlock
        {
            Text = count.ToString(),
            Foreground = current ? AccentBrush : CountBrush,
            FontWeight = current ? FontWeights.SemiBold : FontWeights.Normal,
            Margin = new Thickness(8, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center,
        };
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        Grid.SetColumn(number, 1);
        grid.Children.Add(name);
        grid.Children.Add(number);

        var row = new Border
        {
            Child = grid,
            Padding = new Thickness(10, 5, 12, 5),
            BorderThickness = new Thickness(4, 0, 0, 0),
            BorderBrush = current ? AccentBrush : Brushes.Transparent,
            Background = current ? CurrentAlbumBackground : Brushes.Transparent,
            Cursor = count > 0 ? Cursors.Hand : Cursors.Arrow,
            ToolTip = $"{title} · {count} photos",
        };
        TextElement.SetFontFamily(row, new FontFamily("Segoe UI"));
        TextElement.SetFontSize(row, 13);
        row.MouseLeftButtonUp += (_, _) =>
        {
            if (SectionBounds(sectionIndex).First is var first && first >= 0)
                GoTo(first, first >= _current ? 1 : -1, repeat: false, Stopwatch.GetTimestamp());
        };
        return row;
    }
}
