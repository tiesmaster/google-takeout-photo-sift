using Tiesmaster.GoogleTakeoutPhotoSift.Core.Batches;

namespace Tiesmaster.GoogleTakeoutPhotoSift.Core.Tests.Batches;

public sealed class SidecarTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("photo-sift-sidecar").FullName;

    private string Json(string content)
    {
        var path = Path.Combine(_dir, Guid.NewGuid() + ".json");
        File.WriteAllText(path, content);
        return path;
    }

    [Theory]
    [InlineData("""{"photoTakenTime": {"timestamp": "1462100000", "formatted": "x"}}""", 1462100000L)]
    [InlineData("""{"photoTakenTime": {"timestamp": 1462100000}}""", 1462100000L)]
    [InlineData("""{"photoTakenTime": {"timestamp": "0"}}""", null)]
    [InlineData("""{"photoTakenTime": {"timestamp": "-5"}}""", null)]
    [InlineData("""{"photoTakenTime": {"timestamp": "soon"}}""", null)]
    [InlineData("""{"creationTime": {"timestamp": "1462100000"}}""", null)]
    [InlineData("""{"photoTakenTime": "1462100000"}""", null)]
    [InlineData("not json", null)]
    public void ReadTakenTime(string content, long? expected)
    {
        Assert.Equal(expected, Sidecar.ReadTakenTime(Json(content)));
    }

    [Fact]
    public void ReadTakenTime_of_missing_file_is_null()
    {
        Assert.Null(Sidecar.ReadTakenTime(Path.Combine(_dir, "missing.json")));
    }

    [Fact]
    public void ReadAlbumTitle()
    {
        Assert.Equal("DVR: Drakenbootfestival [2013]", Sidecar.ReadAlbumTitle(Json("""{"title": "DVR: Drakenbootfestival [2013]"}""")));
        Assert.Null(Sidecar.ReadAlbumTitle(Json("""{"description": ""}""")));
    }

    public void Dispose() => Directory.Delete(_dir, recursive: true);
}
