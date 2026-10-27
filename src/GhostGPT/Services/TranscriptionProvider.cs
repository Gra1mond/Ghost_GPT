using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
namespace GhostGPT.Services;
public interface ITranscriptionProvider
{
    Task<string> TranscribeAsync(ProviderOptions options, byte[] wav, CancellationToken cancellationToken);
}
public sealed class TranscriptionProvider(HttpClient client) : ITranscriptionProvider
{
    public async Task<string> TranscribeAsync(ProviderOptions options, byte[] wav, CancellationToken cancellationToken)
    {
        CompatibleProvider.Validate(options);
        using var request = new HttpRequestMessage(HttpMethod.Post, CompatibleProvider.Endpoint(options.Endpoint, "audio/transcriptions"));
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", options.Key);
        using var form = new MultipartFormDataContent();
        var file = new ByteArrayContent(wav); file.Headers.ContentType = new MediaTypeHeaderValue("audio/wav");
        form.Add(file, "file", "speech.wav"); form.Add(new StringContent(options.Model), "model"); form.Add(new StringContent("json"), "response_format");
        request.Content = form;
        using var response = await client.SendAsync(request, cancellationToken);
        CompatibleProvider.EnsureSuccess(response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
        if (!json.RootElement.TryGetProperty("text", out var text) || text.ValueKind != JsonValueKind.String) throw new ProviderException("Провайдер не вернул текст транскрипции.");
        return text.GetString()!.Trim();
    }
}
