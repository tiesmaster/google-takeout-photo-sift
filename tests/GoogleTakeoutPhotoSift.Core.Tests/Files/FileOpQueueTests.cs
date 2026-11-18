using Tiesmaster.GoogleTakeoutPhotoSift.Core.Exif;
using Tiesmaster.GoogleTakeoutPhotoSift.Core.Files;

namespace Tiesmaster.GoogleTakeoutPhotoSift.Core.Tests.Files;

public class FileOpQueueTests
{
    [Fact]
    public void Ops_run_one_at_a_time_in_order_and_report_errors()
    {
        var log = new List<string>();
        using var queue = new FileOpQueue(() => throw new InvalidOperationException("no exiftool"), (op, error) =>
        {
            lock (log)
                log.Add($"{op.Tag}:{error ?? "ok"}");
        });
        for (var i = 0; i < 5; i++)
        {
            var n = i;
            queue.Enqueue(new WorkOp(n, () =>
            {
                Thread.Sleep(5);
                return n == 3 ? "broken" : null;
            }));
        }

        Assert.True(queue.WaitForIdle(TimeSpan.FromSeconds(10)));
        Assert.Equal(["0:ok", "1:ok", "2:ok", "3:broken", "4:ok"], log);
        Assert.Equal(0, queue.Pending);
    }

    [Fact]
    public void A_rotation_without_exiftool_fails_with_a_message()
    {
        string? error = null;
        using var queue = new FileOpQueue(() => throw new InvalidOperationException("Could not start ExifTool"), (_, e) => error = e);

        queue.Rotate("p", ["x.jpg"], 6);

        Assert.True(queue.WaitForIdle(TimeSpan.FromSeconds(10)));
        Assert.Equal("Could not start ExifTool", error);
    }

    [Fact]
    public void Dispose_runs_what_is_queued_first()
    {
        var ran = 0;
        var queue = new FileOpQueue(() => throw new InvalidOperationException(), (_, _) => Interlocked.Increment(ref ran));
        for (var i = 0; i < 3; i++)
        {
            queue.Enqueue(new WorkOp(i, () =>
            {
                Thread.Sleep(20);
                return null;
            }));
        }

        queue.Dispose();

        Assert.Equal(3, ran);
    }

    [Theory]
    [InlineData(new[] { "    1 image files updated" }, new string[0], 1, true)]
    [InlineData(new[] { "    2 image files updated", "    1 image files unchanged" }, new string[0], 3, true)]
    [InlineData(new[] { "    1 image files updated", "    1 files weren't updated due to errors" }, new[] { "Error: File not found - x.jpg" }, 2, false)]
    [InlineData(new[] { "    0 image files updated" }, new[] { "Error: [minor] whatever" }, 1, false)]
    [InlineData(new[] { "    1 image files updated" }, new[] { "Warning: [minor] Ignored empty rational value" }, 1, true)]
    public void CheckWrite(string[] output, string[] errors, int files, bool ok)
    {
        var error = ExifToolProcess.CheckWrite(new ExifToolResult(output, errors), files);
        Assert.Equal(ok, error is null);
    }
}
