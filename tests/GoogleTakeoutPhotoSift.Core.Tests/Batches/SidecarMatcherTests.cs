using Tiesmaster.GoogleTakeoutPhotoSift.Core.Batches;

namespace Tiesmaster.GoogleTakeoutPhotoSift.Core.Tests.Batches;

public class SidecarMatcherTests
{
    private static string? SidecarOf(string media, params string[] otherNames) =>
        SidecarMatcher.Match([media, .. otherNames])[media];

    [Fact]
    public void Plain_supplemental_metadata_name()
    {
        Assert.Equal("IMG_1.jpg.supplemental-metadata.json", SidecarOf("IMG_1.jpg", "IMG_1.jpg.supplemental-metadata.json"));
    }

    [Fact]
    public void Old_style_name_without_supplemental()
    {
        Assert.Equal("IMG_1.jpg.json", SidecarOf("IMG_1.jpg", "IMG_1.jpg.json"));
    }

    [Theory]
    [InlineData("IMG_1.jpg.supplemental-me.json")]
    [InlineData("IMG_1.jpg.supp.json")]
    [InlineData("IMG_1.jpg.s.json")]
    public void Cut_off_supplemental_part(string json)
    {
        Assert.Equal(json, SidecarOf("IMG_1.jpg", json));
    }

    [Fact]
    public void Empty_supplemental_part_does_not_match()
    {
        // As in Python: the trailing dot stays in the prefix, so "img_1.jpg" doesn't start with it.
        Assert.Null(SidecarOf("IMG_1.jpg", "IMG_1.jpg..json"));
    }

    [Fact]
    public void Cut_off_media_name_matches_by_prefix()
    {
        const string media = "PXL_20160812_154512345.PORTRAIT-01.COVER.jpg";
        const string json = "PXL_20160812_154512345.PORTRAIT-01.COVER.j.json";
        Assert.Equal(json, SidecarOf(media, json));
    }

    [Fact]
    public void Numbered_copy_takes_the_numbered_sidecar()
    {
        var result = SidecarMatcher.Match([
            "IMG.jpg", "IMG(1).jpg",
            "IMG.jpg.supplemental-metadata.json", "IMG.jpg.supplemental-metadata(1).json",
        ]);
        Assert.Equal("IMG.jpg.supplemental-metadata(1).json", result["IMG(1).jpg"]);
        Assert.Equal("IMG.jpg.supplemental-metadata.json", result["IMG.jpg"]);
    }

    [Fact]
    public void Numbered_copy_falls_back_to_the_unnumbered_sidecar()
    {
        Assert.Equal("IMG.jpg.supplemental-metadata.json", SidecarOf("IMG(1).jpg", "IMG.jpg.supplemental-metadata.json"));
    }

    [Fact]
    public void Unnumbered_photo_does_not_take_a_numbered_sidecar()
    {
        Assert.Null(SidecarOf("IMG.jpg", "IMG.jpg.supplemental-metadata(1).json"));
    }

    [Theory]
    [InlineData("IMG_1-edited.jpg")]
    [InlineData("IMG_1-bewerkt.jpg")]
    [InlineData("IMG_1-EDITED.jpg")]
    public void Edited_copy_shares_the_original_sidecar(string edited)
    {
        var result = SidecarMatcher.Match(["IMG_1.jpg", edited, "IMG_1.jpg.supplemental-metadata.json"]);
        Assert.Equal("IMG_1.jpg.supplemental-metadata.json", result[edited]);
        Assert.Equal("IMG_1.jpg.supplemental-metadata.json", result["IMG_1.jpg"]);
    }

    [Fact]
    public void Numbered_edited_copy_takes_the_numbered_sidecar()
    {
        Assert.Equal("IMG.jpg.supplemental-metadata(1).json",
            SidecarOf("IMG-edited(1).jpg", "IMG.jpg.supplemental-metadata(1).json"));
    }

    [Fact]
    public void Longest_prefix_wins()
    {
        Assert.Equal("IMG_10.jpg.supplemental-metadata.json",
            SidecarOf("IMG_10.jpg", "IMG_1.json", "IMG_10.jpg.supplemental-metadata.json"));
    }

    [Fact]
    public void Matching_ignores_case()
    {
        Assert.Equal("img_1.JPG.supplemental-metadata.json", SidecarOf("IMG_1.jpg", "img_1.JPG.supplemental-metadata.json"));
    }

    [Fact]
    public void Album_metadata_is_never_a_sidecar()
    {
        Assert.Null(SidecarOf("metadata.jpg", "metadata.json"));
    }

    [Fact]
    public void Media_without_sidecar_maps_to_null_and_json_files_get_no_entry()
    {
        var result = SidecarMatcher.Match(["IMG_1.jpg", "other.jpg.supplemental-metadata.json"]);
        Assert.Null(result["IMG_1.jpg"]);
        Assert.Single(result);
    }

    [Theory]
    [InlineData("IMG.jpg", "IMG", ".jpg")]
    [InlineData("archive.tar.gz", "archive.tar", ".gz")]
    [InlineData(".hidden", ".hidden", "")]
    [InlineData("..jpg", "..jpg", "")]
    [InlineData("noext", "noext", "")]
    public void SplitExtension_matches_python(string name, string stem, string ext)
    {
        Assert.Equal((stem, ext), SidecarMatcher.SplitExtension(name));
    }
}
