namespace GhostGPT.Services;
public sealed record ContextSnapshot(string Summary, string Older, string Recent, long Cutoff);
public sealed class ConversationContext
{
    private readonly List<(long Id, string Text)> recent = [], older = [];
    private long sequence;
    public string Summary { get; private set; } = "";
    public bool LostOlderContext { get; private set; }
    public bool HasContent => recent.Count > 0 || older.Count > 0 || Summary.Length > 0;
    public void Add(string source, string text)
    {
        text = text.Length > 8000 ? text[..8000] : text;
        recent.Add((++sequence, $"{source}: {text}"));
        while (recent.Sum(x => x.Text.Length) > 12000 && recent.Count > 1) { older.Add(recent[0]); recent.RemoveAt(0); }
        while (older.Sum(x => x.Text.Length) > 24000) { older.RemoveAt(0); LostOlderContext = true; }
    }
    public ContextSnapshot Snapshot() => new(Summary, string.Join("\n", older.Select(x => x.Text)), string.Join("\n", recent.Select(x => x.Text)), older.Count == 0 ? 0 : older[^1].Id);
    public void CommitSummary(ContextSnapshot snapshot, string summary)
    {
        Summary = summary.Length > 4000 ? summary[..4000] : summary;
        older.RemoveAll(x => x.Id <= snapshot.Cutoff);
    }
    public void Clear() { recent.Clear(); older.Clear(); Summary = ""; LostOlderContext = false; }
}
