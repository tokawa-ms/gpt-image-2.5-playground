namespace GptImagePlayground.Core;

public sealed record AppSettings(
    string Endpoint = "",
    string Deployment = "gpt-image-2.5-sunburst",
    string DefaultSize = "auto",
    string DefaultQuality = "auto",
    string CredentialSource = "AzureCli");

public sealed record ReferenceImage(string Path, string Name, string MediaType, string ThumbnailPath)
{
    public override string ToString() => Name;
}

public sealed record GenerationRequest(
    string Prompt, string Size, string Quality, string OutputFormat, string Background,
    IReadOnlyList<ReferenceImage> References);

public sealed record GenerationResult(byte[] Bytes, string Format, DateTimeOffset Created)
{
    public string Extension => ImageRules.Extension(Format);
}

public sealed record HistoryItem(
    string Id, DateTimeOffset Created, AppSettings Settings, GenerationRequest Request,
    string OutputFile, string OutputFormat)
{
    public string Summary => $"{Created.ToLocalTime():yyyy/MM/dd HH:mm} | {Request.Size} | {Request.Quality}";
    public string Details => Text.Get("HistoryDetails", Summary, OutputFormat, Request.Background, Settings.Deployment, Request.Prompt);
    public override string ToString() => $"{Summary} {Request.Prompt}";
}

public sealed record HistoryLoadResult(IReadOnlyList<HistoryItem> Items, IReadOnlyList<string> Errors);

public sealed class AppException(string message, Exception? inner = null) : Exception(message, inner);
