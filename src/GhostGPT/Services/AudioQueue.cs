using System.Security.Cryptography;
using System.Threading.Channels;
namespace GhostGPT.Services;
public sealed record AudioChunk(string Source, DateTimeOffset Time, byte[] Data) : IDisposable
{
    public void Dispose() => CryptographicOperations.ZeroMemory(Data);
}
public sealed class AudioQueue(int capacity) : IDisposable
{
    private readonly Channel<AudioChunk> channel = Channel.CreateBounded<AudioChunk>(new BoundedChannelOptions(capacity) { FullMode = BoundedChannelFullMode.Wait, SingleReader = true, SingleWriter = false });
    public bool TryAdd(AudioChunk chunk)
    {
        if (channel.Writer.TryWrite(chunk)) return true;
        chunk.Dispose(); return false;
    }
    public IAsyncEnumerable<AudioChunk> ReadAsync(CancellationToken token) => channel.Reader.ReadAllAsync(token);
    public void Dispose() { channel.Writer.TryComplete(); while (channel.Reader.TryRead(out var chunk)) chunk.Dispose(); }
}
