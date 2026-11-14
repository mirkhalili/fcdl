using System.IO;
using System.Text.Json;

namespace JacquardStudio;

public sealed record WorkflowPresetInfo(string Id, string Name, DateTimeOffset SavedAt);

/// <summary>Stores named workflow graphs locally in the current Windows user's application data folder.</summary>
public sealed class WorkflowPresetStore
{
    private const int Version = 1;
    public const int MaxPresets = 32;
    private const int MaxIndexBytes = 64 * 1024;
    private readonly string folder;

    private sealed class IndexDocument
    {
        public IndexDocument() { }
        public int Version { get; set; } = WorkflowPresetStore.Version;
        public List<Entry> Presets { get; set; } = new();
    }
    private sealed class Entry
    {
        public Entry() { }
        public string Id { get; set; } = "";
        public string Name { get; set; } = "";
        public DateTimeOffset SavedAt { get; set; }
    }

    private string IndexPath => Path.Combine(folder, "index.json");
    private string GraphPath(string id) => Path.Combine(folder, id + ".sam");

    public WorkflowPresetStore() : this(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "JacquardStudio", "WorkflowPresets")) { }
    public WorkflowPresetStore(string storageFolder)
    {
        if(string.IsNullOrWhiteSpace(storageFolder))throw new ArgumentException("A local preset folder is required.",nameof(storageFolder));
        folder=Path.GetFullPath(storageFolder);
    }

    public IReadOnlyList<WorkflowPresetInfo> List() => ReadIndex().Presets
        .OrderByDescending(item => item.SavedAt)
        .Select(item => new WorkflowPresetInfo(item.Id, item.Name, item.SavedAt))
        .ToArray();

    public WorkflowPresetInfo Save(string name, ModuleGraph graph)
    {
        if (graph is null) throw new ArgumentNullException(nameof(graph));
        name = (name ?? "").Trim();
        if (name.Length is < 1 or > 80 || name.Any(char.IsControl)) throw new ArgumentException("نام زنجیره باید ۱ تا ۸۰ نویسه و بدون نویسهٔ کنترلی باشد.");
        if (graph.Nodes.Count == 0) throw new ArgumentException("برای ذخیرهٔ زنجیره، دست‌کم یک ماژول به بوم اضافه کنید.");
        graph.Validate();

        var index = ReadIndex();
        if (index.Presets.Count >= MaxPresets) throw new InvalidOperationException($"حداکثر {MaxPresets} زنجیرهٔ سفارشی قابل ذخیره است.");
        if (index.Presets.Any(item => string.Equals(item.Name, name, StringComparison.OrdinalIgnoreCase))) throw new ArgumentException("زنجیره‌ای با این نام از قبل ذخیره شده است.");

        string id = Guid.NewGuid().ToString("N");
        var entry = new Entry { Id = id, Name = name, SavedAt = DateTimeOffset.UtcNow };
        Directory.CreateDirectory(folder);
        ModuleProjectFile.Save(GraphPath(id), graph);
        index.Presets.Add(entry);
        try { WriteIndex(index); }
        catch
        {
            try { File.Delete(GraphPath(id)); } catch(Exception cleanup) when(cleanup is IOException or UnauthorizedAccessException) { }
            throw;
        }
        return new WorkflowPresetInfo(entry.Id, entry.Name, entry.SavedAt);
    }

    public ModuleGraph Load(string id)
    {
        var entry = Find(id);
        string path = GraphPath(entry.Id);
        if (!File.Exists(path)) throw new InvalidDataException("فایل این زنجیره پیدا نشد.");
        return ModuleProjectFile.Load(path);
    }

    public void Delete(string id)
    {
        var index = ReadIndex();
        var entry = index.Presets.SingleOrDefault(item => string.Equals(item.Id, id, StringComparison.Ordinal))
            ?? throw new ArgumentException("زنجیرهٔ انتخاب‌شده پیدا نشد.");
        index.Presets.Remove(entry);
        WriteIndex(index);
        string path = GraphPath(entry.Id);
        if (File.Exists(path)) File.Delete(path);
    }

    private Entry Find(string id)
    {
        var index = ReadIndex();
        return index.Presets.SingleOrDefault(item => string.Equals(item.Id, id, StringComparison.Ordinal))
            ?? throw new ArgumentException("زنجیرهٔ انتخاب‌شده پیدا نشد.");
    }

    private IndexDocument ReadIndex()
    {
        if (!File.Exists(IndexPath)) return new IndexDocument();
        var file = new FileInfo(IndexPath);
        if (file.Length > MaxIndexBytes) throw new InvalidDataException("فهرست زنجیره‌های سفارشی بیش از حد بزرگ است.");
        var index = JsonSerializer.Deserialize<IndexDocument>(File.ReadAllBytes(IndexPath))
            ?? throw new InvalidDataException("فهرست زنجیره‌های سفارشی نامعتبر است.");
        if (index.Version != Version || index.Presets is null || index.Presets.Count > MaxPresets ||
            index.Presets.Any(item => item is null || !Guid.TryParseExact(item.Id, "N", out _) || item.Name is null || item.Name.Length is < 1 or > 80 || item.Name.Any(char.IsControl)) ||
            index.Presets.Select(item => item.Id).Distinct(StringComparer.Ordinal).Count() != index.Presets.Count ||
            index.Presets.Select(item => item.Name).Distinct(StringComparer.OrdinalIgnoreCase).Count() != index.Presets.Count)
            throw new InvalidDataException("فهرست زنجیره‌های سفارشی نامعتبر است.");
        return index;
    }

    private void WriteIndex(IndexDocument index)
    {
        Directory.CreateDirectory(folder);
        byte[] json = JsonSerializer.SerializeToUtf8Bytes(index);
        if (json.Length > MaxIndexBytes) throw new InvalidDataException("فهرست زنجیره‌های سفارشی بیش از حد بزرگ است.");
        string temporary = IndexPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try { File.WriteAllBytes(temporary, json); File.Move(temporary, IndexPath, true); }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
}
