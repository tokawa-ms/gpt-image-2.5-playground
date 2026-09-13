using System.Globalization;
using System.Resources;

namespace GptImagePlayground.Core;

public static class Text
{
    private static readonly ResourceManager Resources = new(
        "GptImagePlayground.Core.Strings", typeof(Text).Assembly);

    public static string ResolveLanguage(string? language) =>
        language?.Split('-')[0].Equals("ja", StringComparison.OrdinalIgnoreCase) == true
            ? "ja-JP" : "en-US";

    public static string Get(string key, params object?[] arguments)
    {
        var value = Resources.GetString(key, CultureInfo.CurrentUICulture)
            ?? throw new MissingManifestResourceException($"Missing UI resource: {key}");
        return arguments.Length == 0 ? value : string.Format(CultureInfo.CurrentCulture, value, arguments);
    }
}
