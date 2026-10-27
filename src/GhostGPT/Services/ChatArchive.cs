using System.IO;
using System.Text.Json;
namespace GhostGPT.Services;
public sealed class SavedChat
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Title { get; set; } = "Новый чат";
    public string? Folder { get; set; }
    public DateTime Updated { get; set; } = DateTime.Now;
    public List<ChatTurn> Turns { get; set; } = [];
}
public sealed class ChatArchive(string directory)
{
    private string FilePath => Path.Combine(directory, "chats.protected");
    public List<SavedChat> Load() => File.Exists(FilePath)
        ? JsonSerializer.Deserialize<List<SavedChat>>(SecretStore.Unprotect(File.ReadAllText(FilePath))) ?? [] : [];
    public void Save(IEnumerable<SavedChat> chats)
    {
        Directory.CreateDirectory(directory);
        File.WriteAllText(FilePath + ".tmp", SecretStore.Protect(JsonSerializer.Serialize(chats)));
        File.Move(FilePath + ".tmp", FilePath, true);
    }
}
