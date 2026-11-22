using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Tiesmaster.GoogleTakeoutPhotoSift.Core.Exif;
using Tiesmaster.GoogleTakeoutPhotoSift.Core.Index;
using Tiesmaster.GoogleTakeoutPhotoSift.Core.Input;
using Group = Tiesmaster.GoogleTakeoutPhotoSift.Core.Input.Group;

namespace Tiesmaster.GoogleTakeoutPhotoSift.App;

// The overlays: help (?), album picker (g a), go to number (:), and the info panel (i).
// A modal overlay gets the keyboard while it's open; Esc closes it.
public partial class MainWindow
{
    private enum Overlay
    {
        None,
        Help,
        Picker,
        GoTo,
    }

    private static readonly Brush Dimmed = Frozen(new SolidColorBrush(Color.FromRgb(0x88, 0x88, 0x88)));

    private Overlay _overlay;
    private int _infoRequest;

    private bool OverlayOpen => _overlay != Overlay.None;

    private void OpenOverlay(Overlay overlay)
    {
        CloseOverlay();
        _overlay = overlay;
        switch (overlay)
        {
            case Overlay.Help:
                BuildHelp();
                HelpOverlay.Visibility = Visibility.Visible;
                break;
            case Overlay.Picker:
                PickerFilter.Text = "";
                FillPicker();
                PickerOverlay.Visibility = Visibility.Visible;
                PickerFilter.Focus();
                break;
            case Overlay.GoTo:
                var count = _review!.Photos.Count(p => !p.Deleted);
                GoToTitle.Text = $"Go to photo (1–{count})";
                GoToBox.Text = "";
                GoToOverlay.Visibility = Visibility.Visible;
                GoToBox.Focus();
                break;
        }
    }

    private void CloseOverlay()
    {
        _overlay = Overlay.None;
        HelpOverlay.Visibility = Visibility.Collapsed;
        PickerOverlay.Visibility = Visibility.Collapsed;
        GoToOverlay.Visibility = Visibility.Collapsed;
        // Give the keyboard back to the window.
        Keyboard.Focus(this);
    }

    /// <summary>Keys while a modal overlay is open: true when handled here.</summary>
    private bool OverlayKeyDown(KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Escape:
                CloseOverlay();
                return true;
            case Key.Enter when _overlay == Overlay.Picker:
                JumpToPicked();
                return true;
            case Key.Enter when _overlay == Overlay.GoTo:
                GoToTyped();
                return true;
            case Key.Enter or Key.F1 when _overlay == Overlay.Help:
                CloseOverlay();
                return true;
            case Key.Down or Key.Up when _overlay == Overlay.Picker && PickerList.Items.Count > 0:
                var next = PickerList.SelectedIndex + (e.Key == Key.Down ? 1 : -1);
                PickerList.SelectedIndex = Math.Clamp(next, 0, PickerList.Items.Count - 1);
                PickerList.ScrollIntoView(PickerList.SelectedItem);
                return true;
        }
        // Typing goes to the overlay's text box; the help closes on any other key.
        if (_overlay == Overlay.Help)
        {
            CloseOverlay();
            return true;
        }
        return false;
    }

    // ---- help ----

    private void BuildHelp()
    {
        HelpColumns.Children.Clear();
        foreach (var group in Enum.GetValues<Group>())
        {
            var column = new StackPanel { Margin = new Thickness(0, 0, 28, 0), MaxWidth = 300 };
            column.Children.Add(new TextBlock
            {
                Text = group.ToString(),
                Foreground = Brushes.White,
                FontSize = 16,
                FontWeight = FontWeights.SemiBold,
                Margin = new Thickness(0, 0, 0, 6),
            });
            foreach (var binding in Bindings.All.Where(b => b.Group == group && b.Command != Command.DeleteKeyHint))
            {
                column.Children.Add(new TextBlock
                {
                    Text = string.Join("  ", binding.Keys.Select(Bindings.Display)),
                    Foreground = Brushes.White,
                    FontFamily = new FontFamily("Consolas"),
                    FontSize = 13,
                    Margin = new Thickness(0, 4, 0, 0),
                });
                column.Children.Add(new TextBlock
                {
                    Text = binding.Description,
                    Foreground = Dimmed,
                    FontSize = 12,
                    TextWrapping = TextWrapping.Wrap,
                });
            }
            HelpColumns.Children.Add(column);
        }
        HelpFooter.Text = "g starts a sequence: press g, then the second key (the hint shows which).  ·  "
                          + "Holding d deletes one photo; the Delete key only explains.  ·  Esc closes this.\n"
                          + $"Batch: {_batchPath}    Deleted photos go to: {_deletions?.DeletedRoot}";
    }

    // ---- album picker ----

    private sealed record PickerItem(string Label, int Target, bool Dim, bool Current);

    private void OnPickerFilterChanged(object sender, TextChangedEventArgs e) => FillPicker();

    private void OnPickerDoubleClick(object sender, MouseButtonEventArgs e) => JumpToPicked();

    private void FillPicker()
    {
        if (_review is not { } review)
            return;
        var filter = PickerFilter.Text.Trim();
        var compare = CultureInfo.CurrentCulture.CompareInfo;
        var current = review.SectionIndexOf(review.Photos[_current]);
        var items = new List<PickerItem>();
        foreach (var entry in review.Albums)
        {
            if (filter.Length > 0 && compare.IndexOf(entry.Album.Title, filter, CompareOptions.IgnoreCase | CompareOptions.IgnoreNonSpace) < 0)
                continue;
            var date = entry.FirstTaken?.ToLocalTime().ToString("d MMM yyyy", CultureInfo.CurrentCulture) ?? "no date";
            if (entry.Section is { } section)
            {
                var sectionIndex = review.Sections.ToList().IndexOf(section);
                var (first, _) = SectionBounds(sectionIndex);
                var left = section.Photos.Count(p => !p.Deleted);
                var label = $"{sectionIndex + 1,3}  {entry.Album.Title}   ·  {left} photos  ·  {date}"
                            + (entry.MemberCount > section.Photos.Count ? $"  (+{entry.MemberCount - section.Photos.Count} shown elsewhere)" : "");
                items.Add(new PickerItem(label, first, Dim: first < 0, Current: sectionIndex == current));
            }
            else
            {
                // All its photos are reviewed in an earlier album: jump to the first of them there.
                var target = review.Photos.Select((p, i) => (p, i))
                    .FirstOrDefault(x => !x.p.Deleted && review.AlbumsOf(x.p).Contains(entry.Album), (null!, -1)).Item2;
                items.Add(new PickerItem($"      {entry.Album.Title}   ·  {entry.MemberCount} photos, shown in {entry.ShownIn?.Title}",
                    target, Dim: true, Current: false));
            }
        }
        // The loose photos and the WhatsApp photos, after the albums.
        foreach (var index in new[] { review.LooseSection, review.WhatsAppSection })
        {
            if (index < 0)
                continue;
            var section = review.Sections[index];
            if (filter.Length > 0 && compare.IndexOf(section.Title, filter, CompareOptions.IgnoreCase) < 0)
                continue;
            items.Add(new PickerItem($"      {section.Title}   ·  {section.Photos.Count(p => !p.Deleted)} photos",
                SectionBounds(index).First, Dim: false, Current: current == index));
        }

        PickerList.Items.Clear();
        foreach (var item in items)
        {
            PickerList.Items.Add(new ListBoxItem
            {
                Content = item.Label,
                Tag = item.Target,
                Foreground = item.Dim ? Dimmed : Brushes.White,
                FontWeight = item.Current ? FontWeights.Bold : FontWeights.Normal,
            });
        }
        var selected = items.FindIndex(i => i.Current);
        PickerList.SelectedIndex = filter.Length > 0 || selected < 0 ? (items.Count > 0 ? 0 : -1) : selected;
        if (PickerList.SelectedItem is { } visible)
            PickerList.ScrollIntoView(visible);
    }

    private void JumpToPicked()
    {
        if (PickerList.SelectedItem is ListBoxItem { Tag: int target } && target >= 0)
        {
            CloseOverlay();
            GoTo(target, target >= _current ? 1 : -1, repeat: false, Stopwatch.GetTimestamp());
        }
    }

    // ---- go to number ----

    private void GoToTyped()
    {
        var text = GoToBox.Text.Trim();
        CloseOverlay();
        if (!int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out var number) || number < 1)
        {
            SetStatus($"'{text}' isn't a photo number");
            return;
        }
        // The number as the counter shows it: among the photos that aren't deleted.
        var index = Step(-1, 1, number);
        if (index is null)
        {
            SetStatus($"there are only {_review!.Photos.Count(p => !p.Deleted)} photos");
            return;
        }
        GoTo(index.Value, index.Value >= _current ? 1 : -1, repeat: false, Stopwatch.GetTimestamp());
    }

    // ---- info panel ----

    private void ToggleInfo()
    {
        InfoPanel.Visibility = InfoPanel.Visibility == Visibility.Visible ? Visibility.Collapsed : Visibility.Visible;
        UpdateInfo();
    }

    /// <summary>Fills the info panel for the current photo; the file's metadata is read off the UI thread.</summary>
    private async void UpdateInfo()
    {
        if (InfoPanel.Visibility != Visibility.Visible || CurrentPhoto is not { } photo || _review is not { } review)
            return;
        var request = ++_infoRequest;
        var files = photo.Files.Select(f => (f, Size: new FileInfo(f.Path) is { Exists: true } fi ? fi.Length : -1)).ToList();
        var info = await Task.Run(() => PhotoInfo.Read(photo.Primary.Path));
        var videoInfo = photo.IsVideo ? await Task.Run(() => VideoInfo.Read(photo.Primary.Path)) : null;
        if (request != _infoRequest)
            return;

        var text = new StringBuilder();
        void Line(string label, string? value)
        {
            if (!string.IsNullOrEmpty(value))
                text.AppendLine($"{label,-12}{value}");
        }
        Line("File", photo.Primary.Name);
        Line("Taken", photo.Primary.TakenUnix is long t
            ? DateTimeOffset.FromUnixTimeSeconds(t).ToLocalTime().ToString("ddd d MMM yyyy HH:mm:ss", CultureInfo.CurrentCulture) + "  (sidecar)"
            : null);
        Line("EXIF date", info.DateTimeOriginal?.ToString("ddd d MMM yyyy HH:mm:ss", CultureInfo.CurrentCulture));
        Line("Camera", info.Camera);
        Line("Source", photo.IsFromPartner ? "partner sharing (sidecar)"
            : !photo.IsWhatsApp ? null
            : photo.IsVideo ? "WhatsApp (at most 640 px)" : "WhatsApp (no camera, no EXIF date, at most 1600 px)");
        if (videoInfo is not null)
        {
            Line("Length", videoInfo.Duration is { } duration ? VideoInfo.FormatDuration(duration) : "unknown");
            Line("Size", (videoInfo.Width is int vw && videoInfo.Height is int vh ? $"{vw} × {vh}  ·  " : "") + $"{files[0].Size / 1e6:0.0} MB");
        }
        else
        {
            Line("Size", (info.Width is int w && info.Height is int h ? $"{w} × {h}  ·  " : "") + $"{files[0].Size / 1e6:0.0} MB");
            var orientation = _orientations.GetValueOrDefault(photo.Primary.Path, 1);
            Line("Orientation", $"{orientation} ({PhotoInfo.OrientationName(orientation)})");
        }
        var albums = review.AlbumsOf(photo);
        Line("Album", albums.Count == 0 ? "none (loose photo)" : albums[0].Title);
        if (albums.Count > 1)
            Line("Also in", string.Join(", ", albums.Skip(1).Select(a => a.Title)));
        text.AppendLine();
        text.AppendLine(photo.CopyCount == 0 ? "No album copies" : $"{photo.Files.Count} files (rotate and delete apply to all):");
        foreach (var (file, size) in files)
            text.AppendLine($"  {file.Folder} / {file.Name}  ·  {size / 1e6:0.00} MB");
        if (files.Select(f => f.Size).Distinct().Count() > 1)
            text.AppendLine("  ⚠ copies differ in size: immich-go won't pair them (decision 7)");
        if (photo.Primary.SidecarPath is { } sidecar)
        {
            text.AppendLine();
            var shared = _index!.SidecarUsers.TryGetValue(sidecar, out var users) && users.Count > 1;
            text.AppendLine($"Sidecar     {Path.GetFileName(sidecar)}" + (shared ? $"  (shared with {users!.Count - 1} other file(s))" : ""));
        }
        else
        {
            text.AppendLine();
            text.AppendLine("Sidecar     none");
        }
        if (_index!.LivePhotoVideos.TryGetValue(photo.Primary.Path, out var video))
            text.AppendLine($"Live Photo  {Path.GetFileName(video)} (moves along on delete)");
        InfoText.Text = text.ToString().TrimEnd();
    }

    // ---- file actions ----

    private void Reveal()
    {
        if (CurrentPhoto is not { } photo)
            return;
        Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{photo.Primary.Path}\"") { UseShellExecute = false });
    }

    /// <summary>p: the file in its default app, so a video plays in the usual player.</summary>
    private void OpenInApp()
    {
        if (CurrentPhoto is not { } photo)
            return;
        try
        {
            Process.Start(new ProcessStartInfo(photo.Primary.Path) { UseShellExecute = true });
        }
        catch (System.ComponentModel.Win32Exception e)
        {
            SetStatus($"can't open {photo.Primary.Name}: {e.Message}", error: true);
        }
    }

    private void CopyPath()
    {
        if (CurrentPhoto is not { } photo)
            return;
        try
        {
            Clipboard.SetText(photo.Primary.Path);
            SetStatus("path copied");
        }
        catch (System.Runtime.InteropServices.COMException)
        {
            SetStatus("the clipboard is busy; try again", error: true);
        }
    }
}
