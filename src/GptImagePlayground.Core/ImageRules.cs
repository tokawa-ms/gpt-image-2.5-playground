using System.Globalization;

namespace GptImagePlayground.Core;

public static class ImageRules
{
    // Conservative decimal MB follows Azure's documented "less than 50 MB".
    public const long MaxReferenceBytes = 50_000_000;
    public const int MaxReferences = 16;
    public static IReadOnlyList<string> Qualities { get; } = ["auto", "low", "medium", "high", "xhigh", "max"];
    public static IReadOnlyList<string> Formats { get; } = ["png", "jpeg", "webp"];
    public static IReadOnlyList<string> Backgrounds { get; } = ["auto", "opaque", "transparent"];
    public static IReadOnlyList<string> Sizes { get; } = ["auto", "1024x1024", "1536x1024", "1024x1536", "1536x864", "864x1536", "3840x2160", "2160x3840"];

    public static (int Width, int Height)? ValidateSize(string size)
    {
        if (string.IsNullOrEmpty(size)) throw new AppException(Text.Get("EmptySize"));
        if (size == "auto") return null;
        var parts = size.Split('x');
        if (parts.Length != 2 ||
            !int.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out var width) ||
            !int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out var height) ||
            width <= 0 || height <= 0 || width > 3840 || height > 3840 ||
            width % 16 != 0 || height % 16 != 0 ||
            width > 3L * height || height > 3L * width ||
            (long)width * height < 655360 || (long)width * height > 8294400)
            throw new AppException(Text.Get("InvalidSize"));
        return (width, height);
    }

    public static bool IsExperimental(string size)
    {
        var dimensions = ValidateSize(size);
        return dimensions is { } d && (long)d.Width * d.Height > 2560L * 1440;
    }

    public static Uri EditUri(AppSettings settings) => ImageUri(settings, true);

    public static Uri GenerationUri(AppSettings settings) => ImageUri(settings, false);

    private static Uri ImageUri(AppSettings settings, bool hasReferences)
    {
        if (!Uri.TryCreate(settings.Endpoint.Trim(), UriKind.Absolute, out var uri) ||
            uri.Scheme != Uri.UriSchemeHttps || !uri.IsDefaultPort ||
            !string.IsNullOrEmpty(uri.UserInfo) || !string.IsNullOrEmpty(uri.Query) ||
            !string.IsNullOrEmpty(uri.Fragment) ||
            !(uri.Host.EndsWith(".openai.azure.com", StringComparison.OrdinalIgnoreCase) ||
              uri.Host.EndsWith(".services.ai.azure.com", StringComparison.OrdinalIgnoreCase) ||
              uri.Host.EndsWith(".cognitiveservices.azure.com", StringComparison.OrdinalIgnoreCase)))
            throw new AppException(Text.Get("InvalidEndpoint"));
        var path = uri.AbsolutePath.TrimEnd('/');
        if (path is not ("" or "/openai/v1" or "/openai/v1/images/generations" or "/openai/v1/images/edits"))
            throw new AppException(Text.Get("InvalidEndpointPath"));
        if (string.IsNullOrWhiteSpace(settings.Deployment) || settings.Deployment.Length > 128 ||
            settings.Deployment.Any(c => !char.IsAsciiLetterOrDigit(c) && c is not '-' and not '_' and not '.'))
            throw new AppException(Text.Get("InvalidDeployment"));
        return new Uri(uri, hasReferences ? "/openai/v1/images/edits" : "/openai/v1/images/generations");
    }

    public static void ValidateSettings(AppSettings settings, bool allowEmptyEndpoint = false)
    {
        if (!DeveloperCredentialProvider.Sources.Contains(settings.CredentialSource))
            throw new AppException(Text.Get("InvalidCredentialSource"));
        if (!allowEmptyEndpoint || settings.Endpoint.Length != 0) _ = EditUri(settings);
        if (string.IsNullOrWhiteSpace(settings.Deployment)) throw new AppException(Text.Get("EmptyDeployment"));
        ValidateSize(settings.DefaultSize);
        if (!Qualities.Contains(settings.DefaultQuality)) throw new AppException(Text.Get("InvalidDefaultQuality"));
    }

    public static void Validate(GenerationRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Prompt) || request.Prompt.Length > 32000)
            throw new AppException(Text.Get("InvalidPrompt"));
        ValidateSize(request.Size);
        if (!Qualities.Contains(request.Quality) || !Formats.Contains(request.OutputFormat) || !Backgrounds.Contains(request.Background))
            throw new AppException(Text.Get("InvalidImageOptions"));
        if (request.Background == "transparent" && request.OutputFormat == "jpeg")
            throw new AppException(Text.Get("TransparentJpeg"));
        if (request.References is null || request.References.Count > MaxReferences ||
            request.References.Any(r => r is null || string.IsNullOrEmpty(r.Path) || string.IsNullOrEmpty(r.ThumbnailPath)))
            throw new AppException(Text.Get("InvalidReferences"));
    }

    public static string DetectFormat(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length >= 8 && bytes[..8].SequenceEqual(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 })) return "png";
        if (bytes.Length >= 3 && bytes[0] == 255 && bytes[1] == 216 && bytes[2] == 255) return "jpeg";
        if (bytes.Length >= 12 && bytes[..4].SequenceEqual("RIFF"u8) && bytes.Slice(8, 4).SequenceEqual("WEBP"u8)) return "webp";
        throw new AppException(Text.Get("InvalidImageData"));
    }

    public static string Extension(string format) => format switch
    {
        "png" => ".png", "jpeg" => ".jpg", "webp" => ".webp",
        _ => throw new AppException(Text.Get("UnknownImageFormat"))
    };

    public static async Task<string> ValidateReferenceAsync(string path, CancellationToken cancellationToken = default)
    {
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 8192, true);
        if (stream.Length is <= 0 or >= MaxReferenceBytes)
            throw new AppException(Text.Get("InvalidReferenceSize"));
        var header = new byte[12];
        var count = await stream.ReadAsync(header, cancellationToken);
        var format = DetectFormat(header.AsSpan(0, count));
        var extension = System.IO.Path.GetExtension(path).ToLowerInvariant();
        if ((format == "png" && extension != ".png") ||
            (format == "jpeg" && extension is not ".jpg" and not ".jpeg") || format == "webp")
            throw new AppException(Text.Get("InvalidReferenceFormat"));
        return $"image/{format}";
    }
}
