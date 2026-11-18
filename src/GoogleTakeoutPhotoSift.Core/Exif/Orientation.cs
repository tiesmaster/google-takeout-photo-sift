namespace Tiesmaster.GoogleTakeoutPhotoSift.Core.Exif;

/// <summary>
/// EXIF Orientation values (1–8). Rotating a photo only changes this tag, never the pixels.
/// </summary>
public static class Orientation
{
    /// <summary>
    /// The orientation after rotating clockwise by <paramref name="degrees"/> (a multiple of 90,
    /// negative for left), or null when <paramref name="orientation"/> is mirrored (2/4/5/7) or unknown.
    /// </summary>
    public static int? Rotate(int orientation, int degrees)
    {
        if (degrees % 90 != 0)
            throw new ArgumentOutOfRangeException(nameof(degrees), degrees, "Must be a multiple of 90.");
        if (ToDegrees(orientation) is not int current)
            return null;
        return FromDegrees(((current + degrees) % 360 + 360) % 360);
    }

    // Non-mirrored orientations as clockwise degrees, as in rotate_viewer.py's CW_DEGREES.
    private static int? ToDegrees(int orientation) => orientation switch
    {
        1 => 0,
        6 => 90,
        3 => 180,
        8 => 270,
        _ => null,
    };

    private static int FromDegrees(int degrees) => degrees switch
    {
        0 => 1,
        90 => 6,
        180 => 3,
        _ => 8,
    };
}
