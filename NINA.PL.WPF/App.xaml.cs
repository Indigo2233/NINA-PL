using System.IO;
using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NLog;
using NLog.Config;
using NLog.Extensions.Logging;
using NLog.Targets;
using NINA.PL.AutoFocus;
using NINA.PL.Capture;
using NINA.PL.Core;
using CoreLogger = NINA.PL.Core.Logger;
using NINA.PL.Equipment.Camera;
using NINA.PL.Equipment.Camera.Sdk;
using NINA.PL.Equipment.Mount;
using NINA.PL.Equipment.Focuser;
using NINA.PL.Equipment.FilterWheel;
using NINA.PL.Equipment.Etalon;
using NINA.PL.Guider;
using NINA.PL.LiveStack;
using NINA.PL.Profile;
using NINA.PL.WPF.ViewModels;
using NINA.PL.WPF.Views;
namespace NINA.PL.WPF;

public partial class App : Application
{
    private ServiceProvider? _serviceProvider;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        LoadOrCreateProfile();
        var savedLang = ProfileManager.Instance.ActiveProfile.Language;
        Localization.LocalizationManager.SetLanguage(
            string.IsNullOrWhiteSpace(savedLang) ? "en" : savedLang);

        DispatcherUnhandledException += (_, args) =>
        {
            File.AppendAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "crash.log"),
                $"[{DateTime.Now:O}] UNHANDLED: {args.Exception}\n\n");
            args.Handled = false;
        };

        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
        {
            File.AppendAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "crash.log"),
                $"[{DateTime.Now:O}] DOMAIN: {args.ExceptionObject}\n\n");
        };

        try
        {
        ConfigureNLog();

        var services = new ServiceCollection();
        services.AddLogging(builder =>
        {
            builder.ClearProviders();
            builder.SetMinimumLevel(Microsoft.Extensions.Logging.LogLevel.Trace);
            builder.AddNLog();
        });

        RegisterNativeCameraBackends();
        services.AddSingleton(_ => new NativeCameraProvider(NativeBackendFactory.CreateAllBackends()));
        services.AddSingleton<AscomCameraProvider>();
        services.AddSingleton<AscomMountProvider>();
        services.AddSingleton<AscomFocuserProvider>();
        services.AddSingleton<AscomFilterWheelProvider>();
        services.AddSingleton<AscomEtalonProvider>();
        services.AddSingleton(provider =>
        {
            var mediator = new CameraMediator();
            mediator.RegisterProvider(provider.GetRequiredService<NativeCameraProvider>());
            mediator.RegisterProvider(provider.GetRequiredService<AscomCameraProvider>());
            return mediator;
        });
        services.AddSingleton(provider =>
        {
            var mediator = new MountMediator();
            mediator.RegisterProvider(provider.GetRequiredService<AscomMountProvider>());
            return mediator;
        });
        services.AddSingleton(provider =>
        {
            var mediator = new FocuserMediator();
            mediator.RegisterProvider(provider.GetRequiredService<AscomFocuserProvider>());
            return mediator;
        });
        services.AddSingleton(provider =>
        {
            var mediator = new FilterWheelMediator();
            mediator.RegisterProvider(provider.GetRequiredService<AscomFilterWheelProvider>());
            return mediator;
        });
        services.AddSingleton(provider =>
        {
            var mediator = new EtalonMediator();
            mediator.RegisterProvider(provider.GetRequiredService<AscomEtalonProvider>());
            return mediator;
        });
        services.AddSingleton<FlatDeviceMediator>();
        services.AddSingleton<SwitchMediator>();
        services.AddSingleton<RotatorMediator>();
        services.AddSingleton<CaptureEngine>();
        services.AddSingleton<AutoFocusEngine>();
        services.AddSingleton<PlanetaryGuider>();
        services.AddSingleton<LiveStackEngine>();

        services.AddSingleton(sp => new EquipmentViewModel(
            sp.GetRequiredService<CameraMediator>(),
            sp.GetRequiredService<MountMediator>(),
            sp.GetRequiredService<FocuserMediator>(),
            sp.GetRequiredService<FilterWheelMediator>(),
            sp.GetRequiredService<EtalonMediator>()));
        services.AddSingleton(sp => new CaptureViewModel(
            sp.GetRequiredService<CameraMediator>(),
            sp.GetRequiredService<FilterWheelMediator>(),
            sp.GetRequiredService<CaptureEngine>()));
        services.AddSingleton<FocusViewModel>();
        services.AddSingleton<GuiderViewModel>();
        services.AddSingleton<LiveStackViewModel>();
        services.AddSingleton<SettingsPanelViewModel>();
        services.AddSingleton(sp => new SequencerPanelViewModel(
            sp.GetRequiredService<CameraMediator>(),
            sp.GetRequiredService<MountMediator>(),
            sp.GetRequiredService<FocuserMediator>(),
            sp.GetRequiredService<FilterWheelMediator>(),
            sp.GetRequiredService<CaptureEngine>(),
            sp.GetRequiredService<AutoFocusEngine>(),
            sp.GetRequiredService<PlanetaryGuider>(),
            sp.GetRequiredService<FlatDeviceMediator>(),
            sp.GetRequiredService<SwitchMediator>(),
            sp.GetRequiredService<RotatorMediator>(),
            sp.GetRequiredService<SettingsPanelViewModel>()));
        services.AddSingleton<MainViewModel>();
        services.AddSingleton(sp => new MainWindow(sp.GetRequiredService<MainViewModel>()));

        _serviceProvider = services.BuildServiceProvider();

        var loggerFactory = _serviceProvider.GetRequiredService<ILoggerFactory>();
        CoreLogger.Initialize(loggerFactory);

        var settings = _serviceProvider.GetRequiredService<SettingsPanelViewModel>();
        settings.RestoreFromProfile();

        var mainWindow = _serviceProvider.GetRequiredService<MainWindow>();
        mainWindow.Show();
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.ToString(), "NINA-PL Startup Error", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(1);
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        if (_serviceProvider is not null)
        {
            _serviceProvider.GetService<CaptureViewModel>()?.Dispose();
            _serviceProvider.GetService<FocusViewModel>()?.Dispose();
            _serviceProvider.GetService<GuiderViewModel>()?.Dispose();
            _serviceProvider.GetService<LiveStackViewModel>()?.Dispose();
            _serviceProvider.GetService<LiveStackEngine>()?.Dispose();
            (_serviceProvider.GetService<SequencerPanelViewModel>() as IDisposable)?.Dispose();
            _serviceProvider.GetService<CameraMediator>()?.Dispose();
            _serviceProvider.GetService<MountMediator>()?.Dispose();
            _serviceProvider.GetService<FocuserMediator>()?.Dispose();
            _serviceProvider.GetService<FilterWheelMediator>()?.Dispose();
            _serviceProvider.GetService<EtalonMediator>()?.Dispose();
            _serviceProvider.GetService<FlatDeviceMediator>()?.Dispose();
            _serviceProvider.GetService<SwitchMediator>()?.Dispose();
            _serviceProvider.GetService<RotatorMediator>()?.Dispose();
            _serviceProvider.GetService<NativeCameraProvider>()?.Dispose();

            _serviceProvider.Dispose();
            _serviceProvider = null;
        }

        base.OnExit(e);
    }

    private static void LoadOrCreateProfile()
    {
        try
        {
            var dir = ProfileManager.GetProfilesDirectory();
            Directory.CreateDirectory(dir);
            var path = ProfileManager.GetDefaultPath();
            if (File.Exists(path))
                ProfileManager.Instance.Load(path);
            else
                ProfileManager.Instance.CreateDefault();
        }
        catch
        {
            ProfileManager.Instance.CreateDefault();
        }
    }

    private static void RegisterNativeCameraBackends()
    {
        NativeBackendFactory.Register(() =>
        {
            var b = new ToupcamBackend();
            b.Initialize();
            return b;
        });
        NativeBackendFactory.Register(() =>
        {
            var b = new ToupcamBackend("ogmacam");
            b.Initialize();
            return b;
        });
        NativeBackendFactory.Register(() =>
        {
            var b = new ZwoAsiBackend();
            b.Initialize();
            return b;
        });
        NativeBackendFactory.Register(() =>
        {
            var b = new QhyCcdBackend();
            b.Initialize();
            return b;
        });
        NativeBackendFactory.Register(() =>
        {
            var b = new PlayerOneBackend();
            b.Initialize();
            return b;
        });
    }

    private static void ConfigureNLog()
    {
        string logDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "NINA-PL",
            "logs");
        Directory.CreateDirectory(logDir);

        var config = new LoggingConfiguration();
        var console = new ConsoleTarget("console")
        {
            Layout = "${longdate}|${level:uppercase=true}|${logger}|${message} ${exception:format=tostring}",
        };
        var file = new FileTarget("file")
        {
            FileName = Path.Combine(logDir, "nina-pl-${shortdate}.log"),
            Layout = "${longdate}|${level:uppercase=true}|${logger}|${message} ${exception:format=tostring}",
            ArchiveEvery = FileArchivePeriod.Day,
            MaxArchiveFiles = 14,
        };

        config.AddRule(NLog.LogLevel.Info, NLog.LogLevel.Fatal, console);
        config.AddRule(NLog.LogLevel.Debug, NLog.LogLevel.Fatal, file);
        NLog.LogManager.Configuration = config;
    }
}
