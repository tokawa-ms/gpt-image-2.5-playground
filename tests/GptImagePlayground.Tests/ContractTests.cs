using Azure.Core;
using Azure.Identity;
using GptImagePlayground.Core;
using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;

namespace GptImagePlayground.Tests;

public sealed class TestFiles : IDisposable
{
    public string Root { get; } = Path.Combine(Path.GetTempPath(), "SunburstTests-" + Guid.NewGuid().ToString("N"));
    public static byte[] Png => Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+jRZkAAAAASUVORK5CYII=");
    public TestFiles() => Directory.CreateDirectory(Root);
    public string Write(string name, byte[] data)
    {
        var path = Path.Combine(Root, name);
        File.WriteAllBytes(path, data);
        return path;
    }
    public ReferenceImage Reference(string name = "source.png")
    {
        var path = Write(name, Png);
        return new(path, name, "image/png", path);
    }
    public static AppSettings Settings => new("https://test-resource.openai.azure.com/", "sunburst-deployment");
    public GenerationRequest Request() => new("画像を合成", "1536x864", "xhigh", "png", "auto", [Reference()]);
    public void Dispose() => Directory.Delete(Root, true);
}

public class DimensionTests
{
    [Theory]
    [InlineData("auto")]
    [InlineData("1024x1024")]
    [InlineData("1536x1024")]
    [InlineData("1024x1536")]
    [InlineData("1536x864")]
    [InlineData("864x1536")]
    [InlineData("1024x640")]
    [InlineData("640x1024")]
    [InlineData("1536x512")]
    [InlineData("512x1536")]
    [InlineData("3840x2160")]
    [InlineData("2160x3840")]
    public void AcceptsAllValidBoundaries(string size) => ImageRules.ValidateSize(size);

    [Theory]
    [InlineData("0x1024")]
    [InlineData("-1024x1024")]
    [InlineData("1025x1024")]
    [InlineData("1024x624")]
    [InlineData("3840x2176")]
    [InlineData("2176x3840")]
    [InlineData("3856x1024")]
    [InlineData("1024x3856")]
    [InlineData("1552x512")]
    [InlineData("512x1552")]
    [InlineData("2147483647x2147483647")]
    [InlineData("1X1")]
    [InlineData(" 1024x1024")]
    [InlineData("1024x1024x16")]
    public void RejectsInvalidBoundaries(string size) => Assert.Throws<AppException>(() => ImageRules.ValidateSize(size));

    [Fact]
    public void ExperimentalWarningUsesAreaNotOrientation()
    {
        Assert.False(ImageRules.IsExperimental("2560x1440"));
        Assert.False(ImageRules.IsExperimental("1440x2560"));
        Assert.True(ImageRules.IsExperimental("2160x3840"));
    }

    [Theory]
    [InlineData("https://evil.example/")]
    [InlineData("http://test.openai.azure.com/")]
    [InlineData("https://test.openai.azure.com.evil.example/")]
    [InlineData("https://test.openai.azure.com/api/projects/test")]
    [InlineData("https://test.services.ai.azure.com.evil.example/openai/v1/")]
    [InlineData("https://test.services.ai.azure.com/api/projects/test")]
    [InlineData("https://test.services.ai.azure.com/openai/v1/chat/completions")]
    [InlineData("https://test.services.ai.azure.com/openai/v1/images/generations/extra")]
    [InlineData("https://test.services.ai.azure.com/openai/v1/images/edits#fragment")]
    [InlineData("https://test.openai.azure.com:444/")]
    [InlineData("https://user@test.openai.azure.com/")]
    [InlineData("https://test.openai.azure.com/?q=1")]
    public void RejectsUnsafeEndpoints(string endpoint)
    {
        Assert.Throws<AppException>(() => ImageRules.EditUri(TestFiles.Settings with { Endpoint = endpoint }));
        Assert.Throws<AppException>(() => ImageRules.GenerationUri(TestFiles.Settings with { Endpoint = endpoint }));
    }

    [Theory]
    [InlineData("")]
    [InlineData("/")]
    [InlineData("/openai/v1")]
    [InlineData("/openai/v1/")]
    [InlineData("/openai/v1/images/generations")]
    [InlineData("/openai/v1/images/generations/")]
    [InlineData("/openai/v1/images/edits")]
    [InlineData("/openai/v1/images/edits/")]
    public void AcceptsFoundryEndpointFormsAndUsesEdits(string path)
    {
        foreach (var host in new[] { "test.services.ai.azure.com", "test.openai.azure.com", "test.cognitiveservices.azure.com" })
        {
            var settings = TestFiles.Settings with { Endpoint = $" https://{host}{path} " };
            ImageRules.ValidateSettings(settings);
            Assert.Equal($"https://{host}/openai/v1/images/edits", ImageRules.EditUri(settings).AbsoluteUri);
            Assert.Equal($"https://{host}/openai/v1/images/generations", ImageRules.GenerationUri(settings).AbsoluteUri);
        }
    }

    [Fact]
    public void BackgroundFormatAndInputCountAreValidated()
    {
        using var files = new TestFiles();
        var request = files.Request();
        Assert.Throws<AppException>(() => ImageRules.Validate(request with { OutputFormat = "jpeg", Background = "transparent" }));
        ImageRules.Validate(request with { OutputFormat = "webp", Background = "transparent" });
        ImageRules.Validate(request with { References = [] });
        Assert.Throws<AppException>(() => ImageRules.Validate(request with { References = [], Prompt = "" }));
        Assert.Throws<AppException>(() => ImageRules.Validate(request with { References = [], Size = "123x456" }));
        Assert.Throws<AppException>(() => ImageRules.Validate(request with { References = [], Quality = "invalid" }));
        Assert.Throws<AppException>(() => ImageRules.Validate(request with { References = Enumerable.Repeat(files.Reference(), 17).ToArray() }));
        ImageRules.Validate(request with { References = Enumerable.Repeat(files.Reference(), 16).ToArray() });
        foreach (var quality in ImageRules.Qualities) ImageRules.Validate(request with { Quality = quality });
        Assert.Throws<AppException>(() => ImageRules.Validate(request with { Prompt = new string('a', 32001) }));
    }

    [Fact]
    public async Task InputChecksActualContentExtensionAndSize()
    {
        using var files = new TestFiles();
        Assert.Equal("image/png", await ImageRules.ValidateReferenceAsync(files.Reference().Path));
        await Assert.ThrowsAsync<AppException>(() => ImageRules.ValidateReferenceAsync(files.Write("fake.jpg", TestFiles.Png)));
        await Assert.ThrowsAsync<AppException>(() => ImageRules.ValidateReferenceAsync(files.Write("fake.png", "not an image"u8.ToArray())));
        await Assert.ThrowsAsync<AppException>(() => ImageRules.ValidateReferenceAsync(files.Write("empty.png", [])));
        var huge = files.Write("huge.png", TestFiles.Png);
        using (var stream = File.OpenWrite(huge)) stream.SetLength(ImageRules.MaxReferenceBytes);
        await Assert.ThrowsAsync<AppException>(() => ImageRules.ValidateReferenceAsync(huge));
    }

    [Theory]
    [InlineData("png", ".png")]
    [InlineData("jpeg", ".jpg")]
    [InlineData("webp", ".webp")]
    public void ExtensionMatchesActualEncoding(string format, string extension) =>
        Assert.Equal(extension, new GenerationResult([], format, DateTimeOffset.UtcNow).Extension);
}

public sealed class MockCredential : TokenCredential
{
    public TokenRequestContext? Context { get; private set; }
    public bool Fail { get; set; }
    public int Calls { get; private set; }
    public override AccessToken GetToken(TokenRequestContext requestContext, CancellationToken cancellationToken) =>
        GetTokenAsync(requestContext, cancellationToken).GetAwaiter().GetResult();
    public override ValueTask<AccessToken> GetTokenAsync(TokenRequestContext requestContext, CancellationToken cancellationToken)
    {
        Calls++;
        Context = requestContext;
        cancellationToken.ThrowIfCancellationRequested();
        if (Fail) throw new AuthenticationFailedException("mock failure");
        return ValueTask.FromResult(new AccessToken("mock-only-token", DateTimeOffset.UtcNow.AddHours(1)));
    }
}

public sealed class MockHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> respond) : HttpMessageHandler
{
    public int Calls { get; private set; }
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Calls++;
        return respond(request, cancellationToken);
    }
}

public class HttpContractTests
{
    [Theory]
    [InlineData("https://test-resource.services.ai.azure.com/")]
    [InlineData("https://test-resource.services.ai.azure.com/openai/v1/images/generations")]
    [InlineData("https://test-resource.services.ai.azure.com/openai/v1/images/edits")]
    public async Task NoReferencesUsesGenerationJsonWithAllSettings(string endpoint)
    {
        var credential = new MockCredential();
        var request = new GenerationRequest("青い画像を生成", "2160x3840", "max", "png", "transparent", []);
        using var handler = new MockHandler(async (message, token) =>
        {
            Assert.Equal(HttpMethod.Post, message.Method);
            Assert.Equal("https://test-resource.services.ai.azure.com/openai/v1/images/generations", message.RequestUri!.AbsoluteUri);
            Assert.Equal("application/json", message.Content!.Headers.ContentType!.MediaType);
            Assert.Equal("Bearer", message.Headers.Authorization!.Scheme);
            Assert.Equal("mock-only-token", message.Headers.Authorization.Parameter);
            using var json = JsonDocument.Parse(await message.Content.ReadAsStringAsync(token));
            var body = json.RootElement;
            Assert.Equal(7, body.EnumerateObject().Count());
            Assert.Equal("sunburst-deployment", body.GetProperty("model").GetString());
            Assert.Equal(request.Prompt, body.GetProperty("prompt").GetString());
            Assert.Equal(1, body.GetProperty("n").GetInt32());
            Assert.Equal(request.Size, body.GetProperty("size").GetString());
            Assert.Equal(request.Quality, body.GetProperty("quality").GetString());
            Assert.Equal(request.OutputFormat, body.GetProperty("output_format").GetString());
            Assert.Equal(request.Background, body.GetProperty("background").GetString());
            Assert.False(body.TryGetProperty("image", out _));
            Assert.False(body.TryGetProperty("images", out _));
            return Response(TestFiles.Png);
        });
        using var client = new HttpClient(handler);
        var result = await new FoundryImageService(client, credential).EditAsync(TestFiles.Settings with { Endpoint = endpoint }, request, default);
        Assert.Equal(TestFiles.Png, result.Bytes);
        Assert.Equal("png", result.Format);
        Assert.Equal(["https://ai.azure.com/.default"], credential.Context!.Value.Scopes);
        Assert.Equal(1, handler.Calls);
    }

    [Theory]
    [InlineData("AzureCli")]
    [InlineData("VisualStudio")]
    public void CredentialSelectionExcludesOtherAccounts(string source)
    {
        var options = DeveloperCredentialProvider.CreateOptions(source);
        Assert.Equal(source != "AzureCli", options.ExcludeAzureCliCredential);
        Assert.Equal(source != "VisualStudio", options.ExcludeVisualStudioCredential);
        Assert.True(options.ExcludeEnvironmentCredential);
        Assert.True(options.ExcludeManagedIdentityCredential);
        Assert.True(options.ExcludeWorkloadIdentityCredential);
        Assert.True(options.ExcludeInteractiveBrowserCredential);
        Assert.True(options.ExcludeBrokerCredential);
        Assert.True(options.ExcludeVisualStudioCodeCredential);
        Assert.True(options.ExcludeAzurePowerShellCredential);
        Assert.True(options.ExcludeAzureDeveloperCliCredential);
    }

    [Fact]
    public void InvalidCredentialSourceCannotSilentlyFallBack()
    {
        Assert.Throws<AppException>(() => DeveloperCredentialProvider.CreateOptions("Other"));
        Assert.Throws<AppException>(() => ImageRules.ValidateSettings(TestFiles.Settings with { CredentialSource = "Other" }));
    }

    [Fact]
    public async Task CredentialFactoryReceivesCurrentSavedSelection()
    {
        using var files = new TestFiles();
        var cli = new MockCredential();
        var studio = new MockCredential { Fail = true };
        using var handler = new MockHandler((_, _) => Task.FromResult(Response(TestFiles.Png)));
        using var client = new HttpClient(handler);
        var service = new FoundryImageService(client, settings => settings.CredentialSource == "AzureCli" ? cli : studio);
        await Assert.ThrowsAsync<AppException>(() => service.EditAsync(TestFiles.Settings with { CredentialSource = "VisualStudio" }, files.Request(), default));
        Assert.Equal(0, cli.Calls);
        Assert.Equal(0, handler.Calls);
        await service.EditAsync(TestFiles.Settings with { CredentialSource = "AzureCli" }, files.Request(), default);
        Assert.Equal(1, cli.Calls);
        Assert.Equal(1, studio.Calls);
        Assert.Equal(1, handler.Calls);
    }

    [Fact]
    public async Task TenantMismatchExplainsAuthenticationInsteadOfImageParameters()
    {
        using var files = new TestFiles();
        using var handler = new MockHandler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.BadRequest)
        {
            Content = new StringContent("""
                {"error":{"code":"Tenant provided in token does not match resource token",
                "message":"Token tenant does not match resource tenant."}}
                """)
        }));
        using var client = new HttpClient(handler);
        var ex = await Assert.ThrowsAsync<AppException>(() =>
            new FoundryImageService(client, new MockCredential()).EditAsync(TestFiles.Settings, files.Request(), default));
        Assert.Contains(Text.Get("TenantMismatch"), ex.Message);
        Assert.Contains("az login --tenant", ex.Message);
        Assert.Equal(1, handler.Calls);
    }

    [Fact]
    public void ProductionHandlerDoesNotFollowRedirects()
    {
        using var handler = FoundryImageService.CreateHttpHandler();
        Assert.False(handler.AllowAutoRedirect);
    }

    [Theory]
    [InlineData("https://test-resource.openai.azure.com/", "https://test-resource.openai.azure.com/openai/v1/images/edits")]
    [InlineData("https://test-resource.services.ai.azure.com/openai/v1/images/generations", "https://test-resource.services.ai.azure.com/openai/v1/images/edits")]
    public async Task SendsExactAzureMultipartContractInReferenceOrder(string endpoint, string expectedUri)
    {
        using var files = new TestFiles();
        var request = files.Request() with { References = [files.Reference("first.png"), files.Reference("second.png")] };
        var credential = new MockCredential();
        using var handler = new MockHandler(async (message, token) =>
        {
            Assert.Equal(HttpMethod.Post, message.Method);
            Assert.Equal(expectedUri, message.RequestUri!.AbsoluteUri);
            Assert.Equal(new AuthenticationHeaderValue("Bearer", "mock-only-token"), message.Headers.Authorization);
            var multipart = Assert.IsType<MultipartFormDataContent>(message.Content);
            Assert.Equal("multipart/form-data", multipart.Headers.ContentType!.MediaType);
            var parts = multipart.ToArray();
            Assert.Equal(9, parts.Length);
            Assert.Equal("image[]", parts[0].Headers.ContentDisposition!.Name!.Trim('"'));
            Assert.Equal("first.png", parts[0].Headers.ContentDisposition!.FileName!.Trim('"'));
            Assert.Equal("second.png", parts[1].Headers.ContentDisposition!.FileName!.Trim('"'));
            Assert.Equal("image/png", parts[0].Headers.ContentType!.MediaType);
            Assert.Equal(TestFiles.Png, await parts[0].ReadAsByteArrayAsync(token));
            var fields = new Dictionary<string, string>();
            foreach (var part in parts.Skip(2))
                fields.Add(part.Headers.ContentDisposition!.Name!.Trim('"'), await part.ReadAsStringAsync(token));
            Assert.Equal("画像を合成", fields["prompt"]);
            Assert.Equal("sunburst-deployment", fields["model"]);
            Assert.Equal("1", fields["n"]);
            Assert.Equal("1536x864", fields["size"]);
            Assert.Equal("xhigh", fields["quality"]);
            Assert.Equal("png", fields["output_format"]);
            Assert.Equal("auto", fields["background"]);
            Assert.False(fields.ContainsKey("images"));
            return Response(TestFiles.Png);
        });
        using var http = new HttpClient(handler);
        var result = await new FoundryImageService(http, credential).EditAsync(TestFiles.Settings with { Endpoint = endpoint }, request, default);
        Assert.Equal(TestFiles.Png, result.Bytes);
        Assert.Equal("png", result.Format);
        Assert.Equal(["https://ai.azure.com/.default"], credential.Context!.Value.Scopes);
        Assert.Null(http.DefaultRequestHeaders.Authorization);
        Assert.Equal(1, handler.Calls);
    }

    [Theory]
    [InlineData(400)]
    [InlineData(401)]
    [InlineData(403)]
    [InlineData(404)]
    [InlineData(429)]
    [InlineData(500)]
    [InlineData(307)]
    public async Task ErrorsAreExplicitAndNeverRetried(int code)
    {
        using var files = new TestFiles();
        using var handler = new MockHandler((_, _) => Task.FromResult(new HttpResponseMessage((HttpStatusCode)code)));
        using var client = new HttpClient(handler);
        var ex = await Assert.ThrowsAsync<AppException>(() => new FoundryImageService(client, new MockCredential()).EditAsync(TestFiles.Settings, files.Request(), default));
        Assert.Contains(code.ToString(), ex.Message);
        Assert.Equal(1, handler.Calls);
    }

    [Theory]
    [InlineData(400)]
    [InlineData(401)]
    [InlineData(403)]
    [InlineData(429)]
    [InlineData(500)]
    public async Task ServiceErrorDetailsAndRequestIdsArePreserved(int status)
    {
        using var files = new TestFiles();
        using var handler = new MockHandler((_, _) =>
        {
            var response = new HttpResponseMessage((HttpStatusCode)status)
            {
                Content = new StringContent("""
                    {"error":{"code":"invalid_parameter","type":"invalid_request_error","param":"quality",
                    "message":"Requested quality is not supported by this deployment.",
                    "innererror":{"code":"DeploymentParameterMismatch"},
                    "debug":"private-server-data"},"request":{"prompt":"private-prompt","image":"private-image"}}
                    """)
            };
            response.Headers.Add("apim-request-id", "test-apim-id");
            response.Headers.Add("x-request-id", "test-request-id");
            response.Headers.Add("x-ms-request-id", "test-ms-id");
            return Task.FromResult(response);
        });
        using var client = new HttpClient(handler);
        var ex = await Assert.ThrowsAsync<AppException>(() =>
            new FoundryImageService(client, new MockCredential()).EditAsync(TestFiles.Settings, files.Request(), default));
        Assert.Contains($"HTTP {status}", ex.Message);
        Assert.Contains("code: invalid_parameter", ex.Message);
        Assert.Contains("type: invalid_request_error", ex.Message);
        Assert.Contains("param: quality", ex.Message);
        Assert.Contains("message: Requested quality is not supported by this deployment.", ex.Message);
        Assert.Contains("innererror.code: DeploymentParameterMismatch", ex.Message);
        Assert.Contains("apim-request-id: test-apim-id", ex.Message);
        Assert.Contains("x-request-id: test-request-id", ex.Message);
        Assert.Contains("x-ms-request-id: test-ms-id", ex.Message);
        Assert.DoesNotContain("private-", ex.Message);
        Assert.DoesNotContain("mock-only-token", ex.Message);
        Assert.Equal(1, handler.Calls);
    }

    [Theory]
    [InlineData("")]
    [InlineData("<html>private-gateway-error</html>")]
    [InlineData("null")]
    [InlineData("[]")]
    [InlineData("{\"error\":null}")]
    [InlineData("{\"error\":{\"code\":null,\"message\":{},\"param\":[]}}")]
    public async Task MalformedErrorKeepsHttpStatusAndRequestId(string body)
    {
        using var files = new TestFiles();
        using var handler = new MockHandler((_, _) =>
        {
            var response = new HttpResponseMessage(HttpStatusCode.BadRequest) { Content = new StringContent(body) };
            response.Headers.Add("apim-request-id", "trace-id");
            return Task.FromResult(response);
        });
        using var client = new HttpClient(handler);
        var ex = await Assert.ThrowsAsync<AppException>(() =>
            new FoundryImageService(client, new MockCredential()).EditAsync(TestFiles.Settings, files.Request(), default));
        Assert.Contains("HTTP 400", ex.Message);
        Assert.Contains("apim-request-id: trace-id", ex.Message);
        Assert.DoesNotContain("private-gateway-error", ex.Message);
        Assert.Equal(1, handler.Calls);
    }

    [Theory]
    [InlineData(10)]
    [InlineData(4000)]
    [InlineData(70000)]
    public async Task ErrorDiagnosticsAreBounded(int length)
    {
        using var files = new TestFiles();
        var body = JsonSerializer.Serialize(new { error = new { code = 400, message = new string('a', length) + "\u0001" } });
        using var handler = new MockHandler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.BadRequest)
        {
            Content = new StringContent(body)
        }));
        using var client = new HttpClient(handler);
        var ex = await Assert.ThrowsAsync<AppException>(() =>
            new FoundryImageService(client, new MockCredential()).EditAsync(TestFiles.Settings, files.Request(), default));
        Assert.Contains("HTTP 400", ex.Message);
        Assert.True(ex.Message.Length < 4000);
        Assert.DoesNotContain("\u0001", ex.Message, StringComparison.Ordinal);
        Assert.Equal(1, handler.Calls);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task CancelPropagatesAndDoesNotRetry(bool hasReferences)
    {
        using var files = new TestFiles();
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var handler = new MockHandler(async (_, token) =>
        {
            started.SetResult();
            await Task.Delay(Timeout.Infinite, token);
            return Response(TestFiles.Png);
        });
        using var client = new HttpClient(handler);
        using var cancellation = new CancellationTokenSource();
        var request = files.Request();
        if (!hasReferences) request = request with { References = [] };
        var task = new FoundryImageService(client, new MockCredential()).EditAsync(TestFiles.Settings, request, cancellation.Token);
        await started.Task;
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => task);
        Assert.Equal(1, handler.Calls);
    }

    [Fact]
    public async Task MissingEndpointCannotAuthenticateOrSend()
    {
        using var files = new TestFiles();
        var credential = new MockCredential();
        using var handler = new MockHandler((_, _) => throw new InvalidOperationException("Must not send"));
        using var client = new HttpClient(handler);
        await Assert.ThrowsAsync<AppException>(() => new FoundryImageService(client, credential).EditAsync(new AppSettings(), files.Request(), default));
        Assert.Equal(0, credential.Calls);
        Assert.Equal(0, handler.Calls);
    }

    [Fact]
    public async Task AuthenticationFailureIsActionable()
    {
        using var files = new TestFiles();
        using var handler = new MockHandler((_, _) => throw new InvalidOperationException("Must not send"));
        using var client = new HttpClient(handler);
        var error = await Assert.ThrowsAsync<AppException>(() => new FoundryImageService(client, new MockCredential { Fail = true }).EditAsync(TestFiles.Settings, files.Request(), default));
        Assert.Contains("az login", error.Message);
        Assert.Equal(0, handler.Calls);
    }

    [Theory]
    [InlineData("{\"data\":[{\"url\":\"https://example.com\"}]}")]
    [InlineData("{\"data\":[{\"b64_json\":\"invalid\"}]}")]
    [InlineData("{\"data\":[]}")]
    [InlineData("{\"data\":[null]}")]
    [InlineData("{\"data\":[1]}")]
    [InlineData("[]")]
    [InlineData("null")]
    [InlineData("not json")]
    public async Task InvalidResponsesAreNotSuccess(string body)
    {
        using var files = new TestFiles();
        using var handler = new MockHandler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body) }));
        using var client = new HttpClient(handler);
        await Assert.ThrowsAsync<AppException>(() => new FoundryImageService(client, new MockCredential()).EditAsync(TestFiles.Settings, files.Request(), default));
    }

    [Fact]
    public async Task EncodingMismatchIsRejected()
    {
        using var files = new TestFiles();
        using var handler = new MockHandler((_, _) => Task.FromResult(Response(TestFiles.Png)));
        using var client = new HttpClient(handler);
        await Assert.ThrowsAsync<AppException>(() => new FoundryImageService(client, new MockCredential()).EditAsync(TestFiles.Settings, files.Request() with { OutputFormat = "jpeg" }, default));
    }

    private static HttpResponseMessage Response(byte[] image) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(JsonSerializer.Serialize(new { data = new[] { new { b64_json = Convert.ToBase64String(image) } } }))
    };
}
