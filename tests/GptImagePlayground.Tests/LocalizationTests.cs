using System.Collections;
using System.Globalization;
using System.Resources;
using System.Text;
using GptImagePlayground.Core;

namespace GptImagePlayground.Tests;

public class LocalizationTests
{
    [Theory]
    [InlineData("ja-JP", "ja-JP")]
    [InlineData("ja", "ja-JP")]
    [InlineData("JA-jp", "ja-JP")]
    [InlineData("en-GB", "en-US")]
    [InlineData("fr-FR", "en-US")]
    [InlineData("", "en-US")]
    [InlineData(null, "en-US")]
    public void ResolvesSupportedLanguageWithEnglishFallback(string? requested, string expected) =>
        Assert.Equal(expected, Text.ResolveLanguage(requested));

    [Fact]
    public void EveryResourceHasBothTranslationsAndMatchingFormatArguments()
    {
        var manager = new ResourceManager("GptImagePlayground.Core.Strings", typeof(Text).Assembly);
        var english = manager.GetResourceSet(CultureInfo.InvariantCulture, true, false)!;
        var japanese = manager.GetResourceSet(CultureInfo.GetCultureInfo("ja"), true, false)!;
        Assert.NotNull(english);
        Assert.NotNull(japanese);
        var keys = english.Cast<DictionaryEntry>().Select(entry => (string)entry.Key).Order().ToArray();
        Assert.Equal(keys, japanese.Cast<DictionaryEntry>().Select(entry => (string)entry.Key).Order().ToArray());
        Assert.True(keys.Length >= 136);
        foreach (var key in keys)
        {
            var en = english.GetString(key)!;
            var ja = japanese.GetString(key)!;
            Assert.False(string.IsNullOrWhiteSpace(en), key);
            Assert.False(string.IsNullOrWhiteSpace(ja), key);
            Assert.NotEqual(en, ja);
            Assert.Equal(CompositeFormat.Parse(en).MinimumArgumentCount, CompositeFormat.Parse(ja).MinimumArgumentCount);
            var arguments = Enumerable.Range(0, CompositeFormat.Parse(en).MinimumArgumentCount).Cast<object>().ToArray();
            _ = string.Format(CultureInfo.InvariantCulture, en, arguments);
            _ = string.Format(CultureInfo.InvariantCulture, ja, arguments);
        }
    }

    [Theory]
    [InlineData("en-US", "Generate image", "Resolution is empty.")]
    [InlineData("ja-JP", "画像を生成", "解像度が空です。")]
    public async Task LabelsValidationHistoryAndWorkerMessagesUseSelectedCulture(
        string language, string button, string error)
    {
        var previous = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(language);
            Assert.Equal(button, Text.Get("Generate"));
            Assert.Equal(error, Assert.Throws<AppException>(() => ImageRules.ValidateSize("")).Message);
            Assert.Equal(error, await Task.Run(() => Assert.Throws<AppException>(() => ImageRules.ValidateSize("")).Message));
            var vm = new MainViewModel(new FakeSettings(),
                new FakeFoundry((_, _) => throw new AppException(Text.Get("NetworkFailed"))), new FakeHistory());
            await vm.InitializeAsync();
            Assert.Equal(Text.Get("ReadyPrompt"), vm.Status);
            Assert.Equal(Text.Get("SelectHistoryHint"), vm.History.SelectedDetails);
            vm.Prompt = "Keep this prompt unchanged / 変更しない";
            await vm.GenerateCommand.ExecuteAsync(null);
            Assert.Equal(Text.Get("NetworkFailed"), vm.Error);
            Assert.Equal(Text.Get("OperationFailed"), vm.Status);
            Assert.Equal("Keep this prompt unchanged / 変更しない", vm.Prompt);
            Assert.Equal("auto", vm.Quality);
            Assert.Equal("png", vm.OutputFormat);
            Assert.Throws<MissingManifestResourceException>(() => Text.Get("MissingResourceForTest"));
        }
        finally { CultureInfo.CurrentUICulture = previous; }
    }
}
