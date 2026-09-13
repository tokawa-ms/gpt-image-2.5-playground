using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Collections.ObjectModel;

namespace GptImagePlayground.Core;

public partial class SettingsViewModel : ObservableObject
{
    [ObservableProperty] private string _endpoint = "";
    [ObservableProperty] private string _deployment = "gpt-image-2.5-sunburst";
    [ObservableProperty] private string _defaultSize = "auto";
    [ObservableProperty] private string _defaultQuality = "auto";
    [ObservableProperty] private string _credentialSource = "AzureCli";
    public AppSettings Snapshot() => new(Endpoint.Trim(), Deployment.Trim(), DefaultSize.Trim(), DefaultQuality, CredentialSource);
    public void Apply(AppSettings settings)
    {
        Endpoint = settings.Endpoint;
        Deployment = settings.Deployment;
        DefaultSize = settings.DefaultSize;
        DefaultQuality = settings.DefaultQuality;
        CredentialSource = settings.CredentialSource;
    }
}

public partial class HistoryViewModel : ObservableObject
{
    public ObservableCollection<HistoryItem> Items { get; } = [];
    [ObservableProperty] private HistoryItem? _selectedItem;
    public string SelectedDetails => SelectedItem?.Details ?? Text.Get("SelectHistoryHint");
    partial void OnSelectedItemChanged(HistoryItem? value) => OnPropertyChanged(nameof(SelectedDetails));
}

public partial class MainViewModel(
    IAppSettingsService settingsService,
    IFoundryImageService foundryService,
    IHistoryService historyService) : ObservableObject
{
    private AppSettings? _activeSettings;
    private CancellationTokenSource? _generationCancellation;
    public SettingsViewModel Settings { get; } = new();
    public HistoryViewModel History { get; } = new();
    public ObservableCollection<ReferenceImage> References { get; } = [];
    [ObservableProperty] private string _prompt = "";
    [ObservableProperty] private string _size = "auto";
    [ObservableProperty] private string _quality = "auto";
    [ObservableProperty] private string _outputFormat = "png";
    [ObservableProperty] private string _background = "auto";
    [ObservableProperty] private string _status = Text.Get("LoadingSettings");
    [ObservableProperty] private string _error = "";
    [ObservableProperty] private GenerationResult? _result;
    [ObservableProperty] private bool _isReady;
    [ObservableProperty] private bool _isBusy;
    public bool IsIdle => !IsBusy;
    public bool HasError => Error.Length > 0;
    public bool HasResult => Result is not null;
    public string SizeDescription
    {
        get
        {
            try
            {
                var dimensions = ImageRules.ValidateSize(Size);
                if (dimensions is null) return Text.Get("AutoSizeDescription");
                return $"{dimensions.Value.Width} × {dimensions.Value.Height} px" +
                    (ImageRules.IsExperimental(Size) ? Text.Get("ExperimentalSize") : "");
            }
            catch (AppException ex) { return ex.Message; }
        }
    }
    public bool CanGenerate => IsReady && IsIdle;
    public bool CanCancel => _generationCancellation is { IsCancellationRequested: false };

    partial void OnSizeChanged(string value) => OnPropertyChanged(nameof(SizeDescription));
    partial void OnErrorChanged(string value) => OnPropertyChanged(nameof(HasError));
    partial void OnResultChanged(GenerationResult? value) => OnPropertyChanged(nameof(HasResult));
    partial void OnIsReadyChanged(bool value) => GenerateCommand.NotifyCanExecuteChanged();
    partial void OnIsBusyChanged(bool value)
    {
        OnPropertyChanged(nameof(IsIdle));
        GenerateCommand.NotifyCanExecuteChanged();
        SaveSettingsCommand.NotifyCanExecuteChanged();
        ReloadHistoryCommand.NotifyCanExecuteChanged();
        OpenHistoryCommand.NotifyCanExecuteChanged();
        RemoveReferenceCommand.NotifyCanExecuteChanged();
        MoveUpCommand.NotifyCanExecuteChanged();
        MoveDownCommand.NotifyCanExecuteChanged();
    }

    public async Task InitializeAsync()
    {
        IsBusy = true;
        try
        {
            _activeSettings = await settingsService.LoadAsync();
            Settings.Apply(_activeSettings);
            Size = _activeSettings.DefaultSize;
            Quality = _activeSettings.DefaultQuality;
            IsReady = !string.IsNullOrWhiteSpace(_activeSettings.Endpoint);
            Status = IsReady ? Text.Get("ReadyPrompt") : Text.Get("SetupRequired");
        }
        catch (Exception ex) when (IsExpected(ex))
        {
            ReportError(ex);
            Status = Text.Get("SettingsUnavailable");
        }
        await LoadHistoryCoreAsync();
        IsBusy = false;
    }

    [RelayCommand(CanExecute = nameof(IsIdle))]
    private async Task SaveSettingsAsync()
    {
        IsBusy = true;
        Error = "";
        try
        {
            var candidate = Settings.Snapshot();
            await settingsService.SaveAsync(candidate);
            _activeSettings = candidate;
            IsReady = true;
            Size = candidate.DefaultSize;
            Quality = candidate.DefaultQuality;
            Status = Text.Get("SettingsSaved");
        }
        catch (Exception ex) when (IsExpected(ex)) { ReportError(ex); }
        finally { IsBusy = false; }
    }

    [RelayCommand(CanExecute = nameof(CanGenerate))]
    private async Task GenerateAsync()
    {
        if (IsBusy || _activeSettings is null) return;
        var settings = _activeSettings;
        var request = new GenerationRequest(Prompt, Size.Trim(), Quality, OutputFormat, Background, References.ToArray());
        Error = "";
        IsBusy = true;
        using var cancellation = new CancellationTokenSource();
        _generationCancellation = cancellation;
        CancelCommand.NotifyCanExecuteChanged();
        try
        {
            ImageRules.Validate(request);
            Status = request.References.Count == 0
                ? Text.Get("Generating")
                : Text.Get("Editing");
            var generated = await Task.Run(() => foundryService.EditAsync(settings, request, cancellation.Token));
            Result = generated;
            Status = Text.Get("SavingHistory");
            _generationCancellation = null;
            CancelCommand.NotifyCanExecuteChanged();
            var item = await Task.Run(() => historyService.SaveAsync(settings, request, generated));
            History.Items.Insert(0, item);
            Status = request.References.Count == 0
                ? Text.Get("GeneratedWithoutReferences")
                : Text.Get("GeneratedWithReferences");
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
            Status = Text.Get("GenerationCancelled");
        }
        catch (Exception ex) when (IsExpected(ex))
        {
            ReportError(ex);
            Status = Text.Get("OperationFailed");
        }
        finally
        {
            _generationCancellation = null;
            CancelCommand.NotifyCanExecuteChanged();
            IsBusy = false;
        }
    }

    [RelayCommand(CanExecute = nameof(CanCancel))]
    private void Cancel()
    {
        _generationCancellation?.Cancel();
        CancelCommand.NotifyCanExecuteChanged();
    }

    [RelayCommand(CanExecute = nameof(IsIdle))]
    private void RemoveReference(ReferenceImage? reference)
    {
        if (reference is not null) References.Remove(reference);
    }

    [RelayCommand(CanExecute = nameof(IsIdle))]
    private void MoveUp(ReferenceImage? reference)
    {
        var index = reference is null ? -1 : References.IndexOf(reference);
        if (index > 0) References.Move(index, index - 1);
    }

    [RelayCommand(CanExecute = nameof(IsIdle))]
    private void MoveDown(ReferenceImage? reference)
    {
        var index = reference is null ? -1 : References.IndexOf(reference);
        if (index >= 0 && index < References.Count - 1) References.Move(index, index + 1);
    }

    [RelayCommand(CanExecute = nameof(IsIdle))]
    private async Task ReloadHistoryAsync()
    {
        IsBusy = true;
        Error = "";
        try { await LoadHistoryCoreAsync(); }
        finally { IsBusy = false; }
    }

    private async Task LoadHistoryCoreAsync()
    {
        try
        {
            var loaded = await Task.Run(historyService.LoadAsync);
            History.Items.Clear();
            foreach (var item in loaded.Items) History.Items.Add(item);
            if (loaded.Errors.Count > 0)
                Error = string.Join("\n", new[] { Error }.Where(x => x.Length > 0).Concat(loaded.Errors));
        }
        catch (Exception ex) when (IsExpected(ex)) { ReportError(ex); }
    }

    [RelayCommand(CanExecute = nameof(IsIdle))]
    private async Task OpenHistoryAsync()
    {
        if (History.SelectedItem is not { } item) return;
        IsBusy = true;
        Error = "";
        try
        {
            var result = await historyService.ReadOutputAsync(item);
            foreach (var reference in item.Request.References)
                await ImageRules.ValidateReferenceAsync(reference.Path);
            Result = result;
            Prompt = item.Request.Prompt;
            Size = item.Request.Size;
            Quality = item.Request.Quality;
            OutputFormat = item.Request.OutputFormat;
            Background = item.Request.Background;
            References.Clear();
            foreach (var reference in item.Request.References) References.Add(reference);
            Status = Text.Get("HistoryOpened");
        }
        catch (Exception ex) when (IsExpected(ex)) { ReportError(ex); }
        finally { IsBusy = false; }
    }

    public void ReportError(Exception exception) => Error = exception switch
    {
        AppException => exception.Message,
        UnauthorizedAccessException => Text.Get("FileAccessDenied"),
        IOException => Text.Get("FileIoFailed", exception.Message),
        _ => Text.Get("UnexpectedFailure", exception.Message)
    };

    public static bool IsExpected(Exception ex) => ex is AppException or IOException or UnauthorizedAccessException
        or System.Text.Json.JsonException or ArgumentException or NotSupportedException;
}
