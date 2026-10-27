namespace GhostGPT.Services;
public static class ConnectionPresets
{
    public const string WellflowEndpoint = "https://api.wellflow.dev/v1";
    public const string WellflowModel = "gpt-5.6-sol";
    public static ProviderOptions CorrectKnownMismatch(ProviderOptions current)
    {
        // Older builds defaulted to OpenAI even when a Wellflow key was entered.
        if (current.Key.StartsWith("wf_", StringComparison.Ordinal) &&
            Uri.TryCreate(current.Endpoint, UriKind.Absolute, out var endpoint) &&
            endpoint.Scheme == "https" && endpoint.Host.Equals("api.openai.com", StringComparison.OrdinalIgnoreCase) && endpoint.IsDefaultPort)
            return current with { Endpoint = WellflowEndpoint };
        return current;
    }
    public static ProviderOptions Wellflow(ProviderOptions current)
    {
        // Never carry a credential to a different provider when applying a preset.
        bool sameOrigin = Uri.TryCreate(current.Endpoint, UriKind.Absolute, out var uri) &&
            uri.Scheme == Uri.UriSchemeHttps && uri.Host.Equals("api.wellflow.dev", StringComparison.OrdinalIgnoreCase) && uri.IsDefaultPort;
        return new(WellflowEndpoint, sameOrigin ? current.Key : "", WellflowModel);
    }
}
