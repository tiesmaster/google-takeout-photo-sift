using System.Text.Json;
using System.Text.Json.Serialization;

namespace Tiesmaster.GoogleTakeoutPhotoSift.Core.Files;

/// <summary>
/// An append-only log of deletes and undos in <c>batches\deleted\&lt;batch&gt;\sift-journal.jsonl</c>:
/// an audit trail, and what lets undo work after a restart (decision 8).
/// </summary>
public sealed class DeleteJournal(string path)
{
    public sealed record Entry(
        string Op,
        string Id,
        DateTimeOffset Time,
        string? Photo = null,
        IReadOnlyList<MoveRecord>? Records = null);

    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    private readonly Lock _lock = new();

    public string Path { get; } = path;

    public void AppendDelete(string id, string photo, IReadOnlyList<MoveRecord> records) =>
        Append(new Entry("delete", id, DateTimeOffset.Now, photo, records));

    public void AppendUndo(string id) => Append(new Entry("undo", id, DateTimeOffset.Now));

    /// <summary>The deletes that weren't undone, oldest first. Unreadable lines are skipped.</summary>
    public List<Entry> PendingDeletes()
    {
        if (!File.Exists(Path))
            return [];
        var deletes = new List<Entry>();
        var undone = new HashSet<string>();
        foreach (var line in File.ReadLines(Path))
        {
            if (string.IsNullOrWhiteSpace(line))
                continue;
            Entry? entry;
            try
            {
                entry = JsonSerializer.Deserialize<Entry>(line, Json);
            }
            catch (JsonException)
            {
                continue;
            }
            if (entry is { Op: "delete", Records: not null })
                deletes.Add(entry);
            else if (entry is { Op: "undo" })
                undone.Add(entry.Id);
        }
        return deletes.Where(d => !undone.Contains(d.Id)).ToList();
    }

    private void Append(Entry entry)
    {
        lock (_lock)
        {
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Path)!);
            using var stream = new FileStream(Path, FileMode.Append, FileAccess.Write, FileShare.Read);
            using var writer = new StreamWriter(stream);
            writer.WriteLine(JsonSerializer.Serialize(entry, Json));
        }
    }
}
