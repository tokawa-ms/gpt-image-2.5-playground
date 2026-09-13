using Azure.Core;
using Azure.Identity;
using System.Collections.Concurrent;

namespace GptImagePlayground.Core;

public sealed class DeveloperCredentialProvider
{
    public static IReadOnlyList<string> Sources { get; } = ["AzureCli", "VisualStudio"];
    private readonly ConcurrentDictionary<string, TokenCredential> _credentials = new();

    public TokenCredential GetCredential(AppSettings settings) =>
        _credentials.GetOrAdd(settings.CredentialSource, source => new DefaultAzureCredential(CreateOptions(source)));

    public static DefaultAzureCredentialOptions CreateOptions(string source)
    {
        if (!Sources.Contains(source)) throw new AppException(Text.Get("InvalidCredentialSource"));
        // Do not silently authenticate through another developer account or tenant.
        return new DefaultAzureCredentialOptions
        {
            ExcludeEnvironmentCredential = true,
            ExcludeWorkloadIdentityCredential = true,
            ExcludeManagedIdentityCredential = true,
            ExcludeInteractiveBrowserCredential = true,
            ExcludeBrokerCredential = true,
            ExcludeVisualStudioCodeCredential = true,
            ExcludeAzurePowerShellCredential = true,
            ExcludeAzureDeveloperCliCredential = true,
            ExcludeAzureCliCredential = source != "AzureCli",
            ExcludeVisualStudioCredential = source != "VisualStudio"
        };
    }
}
