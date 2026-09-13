using System.Text.Json;

namespace GptImagePlayground.Core;

public interface IHistoryService
{
    Task<HistoryItem> SaveAsync(AppSettings settings, GenerationRequest request, GenerationResult result);
    Task<HistoryLoadResult> LoadAsync();
    Task<GenerationResult> ReadOutputAsync(HistoryItem item);
}

public sealed class HistoryService(string root) : IHistoryService
{
    public async Task<HistoryItem> SaveAsync(AppSettings settings, GenerationRequest request, GenerationResult result)
    {
        Directory.CreateDirectory(root);
        var id = Guid.NewGuid().ToString("N");
        var staging = Path.Combine(root, id + ".pending");
        var destination = Path.Combine(root, id);
        Directory.CreateDirectory(staging);
        try
        {
            var references = new List<ReferenceImage>();
            for (var i = 0; i < request.References.Count; i++)
            {
                var reference = request.References[i];
                var fileName = $"reference-{i + 1:00}{Path.GetExtension(reference.Path)}";
                await CopyAsync(reference.Path, Path.Combine(staging, fileName));
                var thumbnail = $"thumbnail-{i + 1:00}.png";
                await CopyAsync(reference.ThumbnailPath, Path.Combine(staging, thumbnail));
                references.Add(reference with { Path = fileName, ThumbnailPath = thumbnail });
            }
            var outputName = "output" + result.Extension;
            await File.WriteAllBytesAsync(Path.Combine(staging, outputName), result.Bytes);
            var stored = new HistoryItem(id, result.Created, settings, request with { References = references }, outputName, result.Format);
            await AtomicFile.WriteJsonAsync(Path.Combine(staging, "metadata.json"), stored);
            Directory.Move(staging, destination);
            return Resolve(stored, destination);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new AppException(Text.Get("HistorySaveFailed"), ex);
        }
        finally
        {
            if (Directory.Exists(staging)) Directory.Delete(staging, true);
        }
    }

    public async Task<HistoryLoadResult> LoadAsync()
    {
        Directory.CreateDirectory(root);
        var items = new List<HistoryItem>();
        var errors = new List<string>();
        foreach (var folder in Directory.EnumerateDirectories(root))
        {
            try
            {
                if (folder.EndsWith(".pending", StringComparison.Ordinal))
                    throw new AppException(Text.Get("IncompleteHistory"));
                var stored = JsonSerializer.Deserialize<HistoryItem>(await File.ReadAllTextAsync(Path.Combine(folder, "metadata.json")))
                    ?? throw new AppException(Text.Get("EmptyHistoryMetadata"));
                if (stored.Request is null || stored.Settings is null || stored.Request.References is null ||
                    stored.Id != Path.GetFileName(folder))
                    throw new AppException(Text.Get("InvalidHistoryMetadata"));
                ImageRules.Validate(stored.Request);
                _ = ImageRules.Extension(stored.OutputFormat);
                items.Add(Resolve(stored, folder));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or AppException or ArgumentException)
            {
                errors.Add(Text.Get("HistoryLoadFailed", Path.GetFileName(folder), ex.Message));
            }
        }
        return new HistoryLoadResult(items.OrderByDescending(x => x.Created).ToArray(), errors);
    }

    public async Task<GenerationResult> ReadOutputAsync(HistoryItem item)
    {
        var file = new FileInfo(item.OutputFile);
        if (file.Length > 96 * 1024 * 1024) throw new AppException(Text.Get("HistoryImageTooLarge"));
        var bytes = await File.ReadAllBytesAsync(item.OutputFile);
        if (ImageRules.DetectFormat(bytes) != item.OutputFormat) throw new AppException(Text.Get("HistoryFormatMismatch"));
        return new GenerationResult(bytes, item.OutputFormat, item.Created);
    }

    private static HistoryItem Resolve(HistoryItem stored, string folder)
    {
        string ResolveFile(string name)
        {
            if (string.IsNullOrEmpty(name) || Path.GetFileName(name) != name || name is "." or "..")
                throw new AppException(Text.Get("InvalidHistoryFileName"));
            var path = Path.Combine(folder, name);
            if (!File.Exists(path)) throw new AppException(Text.Get("HistoryFileMissing", name));
            return path;
        }
        return stored with
        {
            OutputFile = ResolveFile(stored.OutputFile),
            Request = stored.Request with
            {
                References = stored.Request.References.Select(r => r with
                {
                    Path = ResolveFile(r.Path), ThumbnailPath = ResolveFile(r.ThumbnailPath)
                }).ToArray()
            }
        };
    }

    private static async Task CopyAsync(string source, string destination)
    {
        await using var input = File.OpenRead(source);
        await using var output = new FileStream(destination, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, true);
        await input.CopyToAsync(output);
    }
}
