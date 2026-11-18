using System.IO;
using System.Numerics;
using Tiesmaster.GoogleTakeoutPhotoSift.App.Imaging;
using Tiesmaster.GoogleTakeoutPhotoSift.Core.Index;

namespace Tiesmaster.GoogleTakeoutPhotoSift.App.Tests.Imaging;

public class FingerprintTests
{
    private static string Fixture(string name) => Path.Combine(AppContext.BaseDirectory, "fixtures", name);

    private static int Distance(ulong a, ulong b) => BitOperations.PopCount(a ^ b);

    [Fact]
    public void An_upright_re_encoded_copy_matches_its_original()
    {
        var original = Fingerprint.Compute(Fixture("twin-original.jpg"), orientation: 6);
        var upright = Fingerprint.Compute(Fixture("twin-upright.jpg"), orientation: 1);

        Assert.InRange(Distance(original, upright), 0, Twins.MaxDistance);
    }

    [Fact]
    public void Another_picture_does_not_match()
    {
        var original = Fingerprint.Compute(Fixture("twin-original.jpg"), orientation: 6);
        var other = Fingerprint.Compute(Fixture("twin-other.jpg"), orientation: 1);

        Assert.True(Distance(original, other) > Twins.MaxDistance, $"only {Distance(original, other)} bits apart");
    }

    [Fact]
    public void The_orientation_is_applied_before_comparing()
    {
        // Without its rotation the original is the copy turned on its side.
        var unturned = Fingerprint.Compute(Fixture("twin-original.jpg"), orientation: 1);
        var upright = Fingerprint.Compute(Fixture("twin-upright.jpg"), orientation: 1);

        Assert.True(Distance(unturned, upright) > Twins.MaxDistance, $"only {Distance(unturned, upright)} bits apart");
    }
}
