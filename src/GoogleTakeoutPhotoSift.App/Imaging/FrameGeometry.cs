namespace Tiesmaster.GoogleTakeoutPhotoSift.App.Imaging;

/// <summary>Fitting a photo into the window, taking the EXIF orientation into account.</summary>
public static class FrameGeometry
{
    /// <summary>Orientations 5–8 swap width and height on screen.</summary>
    public static bool SwapsAxes(int orientation) => orientation is >= 5 and <= 8;

    /// <summary>
    /// The scale (at most 1, never upscaling) that fits a stored <paramref name="width"/> ×
    /// <paramref name="height"/> image, shown with <paramref name="orientation"/>, into the box.
    /// </summary>
    public static double FitScale(int width, int height, int orientation, int boxWidth, int boxHeight)
    {
        var (shownWidth, shownHeight) = SwapsAxes(orientation) ? (height, width) : (width, height);
        if (shownWidth <= 0 || shownHeight <= 0 || boxWidth <= 0 || boxHeight <= 0)
            return 1;
        return Math.Min(1, Math.Min((double)boxWidth / shownWidth, (double)boxHeight / shownHeight));
    }

    /// <summary>Whether a frame decoded for one box is still sharp enough for another (within 10%).</summary>
    public static bool BoxMatches((int W, int H) decodedFor, (int W, int H) box) =>
        Close(decodedFor.W, box.W) && Close(decodedFor.H, box.H);

    private static bool Close(int a, int b) => Math.Abs(a - b) <= 0.1 * Math.Max(a, b);
}
