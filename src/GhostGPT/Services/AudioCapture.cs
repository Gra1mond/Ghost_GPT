using System.IO;
using NAudio.CoreAudioApi;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;
namespace GhostGPT.Services;
public record AudioDevice(string Id, string Name);
public sealed class AudioCapture : IDisposable
{
    private readonly List<Source> sources = [];
    public event Action<AudioChunk>? Chunk;
    public event Action? DeviceFailed;
    public static List<AudioDevice> Devices(DataFlow flow)
    {
        using var enumerator = new MMDeviceEnumerator();
        var list = new List<AudioDevice> { new("", "Не записывать") };
        foreach (var device in enumerator.EnumerateAudioEndPoints(flow, DeviceState.Active))
            using (device) list.Add(new(device.ID, device.FriendlyName));
        return list;
    }
    public void Start(string microphone, string output)
    {
        if (microphone.Length == 0 && output.Length == 0) throw new ProviderException("Выберите хотя бы одно устройство записи.");
        try { if (microphone.Length > 0) Add(microphone, false); if (output.Length > 0) Add(output, true); }
        catch { Dispose(); throw; }
    }
    private void Add(string id, bool loopback)
    {
        using var enumerator = new MMDeviceEnumerator();
        var device = enumerator.GetDevice(id);
        WasapiCapture capture;
        try { capture = loopback ? new WasapiLoopbackCapture(device) : new WasapiCapture(device); }
        catch { device.Dispose(); throw; }
        var source = new Source(device, capture, loopback ? "Звук компьютера" : "Микрофон", c => Chunk?.Invoke(c), () => DeviceFailed?.Invoke());
        sources.Add(source); capture.StartRecording();
    }
    public void Dispose() { foreach (var source in sources) source.Dispose(); sources.Clear(); }
    private sealed class Source : IDisposable
    {
        private readonly MMDevice device;
        private readonly WasapiCapture capture;
        private readonly string name;
        private readonly Action<AudioChunk> emit;
        private readonly Action failed;
        private readonly object gate = new();
        private readonly float[] samples;
        private int count, quiet;
        private bool voiced, disposed;
        private DateTimeOffset started;
        public Source(MMDevice device, WasapiCapture capture, string name, Action<AudioChunk> emit, Action failed)
        {
            this.device = device; this.capture = capture; this.name = name; this.emit = emit; this.failed = failed;
            samples = new float[capture.WaveFormat.SampleRate * 6];
            capture.DataAvailable += Data;
            capture.RecordingStopped += Stopped;
        }
        private void Stopped(object? sender, StoppedEventArgs args) { if (!disposed && args.Exception is not null) failed(); }
        private void Data(object? sender, WaveInEventArgs args)
        {
            lock (gate)
            {
                if (disposed) return;
                try
                {
                    using var raw = new RawSourceWaveStream(args.Buffer, 0, args.BytesRecorded, capture.WaveFormat);
                    var provider = raw.ToSampleProvider();
                    var block = new float[args.BytesRecorded / Math.Max(1, capture.WaveFormat.BitsPerSample / 8)];
                    int read = provider.Read(block, 0, block.Length), channels = provider.WaveFormat.Channels;
                    for (int i = 0; i + channels <= read; i += channels)
                    {
                        float value = 0; for (int c = 0; c < channels; c++) value += block[i+c] / channels;
                        if (count == 0) started = DateTimeOffset.UtcNow;
                        samples[count++] = value;
                        if (Math.Abs(value) > .015f) { voiced = true; quiet = 0; } else quiet++;
                        if (count == samples.Length || (voiced && quiet > provider.WaveFormat.SampleRate * .6 && count > provider.WaveFormat.SampleRate * .8)) Flush();
                    }
                    Array.Clear(block);
                }
                catch { failed(); }
            }
        }
        private void Flush()
        {
            if (voiced)
            {
                var source = new Samples(samples, count, capture.WaveFormat.SampleRate);
                var resampled = new WdlResamplingSampleProvider(source, 16000);
                var pcm = new SampleToWaveProvider16(resampled);
                using var stream = new MemoryStream();
                using (var writer = new WaveFileWriter(new NAudio.Utils.IgnoreDisposeStream(stream), pcm.WaveFormat))
                {
                    var buffer = new byte[32000]; int n;
                    while ((n = pcm.Read(buffer, 0, buffer.Length)) > 0) writer.Write(buffer, 0, n);
                    Array.Clear(buffer);
                }
                emit(new(name, started, stream.ToArray()));
                if (stream.TryGetBuffer(out var memory)) Array.Clear(memory.Array!, memory.Offset, memory.Count);
            }
            Array.Clear(samples); count = quiet = 0; voiced = false;
        }
        public void Dispose()
        {
            lock (gate) { disposed = true; Array.Clear(samples); }
            capture.DataAvailable -= Data; capture.RecordingStopped -= Stopped;
            try { capture.StopRecording(); } finally { capture.Dispose(); device.Dispose(); }
        }
    }
    private sealed class Samples(float[] data, int length, int rate) : ISampleProvider
    {
        private int position;
        public WaveFormat WaveFormat { get; } = WaveFormat.CreateIeeeFloatWaveFormat(rate, 1);
        public int Read(float[] buffer, int offset, int count) { int n = Math.Min(count, length-position); Array.Copy(data, position, buffer, offset, n); position += n; return n; }
    }
}
