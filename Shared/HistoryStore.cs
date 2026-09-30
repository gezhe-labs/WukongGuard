using System.Text.Json;

namespace RegretPill.Shared;

internal static class HistoryStore
{
    internal static string? SmokeDirectory { get; set; }
    internal sealed class Entry
    {
        public string Id { get; set; } = "";
        public DateTime SeenAt { get; set; }
        public string[] Levels { get; set; } = Array.Empty<string>();

        public override string ToString() => $"{SeenAt:MM-dd HH:mm}  {Levels.FirstOrDefault() ?? Id}";
    }

    private static string PathToHistory => Path.Combine(SmokeDirectory ?? Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "WukongGuard"), "history.jsonl");

    internal static void Add(string id, string[] levels)
    {
        var entry = new Entry { Id = id, SeenAt = DateTime.Now, Levels = (string[])levels.Clone() };
        Directory.CreateDirectory(Path.GetDirectoryName(PathToHistory)!);
        File.AppendAllText(PathToHistory, JsonSerializer.Serialize(entry) + Environment.NewLine);
        var file = new FileInfo(PathToHistory);
        if (file.Length > 512 * 1024)
            File.WriteAllLines(PathToHistory, Load().Take(100).Reverse()
                .Select(entry => JsonSerializer.Serialize(entry)));
    }

    internal static IReadOnlyList<Entry> Load()
    {
        if (!File.Exists(PathToHistory)) return Array.Empty<Entry>();
        var entries = new List<Entry>();
        foreach (var line in File.ReadLines(PathToHistory).TakeLast(100).Reverse())
        {
            try
            {
                var entry = JsonSerializer.Deserialize<Entry>(line);
                if (entry?.Levels is { Length: 4 }) entries.Add(entry);
            }
            catch (JsonException) { }
        }
        return entries;
    }

    internal static void Clear() => File.Delete(PathToHistory);
}
