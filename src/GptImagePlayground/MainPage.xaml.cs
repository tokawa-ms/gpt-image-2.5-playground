using GptImagePlayground.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Imaging;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using Windows.ApplicationModel.DataTransfer;
using Windows.Graphics.Imaging;
using Windows.Globalization;
using Windows.Storage;
using Windows.Storage.Pickers;
using Windows.Storage.Streams;

namespace GptImagePlayground;

public sealed record SizePreset(string Label, string Size);

public sealed partial class MainPage : Page
{
    private bool _isInitialized;
    private bool _languageSelectionReady;
    public MainViewModel Vm { get; } = App.Services.GetRequiredService<MainViewModel>();
    public SettingsViewModel SettingsVm => Vm.Settings;
    public HistoryViewModel HistoryVm => Vm.History;
    public IReadOnlyList<string> Qualities => ImageRules.Qualities;
    public IReadOnlyList<string> Formats => ImageRules.Formats;
    public IReadOnlyList<string> Backgrounds => ImageRules.Backgrounds;
    public IReadOnlyList<string> CredentialSources => DeveloperCredentialProvider.Sources;
    public IReadOnlyList<SizePreset> Presets { get; } =
    [
        new(Text.Get("AutoPreset"), "auto"), new("1:1 — 1024 × 1024", "1024x1024"),
        new("3:2 — 1536 × 1024", "1536x1024"), new("2:3 — 1024 × 1536", "1024x1536"),
        new("16:9 — 1536 × 864", "1536x864"), new("9:16 — 864 × 1536", "864x1536"),
        new(Text.Get("Landscape4kPreset"), "3840x2160"), new(Text.Get("Portrait4kPreset"), "2160x3840")
    ];
    public string StorageDescription => Text.Get("StorageDescription", App.LocalFolder);

    public MainPage()
    {
        InitializeComponent();
        Language = Text.ResolveLanguage(CultureInfo.CurrentUICulture.Name);
        LanguageSelector.SelectedValue = Language;
        _languageSelectionReady = true;
        Vm.PropertyChanged += ViewModel_PropertyChanged;
    }

    private void Language_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_languageSelectionReady || LanguageSelector.SelectedValue is not string language) return;
        try
        {
            ApplicationLanguages.PrimaryLanguageOverride = language;
            LanguageNotice.Text = Text.Get("LanguageSaved");
            LanguageNotice.Visibility = Visibility.Visible;
        }
        catch (Exception ex) when (IsUiError(ex))
        {
            Vm.ReportError(ex);
            _languageSelectionReady = false;
            LanguageSelector.SelectedValue = string.IsNullOrEmpty(ApplicationLanguages.PrimaryLanguageOverride)
                ? Language : ApplicationLanguages.PrimaryLanguageOverride;
            _languageSelectionReady = true;
        }
    }

    private async void Page_Loaded(object sender, RoutedEventArgs e)
    {
        if (_isInitialized) return;
        _isInitialized = true;
        await Vm.InitializeAsync();
    }

    private void Preset_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (sender is ComboBox { SelectedItem: SizePreset preset }) Vm.Size = preset.Size;
    }

    private async void AddImages_Click(object sender, RoutedEventArgs e)
    {
        if (Vm.IsBusy) return;
        Vm.IsBusy = true;
        Vm.Error = "";
        try
        {
            var picker = new FileOpenPicker();
            WinRT.Interop.InitializeWithWindow.Initialize(picker, App.WindowHandle);
            picker.FileTypeFilter.Add(".png");
            picker.FileTypeFilter.Add(".jpg");
            picker.FileTypeFilter.Add(".jpeg");
            await ImportFilesAsync(await picker.PickMultipleFilesAsync());
        }
        catch (Exception ex) when (IsUiError(ex)) { Vm.ReportError(ex); }
        finally { Vm.IsBusy = false; }
    }

    private void References_DragOver(object sender, DragEventArgs e)
    {
        e.AcceptedOperation = !Vm.IsBusy && e.DataView.Contains(StandardDataFormats.StorageItems)
            ? DataPackageOperation.Copy : DataPackageOperation.None;
    }

    private async void References_Drop(object sender, DragEventArgs e)
    {
        if (Vm.IsBusy) return;
        var deferral = e.GetDeferral();
        Vm.IsBusy = true;
        Vm.Error = "";
        try
        {
            var items = await e.DataView.GetStorageItemsAsync();
            if (items.Any(item => item is not StorageFile)) throw new AppException(Text.Get("DropImagesOnly"));
            await ImportFilesAsync(items.Cast<StorageFile>().ToArray());
        }
        catch (Exception ex) when (IsUiError(ex)) { Vm.ReportError(ex); }
        finally { Vm.IsBusy = false; deferral.Complete(); }
    }

    private async Task ImportFilesAsync(IReadOnlyList<StorageFile> files)
    {
        if (Vm.References.Count + files.Count > ImageRules.MaxReferences)
            throw new AppException(Text.Get("TooManyReferences"));
        var importer = App.Services.GetRequiredService<ReferenceImporter>();
        foreach (var file in files)
        {
            Vm.Status = Text.Get("ImportingReference", file.Name);
            Vm.References.Add(await importer.ImportAsync(file));
        }
        Vm.Status = Vm.References.Count == 0
            ? Text.Get("NoReferences")
            : Text.Get("ReferenceCount", Vm.References.Count);
    }

    private async void ViewModel_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(MainViewModel.Result) || Vm.Result is not { } result) return;
        try
        {
            using var memory = new InMemoryRandomAccessStream();
            using (var writer = new DataWriter(memory.GetOutputStreamAt(0)))
            {
                writer.WriteBytes(result.Bytes);
                await writer.StoreAsync();
            }
            memory.Seek(0);
            var decoder = await BitmapDecoder.CreateAsync(memory);
            if (decoder.PixelWidth > 3840 || decoder.PixelHeight > 3840 ||
                (long)decoder.PixelWidth * decoder.PixelHeight > 8294400)
                throw new AppException(Text.Get("OutputDimensionsExceeded"));
            var details = Text.Get("OutputDetails", decoder.PixelWidth, decoder.PixelHeight, result.Format.ToUpperInvariant(), result.Bytes.Length / 1024);
            memory.Seek(0);
            var preview = new BitmapImage { DecodePixelWidth = 1600 };
            await preview.SetSourceAsync(memory);
            if (!ReferenceEquals(Vm.Result, result)) return;
            OutputPreview.Source = preview;
            OutputDetails.Text = details;
            PreviewPlaceholder.Visibility = Visibility.Collapsed;
        }
        catch (Exception ex) when (IsUiError(ex))
        {
            if (!ReferenceEquals(Vm.Result, result)) return;
            OutputPreview.Source = null;
            PreviewPlaceholder.Visibility = Visibility.Visible;
            PreviewPlaceholder.Text = Text.Get("PreviewUnavailable");
            OutputDetails.Text = Text.Get("OutputWithoutPreview", result.Format, result.Bytes.Length / 1024);
            Vm.ReportError(new AppException(Text.Get("PreviewDecodeFailed"), ex));
        }
    }

    private async void SaveOutput_Click(object sender, RoutedEventArgs e)
    {
        if (Vm.Result is not { } result) return;
        try
        {
            var picker = new FileSavePicker { SuggestedFileName = $"GPT-image-2.5 Playground-{result.Created:yyyyMMdd-HHmmss}" };
            WinRT.Interop.InitializeWithWindow.Initialize(picker, App.WindowHandle);
            picker.FileTypeChoices.Add(result.Format.ToUpperInvariant(), new List<string> { result.Extension });
            var file = await picker.PickSaveFileAsync();
            if (file is null) return;
            if (!string.Equals(file.FileType, result.Extension, StringComparison.OrdinalIgnoreCase))
                throw new AppException(Text.Get("SaveExtensionMismatch", result.Extension));
            await FileIO.WriteBytesAsync(file, result.Bytes);
            Vm.Status = Text.Get("OutputSaved", file.Name);
        }
        catch (Exception ex) when (IsUiError(ex)) { Vm.ReportError(ex); }
    }

    private async void OpenHistory_Click(object sender, RoutedEventArgs e)
    {
        if (Vm.History.SelectedItem is null) { Vm.Error = Text.Get("SelectHistory"); return; }
        await Vm.OpenHistoryCommand.ExecuteAsync(null);
        if (!Vm.HasError) WorkspaceTabs.SelectedIndex = 0;
    }

    private void Thumbnail_ImageFailed(object sender, ExceptionRoutedEventArgs e) =>
        Vm.Error = Text.Get("ThumbnailFailed");

    private static bool IsUiError(Exception ex) => MainViewModel.IsExpected(ex) || ex is COMException;
}
