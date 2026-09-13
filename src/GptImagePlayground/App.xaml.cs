using GptImagePlayground.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System.IO;
using System.Globalization;
using System.Runtime.InteropServices;
using Windows.Globalization;
using Windows.Storage;
using Windows.System.UserProfile;

namespace GptImagePlayground;

public partial class App : Application
{
    private Window? _window;
    public static ServiceProvider Services { get; private set; } = null!;
    public static string LocalFolder => ApplicationData.Current.LocalFolder.Path;
    public static IntPtr WindowHandle { get; private set; }

    public App() => InitializeComponent();

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        try
        {
            var language = ApplicationLanguages.PrimaryLanguageOverride;
            if (string.IsNullOrEmpty(language)) language = GlobalizationPreferences.Languages.FirstOrDefault();
            var culture = CultureInfo.GetCultureInfo(Text.ResolveLanguage(language));
            CultureInfo.DefaultThreadCurrentUICulture = culture;
            CultureInfo.CurrentUICulture = culture;
            CreateMainWindow();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or COMException or AppException)
        {
            _window = new Window
            {
                Title = Text.Get("StartupErrorTitle"),
                Content = new TextBlock
                {
                    Text = Text.Get("StartupError", ex.Message),
                    TextWrapping = TextWrapping.Wrap,
                    Margin = new Thickness(24)
                }
            };
            _window.AppWindow.SetIcon(System.IO.Path.Combine(AppContext.BaseDirectory, "Assets", "AppIcon.ico"));
            _window.Activate();
        }
    }

    private void CreateMainWindow()
    {
        var services = new ServiceCollection();
        services.AddSingleton<DeveloperCredentialProvider>();
        services.AddSingleton(_ => FoundryImageService.CreateHttpClient());
        services.AddSingleton<IFoundryImageService>(provider => new FoundryImageService(
            provider.GetRequiredService<System.Net.Http.HttpClient>(),
            provider.GetRequiredService<DeveloperCredentialProvider>().GetCredential));
        services.AddSingleton<IAppSettingsService>(_ => new AppSettingsService(
            System.IO.Path.Combine(AppContext.BaseDirectory, "appsettings.json"),
            System.IO.Path.Combine(LocalFolder, "settings.json")));
        services.AddSingleton<IHistoryService>(_ => new HistoryService(System.IO.Path.Combine(LocalFolder, "history")));
        services.AddSingleton<MainViewModel>();
        services.AddSingleton<ReferenceImporter>();
        Services = services.BuildServiceProvider();
        _window = new MainWindow();
        WindowHandle = WinRT.Interop.WindowNative.GetWindowHandle(_window);
        _window.Activate();
    }
}
