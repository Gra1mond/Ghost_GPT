namespace GhostGPT.Services;
public static class ConnectionPresets
{
    public const string WellflowEndpoint = "https://api.wellflow.dev/v1";
    public const string WellflowModel = "gpt-5.6-sol";
    public static ProviderOptions Wellflow(ProviderOptions current)
    {
        // Never carry a credential to a different provider when applying a preset.
        bool sameOrigin = Uri.TryCreate(current.Endpoint, UriKind.Absolute, out var uri) &&
            uri.Scheme == Uri.UriSchemeHttps && uri.Host.Equals("api.wellflow.dev", StringComparison.OrdinalIgnoreCase) && uri.IsDefaultPort;
        return new(WellflowEndpoint, sameOrigin ? current.Key : "", WellflowModel);
    }
}
