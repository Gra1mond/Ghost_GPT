using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
namespace GhostGPT.Services;
public record ProviderOptions(string Endpoint, string Key, string Model);
public record ChatTurn(string Role, string Text, string? ImageDataUrl = null);
public interface IChatProvider
{
    IAsyncEnumerable<string> StreamAsync(ProviderOptions options, IReadOnlyList<ChatTurn> turns, CancellationToken cancellationToken);
}
public interface IVisionProvider : IChatProvider { }
public sealed class ProviderException(string message) : Exception(message);
public sealed class CompatibleProvider(HttpClient client) : IChatProvider, IVisionProvider
{
    public static Uri Endpoint(string address, string route)
    {
        if (!Uri.TryCreate(address.TrimEnd('/') + "/", UriKind.Absolute, out var uri) ||
            (uri.Scheme != "https" && !(uri.Scheme == "http" && uri.IsLoopback)) ||
            !string.IsNullOrEmpty(uri.UserInfo) || !string.IsNullOrEmpty(uri.Query) || !string.IsNullOrEmpty(uri.Fragment))
            throw new ProviderException("Введите HTTPS-адрес API, например https://api.openai.com/v1. HTTP разрешён только для localhost.");
        return new Uri(uri, route);
    }
    public static void Validate(ProviderOptions options)
    {
        Endpoint(options.Endpoint, "chat/completions");
        if (string.IsNullOrWhiteSpace(options.Key)) throw new ProviderException("Введите API-ключ в настройках.");
        if (options.Key.Any(char.IsControl)) throw new ProviderException("API-ключ содержит недопустимые символы.");
        if (string.IsNullOrWhiteSpace(options.Model)) throw new ProviderException("Укажите идентификатор модели в настройках.");
    }
    public static void EnsureSuccess(HttpStatusCode status)
    {
        if ((int)status is >= 200 and < 300) return;
        throw new ProviderException(status switch
        {
            HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden => "Провайдер отклонил ключ или доступ к модели (401/403).",
            HttpStatusCode.TooManyRequests => "Лимит запросов или баланса (429). Повторите позже.",
            HttpStatusCode.BadRequest or HttpStatusCode.UnprocessableEntity => "Провайдер отклонил запрос. Проверьте модель и поддержку изображений/формата API.",
            HttpStatusCode.NotFound => "Модель или путь API не найдены (404). Проверьте настройки.",
            _ => $"Ошибка провайдера ({(int)status}). Попробуйте позже."
        });
    }
    public async IAsyncEnumerable<string> StreamAsync(ProviderOptions options, IReadOnlyList<ChatTurn> turns, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        Validate(options);
        using var request = new HttpRequestMessage(HttpMethod.Post, Endpoint(options.Endpoint, "chat/completions"));
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", options.Key);
        var messages = turns.Select(t => new { role = t.Role, content = t.ImageDataUrl is null ? (object)t.Text : new object[] { new { type = "text", text = t.Text }, new { type = "image_url", image_url = new { url = t.ImageDataUrl } } } });
        request.Content = new StringContent(JsonSerializer.Serialize(new { model = options.Model, messages, stream = true }), Encoding.UTF8, "application/json");
        using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        EnsureSuccess(response.StatusCode);
        using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var reader = new StreamReader(stream);
        bool finished = false;
        while (await reader.ReadLineAsync(cancellationToken) is { } line)
        {
            if (!line.StartsWith("data:", StringComparison.Ordinal)) continue;
            var data = line[5..].Trim();
            if (data == "[DONE]") yield break;
            if (data.Length == 0) continue;
            using var doc = JsonDocument.Parse(data);
            if (doc.RootElement.TryGetProperty("error", out _)) throw new ProviderException("Провайдер прервал ответ с ошибкой.");
            if (!doc.RootElement.TryGetProperty("choices", out var choices) || choices.GetArrayLength() == 0) continue;
            var choice = choices[0];
            if (choice.TryGetProperty("finish_reason", out var reason) && reason.ValueKind == JsonValueKind.String) finished = true;
            if (choice.TryGetProperty("delta", out var delta) && delta.TryGetProperty("content", out var content) && content.ValueKind == JsonValueKind.String)
                yield return content.GetString()!;
        }
        if (!finished) throw new ProviderException("Соединение оборвалось до завершения ответа. Повторите запрос.");
    }
    public async Task CheckAsync(ProviderOptions options, CancellationToken cancellationToken)
    {
        Validate(options);
        using var request = new HttpRequestMessage(HttpMethod.Get, Endpoint(options.Endpoint, "models"));
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", options.Key);
        using var response = await client.SendAsync(request, cancellationToken);
        EnsureSuccess(response.StatusCode);
    }
}
