using System.Text.Json;
using System.Text.Json.Nodes;

namespace GptImagePlayground.Core;

public interface IAppSettingsService
{
    Task<AppSettings> LoadAsync();
    Task SaveAsync(AppSettings settings);
}

public sealed class AppSettingsService(string defaultsPath, string localPath) : IAppSettingsService
{
    internal static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public async Task<AppSettings> LoadAsync()
    {
        try
        {
            var root = JsonNode.Parse(await File.ReadAllTextAsync(defaultsPath)) as JsonObject
                ?? throw new AppException(Text.Get("InvalidBundledSettings"));
            if (File.Exists(localPath))
            {
                var overrides = JsonNode.Parse(await File.ReadAllTextAsync(localPath)) as JsonObject
                    ?? throw new AppException(Text.Get("InvalidLocalSettings"));
                foreach (var pair in overrides) root[pair.Key] = pair.Value?.DeepClone();
            }
            var settings = root.Deserialize<AppSettings>() ?? throw new AppException(Text.Get("EmptySettings"));
            if (settings.Endpoint is null || settings.Deployment is null || settings.DefaultQuality is null || settings.DefaultSize is null)
                throw new AppException(Text.Get("NullSettings"));
            ImageRules.ValidateSettings(settings, true);
            return settings;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            throw new AppException(Text.Get("SettingsLoadFailed"), ex);
        }
    }

    public async Task SaveAsync(AppSettings settings)
    {
        ImageRules.ValidateSettings(settings);
        await AtomicFile.WriteJsonAsync(localPath, settings);
    }
}

internal static class AtomicFile
{
    public static async Task WriteJsonAsync<T>(string path, T value)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            await File.WriteAllTextAsync(temporary, JsonSerializer.Serialize(value, AppSettingsService.JsonOptions));
            File.Move(temporary, path, true);
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }
}
