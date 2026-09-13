using GptImagePlayground.Core;
using System.Text.Json;

namespace GptImagePlayground.Tests;

public class PersistenceTests
{
    [Fact]
    public async Task SettingsMergeBundledDefaultsWithPartialLocalOverrideAndRoundtrip()
    {
        using var files = new TestFiles();
        var defaults = Path.Combine(files.Root, "appsettings.json");
        var local = Path.Combine(files.Root, "settings.json");
        await File.WriteAllTextAsync(defaults, JsonSerializer.Serialize(TestFiles.Settings with { DefaultQuality = "high" }));
        await File.WriteAllTextAsync(local, "{\"DefaultSize\":\"2160x3840\"}");
        var service = new AppSettingsService(defaults, local);
        var settings = await service.LoadAsync();
        Assert.Equal(TestFiles.Settings.Endpoint, settings.Endpoint);
        Assert.Equal("high", settings.DefaultQuality);
        Assert.Equal("2160x3840", settings.DefaultSize);
        Assert.Equal("AzureCli", settings.CredentialSource);
        await service.SaveAsync(settings with { DefaultQuality = "max", CredentialSource = "VisualStudio" });
        Assert.Equal(settings with { DefaultQuality = "max", CredentialSource = "VisualStudio" }, await service.LoadAsync());
        Assert.Empty(Directory.GetFiles(files.Root, "*.tmp"));
    }

    [Fact]
    public async Task CorruptSettingsNeverSilentlyFallBack()
    {
        using var files = new TestFiles();
        var defaults = files.Write("defaults.json", "{}"u8.ToArray());
        var local = files.Write("settings.json", "bad json"u8.ToArray());
        await Assert.ThrowsAsync<AppException>(() => new AppSettingsService(defaults, local).LoadAsync());
    }

    [Fact]
    public async Task HistoryCopiesReferencesAndRoundtripsAfterOriginalsDisappear()
    {
        using var files = new TestFiles();
        var request = files.Request();
        var service = new HistoryService(Path.Combine(files.Root, "history"));
        var result = new GenerationResult(TestFiles.Png, "png", DateTimeOffset.UtcNow);
        var saved = await service.SaveAsync(TestFiles.Settings, request, result);
        File.Delete(request.References[0].Path);
        var loaded = await service.LoadAsync();
        Assert.Empty(loaded.Errors);
        var item = Assert.Single(loaded.Items);
        Assert.Equal(saved.Id, item.Id);
        Assert.Equal(request.Prompt, item.Request.Prompt);
        Assert.Equal(request.Quality, item.Request.Quality);
        Assert.Equal(TestFiles.Settings, item.Settings);
        Assert.Equal(TestFiles.Png, await File.ReadAllBytesAsync(item.Request.References[0].Path));
        Assert.Equal(TestFiles.Png, (await service.ReadOutputAsync(item)).Bytes);
        Assert.EndsWith(".png", item.OutputFile);
        Assert.DoesNotContain(Directory.GetDirectories(Path.Combine(files.Root, "history")), x => x.EndsWith(".pending"));
    }

    [Fact]
    public async Task CorruptHistoryDoesNotHideOtherEntriesAndReportsErrors()
    {
        using var files = new TestFiles();
        var root = Path.Combine(files.Root, "history");
        var service = new HistoryService(root);
        var result = new GenerationResult(TestFiles.Png, "png", DateTimeOffset.UtcNow);
        var item = await service.SaveAsync(TestFiles.Settings, files.Request(), result);
        Directory.CreateDirectory(Path.Combine(root, "broken"));
        await File.WriteAllTextAsync(Path.Combine(root, "broken", "metadata.json"), "{");
        var loaded = await service.LoadAsync();
        Assert.Equal(item.Id, Assert.Single(loaded.Items).Id);
        Assert.Single(loaded.Errors);
    }

    [Fact]
    public async Task HistoryRejectsPathTraversal()
    {
        using var files = new TestFiles();
        var root = Path.Combine(files.Root, "history");
        var service = new HistoryService(root);
        var item = await service.SaveAsync(TestFiles.Settings, files.Request(), new(TestFiles.Png, "png", DateTimeOffset.UtcNow));
        var metadata = Path.Combine(root, item.Id, "metadata.json");
        var stored = JsonSerializer.Deserialize<HistoryItem>(await File.ReadAllTextAsync(metadata))!;
        await File.WriteAllTextAsync(metadata, JsonSerializer.Serialize(stored with { OutputFile = "..\\outside.png" }));
        var loaded = await service.LoadAsync();
        Assert.Empty(loaded.Items);
        Assert.Single(loaded.Errors);
    }

    [Fact]
    public async Task HistoryNullFieldsAreReportedAsCorruption()
    {
        using var files = new TestFiles();
        var root = Path.Combine(files.Root, "history");
        var service = new HistoryService(root);
        var item = await service.SaveAsync(TestFiles.Settings, files.Request(), new(TestFiles.Png, "png", DateTimeOffset.UtcNow));
        var metadata = Path.Combine(root, item.Id, "metadata.json");
        var node = System.Text.Json.Nodes.JsonNode.Parse(await File.ReadAllTextAsync(metadata))!;
        node["Request"]!["Size"] = null;
        await File.WriteAllTextAsync(metadata, node.ToJsonString());
        var loaded = await service.LoadAsync();
        Assert.Empty(loaded.Items);
        Assert.Single(loaded.Errors);
    }
}

public class ViewModelTests
{
    [Fact]
    public async Task PromptOnlyGenerationPersistsAndReopensWithoutStaleReferences()
    {
        using var files = new TestFiles();
        var history = new HistoryService(Path.Combine(files.Root, "history"));
        var foundry = new FakeFoundry((request, _) =>
        {
            ImageRules.Validate(request);
            Assert.Empty(request.References);
            return Task.FromResult(new GenerationResult(TestFiles.Png, "png", DateTimeOffset.UtcNow));
        });
        var vm = new MainViewModel(new FakeSettings(), foundry, history);
        await vm.InitializeAsync();
        vm.Prompt = "青い画像を生成";
        vm.Size = "1024x1024";
        await vm.GenerateCommand.ExecuteAsync(null);
        Assert.False(vm.HasError);
        Assert.True(vm.HasResult);
        Assert.Equal(Text.Get("GeneratedWithoutReferences"), vm.Status);
        var loaded = await history.LoadAsync();
        Assert.Empty(loaded.Errors);
        var item = Assert.Single(loaded.Items);
        Assert.Empty(item.Request.References);
        Assert.Equal(vm.Prompt, item.Request.Prompt);
        Assert.Equal(TestFiles.Png, (await history.ReadOutputAsync(item)).Bytes);
        vm.References.Add(files.Reference());
        vm.History.SelectedItem = item;
        await vm.OpenHistoryCommand.ExecuteAsync(null);
        Assert.False(vm.HasError);
        Assert.Empty(vm.References);
        Assert.Equal("1024x1024", vm.Size);
        await vm.GenerateCommand.ExecuteAsync(null);
        Assert.False(vm.HasError);
        Assert.Equal(2, foundry.Calls);
    }

    [Fact]
    public async Task FoundryPortalUrlCanBeSavedAndReloadedWithoutCallingAzure()
    {
        using var files = new TestFiles();
        var defaults = files.Write("appsettings.json", "{}"u8.ToArray());
        var settings = new AppSettingsService(defaults, Path.Combine(files.Root, "settings.json"));
        var foundry = new FakeFoundry((_, _) => throw new InvalidOperationException("Settings must not call Azure."));
        var vm = new MainViewModel(settings, foundry, new FakeHistory());
        await vm.InitializeAsync();
        vm.Settings.Endpoint = "https://test-resource.services.ai.azure.com/openai/v1/images/generations";
        vm.Settings.CredentialSource = "VisualStudio";
        await vm.SaveSettingsCommand.ExecuteAsync(null);
        Assert.False(vm.HasError);
        Assert.True(vm.IsReady);
        Assert.True(vm.GenerateCommand.CanExecute(null));
        var reloaded = new MainViewModel(settings, foundry, new FakeHistory());
        await reloaded.InitializeAsync();
        Assert.False(reloaded.HasError);
        Assert.True(reloaded.IsReady);
        Assert.Equal(vm.Settings.Endpoint, reloaded.Settings.Endpoint);
        Assert.Equal("VisualStudio", reloaded.Settings.CredentialSource);
        Assert.Equal("https://test-resource.services.ai.azure.com/openai/v1/images/edits",
            ImageRules.EditUri(reloaded.Settings.Snapshot()).AbsoluteUri);
        Assert.Equal(0, foundry.Calls);
    }

    [Fact]
    public async Task PersistenceFailureKeepsNewResultAndReenablesCommands()
    {
        using var files = new TestFiles();
        var result = new GenerationResult(TestFiles.Png, "png", DateTimeOffset.UtcNow);
        var history = new FakeHistory { FailSave = true };
        var vm = new MainViewModel(new FakeSettings(), new FakeFoundry((_, _) => Task.FromResult(result)), history);
        await vm.InitializeAsync();
        vm.Prompt = "test";
        vm.References.Add(files.Reference());
        await vm.GenerateCommand.ExecuteAsync(null);
        Assert.Same(result, vm.Result);
        Assert.True(vm.HasResult);
        Assert.True(vm.HasError);
        Assert.False(vm.IsBusy);
        Assert.True(vm.GenerateCommand.CanExecute(null));
    }

    [Fact]
    public async Task ApiFailureAndCancellationPreservePreviousOutputAndPreventDoubleSubmit()
    {
        using var files = new TestFiles();
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var foundry = new FakeFoundry(async (_, token) =>
        {
            started.SetResult();
            await Task.Delay(Timeout.Infinite, token);
            throw new InvalidOperationException();
        });
        var vm = new MainViewModel(new FakeSettings(), foundry, new FakeHistory());
        await vm.InitializeAsync();
        var previous = new GenerationResult(TestFiles.Png, "png", DateTimeOffset.UtcNow);
        vm.Result = previous;
        vm.Prompt = "test";
        vm.References.Add(files.Reference());
        var generation = vm.GenerateCommand.ExecuteAsync(null);
        await started.Task;
        Assert.False(vm.GenerateCommand.CanExecute(null));
        await vm.GenerateCommand.ExecuteAsync(null);
        Assert.Equal(1, foundry.Calls);
        vm.CancelCommand.Execute(null);
        await generation;
        Assert.Same(previous, vm.Result);
        Assert.Equal(Text.Get("GenerationCancelled"), vm.Status);
        Assert.False(vm.IsBusy);

        foundry.Respond = (_, _) => throw new AppException("API failed");
        await vm.GenerateCommand.ExecuteAsync(null);
        Assert.Same(previous, vm.Result);
        Assert.Contains("API failed", vm.Error);
    }

    [Fact]
    public async Task StartupSettingsFailureDisablesGenerationUntilSuccessfulSave()
    {
        var settings = new FakeSettings { FailLoad = true };
        var vm = new MainViewModel(settings, new FakeFoundry((_, _) => throw new InvalidOperationException()), new FakeHistory());
        await vm.InitializeAsync();
        Assert.False(vm.IsReady);
        Assert.True(vm.HasError);
        Assert.False(vm.GenerateCommand.CanExecute(null));
        vm.Settings.Apply(TestFiles.Settings);
        await vm.SaveSettingsCommand.ExecuteAsync(null);
        Assert.True(vm.IsReady);
    }

    [Fact]
    public async Task HistoryReopenRestoresPromptReferencesAndParameters()
    {
        using var files = new TestFiles();
        var history = new HistoryService(Path.Combine(files.Root, "history"));
        var request = files.Request();
        var item = await history.SaveAsync(TestFiles.Settings, request, new(TestFiles.Png, "png", DateTimeOffset.UtcNow));
        var vm = new MainViewModel(new FakeSettings(), new FakeFoundry((_, _) => throw new InvalidOperationException()), history);
        await vm.InitializeAsync();
        vm.History.SelectedItem = item;
        await vm.OpenHistoryCommand.ExecuteAsync(null);
        Assert.Equal(request.Prompt, vm.Prompt);
        Assert.Equal(request.Size, vm.Size);
        Assert.Equal(request.Quality, vm.Quality);
        Assert.Equal(item.Request.References[0].Path, Assert.Single(vm.References).Path);
        Assert.Equal(TestFiles.Png, vm.Result!.Bytes);
    }
}

internal sealed class FakeSettings : IAppSettingsService
{
    public bool FailLoad { get; init; }
    public Task<AppSettings> LoadAsync() => FailLoad ? throw new AppException("settings failed") : Task.FromResult(TestFiles.Settings);
    public Task SaveAsync(AppSettings settings) => Task.CompletedTask;
}

internal sealed class FakeFoundry(Func<GenerationRequest, CancellationToken, Task<GenerationResult>> respond) : IFoundryImageService
{
    public Func<GenerationRequest, CancellationToken, Task<GenerationResult>> Respond { get; set; } = respond;
    public int Calls { get; private set; }
    public Task<GenerationResult> EditAsync(AppSettings settings, GenerationRequest request, CancellationToken cancellationToken)
    {
        Calls++;
        return Respond(request, cancellationToken);
    }
}

internal sealed class FakeHistory : IHistoryService
{
    public bool FailSave { get; init; }
    public Task<HistoryLoadResult> LoadAsync() => Task.FromResult(new HistoryLoadResult([], []));
    public Task<GenerationResult> ReadOutputAsync(HistoryItem item) => throw new NotSupportedException();
    public Task<HistoryItem> SaveAsync(AppSettings settings, GenerationRequest request, GenerationResult result) =>
        FailSave ? throw new AppException("履歴の保存に失敗しました。結果は保持されています。") :
        Task.FromResult(new HistoryItem("id", result.Created, settings, request, "output.png", "png"));
}
