using Azure.Core;
using Azure.Identity;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

namespace GptImagePlayground.Core;

public interface IFoundryImageService
{
    Task<GenerationResult> EditAsync(AppSettings settings, GenerationRequest request, CancellationToken cancellationToken);
}

public sealed class FoundryImageService(HttpClient httpClient, Func<AppSettings, TokenCredential> credentialFactory) : IFoundryImageService
{
    public FoundryImageService(HttpClient httpClient, TokenCredential credential) : this(httpClient, _ => credential) { }

    public const string Scope = "https://ai.azure.com/.default";

    public static HttpClientHandler CreateHttpHandler() => new() { AllowAutoRedirect = false };

    public static HttpClient CreateHttpClient() => new(CreateHttpHandler())
    {
        Timeout = TimeSpan.FromMinutes(10),
        MaxResponseContentBufferSize = 128 * 1024 * 1024
    };

    public async Task<GenerationResult> EditAsync(AppSettings settings, GenerationRequest request, CancellationToken cancellationToken)
    {
        ImageRules.Validate(request);
        var hasReferences = request.References.Count > 0;
        var uri = hasReferences ? ImageRules.EditUri(settings) : ImageRules.GenerationUri(settings);
        using HttpContent content = hasReferences ? new MultipartFormDataContent() : JsonContent.Create(new
        {
            model = settings.Deployment,
            prompt = request.Prompt,
            n = 1,
            size = request.Size,
            quality = request.Quality,
            output_format = request.OutputFormat,
            background = request.Background
        });
        if (content is MultipartFormDataContent multipart)
        {
            foreach (var reference in request.References)
            {
                var mediaType = await ImageRules.ValidateReferenceAsync(reference.Path, cancellationToken);
                var stream = new FileStream(reference.Path, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, true);
                var image = new StreamContent(stream);
                image.Headers.ContentType = new MediaTypeHeaderValue(mediaType);
                multipart.Add(image, "image[]", Path.GetFileName(reference.Path));
            }
            multipart.Add(new StringContent(request.Prompt), "prompt");
            multipart.Add(new StringContent(settings.Deployment), "model");
            multipart.Add(new StringContent("1"), "n");
            multipart.Add(new StringContent(request.Size), "size");
            multipart.Add(new StringContent(request.Quality), "quality");
            multipart.Add(new StringContent(request.OutputFormat), "output_format");
            multipart.Add(new StringContent(request.Background), "background");
        }

        AccessToken token;
        try
        {
            token = await credentialFactory(settings).GetTokenAsync(new TokenRequestContext([Scope]), cancellationToken);
        }
        catch (AuthenticationFailedException ex)
        {
            throw new AppException(Text.Get("AuthenticationFailed", settings.CredentialSource), ex);
        }
        using var message = new HttpRequestMessage(HttpMethod.Post, uri) { Content = content };
        message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token.Token);
        try
        {
            // Never automatically replay this potentially paid POST, including 429/5xx.
            using var response = await httpClient.SendAsync(message, HttpCompletionOption.ResponseContentRead, cancellationToken);
            if (!response.IsSuccessStatusCode) throw await ServiceErrorAsync(response, cancellationToken);
            var json = await response.Content.ReadAsStringAsync(cancellationToken);
            using var document = JsonDocument.Parse(json);
            if (document.RootElement.ValueKind != JsonValueKind.Object ||
                !document.RootElement.TryGetProperty("data", out var data) ||
                data.ValueKind != JsonValueKind.Array || data.GetArrayLength() != 1 ||
                data[0].ValueKind != JsonValueKind.Object ||
                !data[0].TryGetProperty("b64_json", out var encoded) || encoded.ValueKind != JsonValueKind.String)
                throw new AppException(Text.Get("MissingResponseImage"));
            var bytes = Convert.FromBase64String(encoded.GetString()!);
            var format = ImageRules.DetectFormat(bytes);
            if (format != request.OutputFormat)
                throw new AppException(Text.Get("ResponseFormatMismatch", request.OutputFormat, format));
            return new GenerationResult(bytes, format, DateTimeOffset.UtcNow);
        }
        catch (OperationCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            throw new AppException(Text.Get("RequestTimeout"), ex);
        }
        catch (HttpRequestException ex)
        {
            throw new AppException(Text.Get("NetworkFailed"), ex);
        }
        catch (Exception ex) when (ex is JsonException or FormatException)
        {
            throw new AppException(Text.Get("InvalidResponse"), ex);
        }
    }

    private static async Task<AppException> ServiceErrorAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        var details = new List<string> { ServiceErrorSummary(response.StatusCode) };
        foreach (var header in new[] { "apim-request-id", "x-request-id", "x-ms-request-id" })
        {
            if (response.Headers.TryGetValues(header, out var values))
                details.Add($"{header}: {DisplayValue(string.Join(", ", values), 256)}");
        }

        // Show only diagnostic fields, not raw responses, request images, or credentials.
        using var reader = new StreamReader(await response.Content.ReadAsStreamAsync(cancellationToken));
        var buffer = new char[65537];
        var count = await reader.ReadBlockAsync(buffer.AsMemory(), cancellationToken);
        if (count == buffer.Length)
        {
            details.Add(Text.Get("ErrorResponseTooLarge"));
        }
        else
        {
            try
            {
                using var document = JsonDocument.Parse(new string(buffer, 0, count));
                var root = document.RootElement;
                if (root.ValueKind == JsonValueKind.Object &&
                    root.TryGetProperty("error", out var error) && error.ValueKind == JsonValueKind.Object)
                {
                    var before = details.Count;
                    AddErrorField(details, error, "code");
                    AddErrorField(details, error, "type");
                    AddErrorField(details, error, "param");
                    AddErrorField(details, error, "message", 3000);
                    if (error.TryGetProperty("code", out var code) && code.ValueKind == JsonValueKind.String &&
                        code.GetString()!.Contains("Tenant provided in token", StringComparison.OrdinalIgnoreCase))
                        details.Add(Text.Get("TenantMismatch"));
                    if (error.TryGetProperty("innererror", out var inner) && inner.ValueKind == JsonValueKind.Object)
                        AddErrorField(details, inner, "code", label: "innererror.code");
                    if (details.Count == before)
                        details.Add(Text.Get("NoErrorDetails"));
                }
                else
                {
                    details.Add(Text.Get("NoErrorObject"));
                }
            }
            catch (JsonException)
            {
                details.Add(Text.Get("InvalidErrorResponse"));
            }
        }
        details.Add(Text.Get("ErrorSharingNotice"));
        return new AppException(string.Join(Environment.NewLine, details));
    }

    private static void AddErrorField(List<string> details, JsonElement error, string field, int limit = 256, string? label = null)
    {
        if (!error.TryGetProperty(field, out var value)) return;
        var text = value.ValueKind switch
        {
            JsonValueKind.String => value.GetString(),
            JsonValueKind.Number => value.GetRawText(),
            _ => null
        };
        if (!string.IsNullOrWhiteSpace(text))
            details.Add($"{label ?? field}: {DisplayValue(text, limit)}");
    }

    private static string DisplayValue(string value, int limit)
    {
        var text = value.Length > limit ? value[..limit] : value;
        text = new string(text.Select(c => char.IsControl(c) ? ' ' : c).ToArray());
        return value.Length > limit ? text + Text.Get("Truncated") : text;
    }

    private static string ServiceErrorSummary(HttpStatusCode status) => status switch
    {
        HttpStatusCode.Unauthorized => Text.Get("Http401"),
        HttpStatusCode.Forbidden => Text.Get("Http403"),
        HttpStatusCode.NotFound => Text.Get("Http404"),
        HttpStatusCode.TooManyRequests => Text.Get("Http429"),
        HttpStatusCode.BadRequest => Text.Get("Http400"),
        _ => Text.Get("HttpOther", (int)status)
    };
}
