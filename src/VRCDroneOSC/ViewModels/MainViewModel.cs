using CommunityToolkit.Mvvm.ComponentModel;
using VRCDroneOSC.Models;
using VRCDroneOSC.Services;

namespace VRCDroneOSC.ViewModels;

public partial class MainViewModel : ObservableObject
{
    private readonly ConfigService _configService;
    private readonly InputManager _inputManager;
    private readonly PhysicsEngine _physicsEngine;
    private readonly OscTransport _oscTransport;

    [ObservableProperty] private AppConfig _config;
    [ObservableProperty] private int _selectedTab;
    [ObservableProperty] private string _controllerName = "Not Connected";
    [ObservableProperty] private bool _controllerConnected;
    [ObservableProperty] private bool _oscConnected;

    public FlightLoop? FlightLoop { get; private set; }

    public FlightProfile ActiveProfile =>
        Config.Profiles.GetValueOrDefault(Config.ActiveProfile) ?? new FlightProfile();

    public InputManager InputManager => _inputManager;
    public PhysicsEngine PhysicsEngine => _physicsEngine;
    public OscTransport OscTransport => _oscTransport;
    public ConfigService ConfigService => _configService;

    public MainViewModel()
    {
        // Save Settings.json next to the actual exe (not the temp extraction dir)
        var exePath = Environment.ProcessPath ?? System.Diagnostics.Process.GetCurrentProcess().MainModule?.FileName;
        var exeDir = !string.IsNullOrEmpty(exePath) ? System.IO.Path.GetDirectoryName(exePath)! : System.IO.Directory.GetCurrentDirectory();
        _configService = new ConfigService(System.IO.Path.Combine(exeDir, "Settings.json"));
        // Initialise backing fields directly — required before source-generated
        // properties are observable; avoids MVVMTK0034 in property accessors below.
#pragma warning disable MVVMTK0034
        _config = _configService.Load();
        _selectedTab = _config.LastOpenedPage;
#pragma warning restore MVVMTK0034

        _inputManager = new InputManager();
        _physicsEngine = new PhysicsEngine(ActiveProfile.Physics);
        _oscTransport = new OscTransport();

        _inputManager.ControllerChanged += info =>
        {
            ControllerName = info.Name;
            ControllerConnected = info.IsConnected;
        };

        _oscTransport.ConnectionChanged += connected =>
        {
            OscConnected = connected;
        };
    }

    public void Start()
    {
        _inputManager.Start();
        _oscTransport.Start();
        FlightLoop = new FlightLoop(_inputManager, _physicsEngine, _oscTransport, () => ActiveProfile);
        FlightLoop.Start();
    }

    public void Stop()
    {
        FlightLoop?.Stop();
        _inputManager.Stop();
        _oscTransport.Stop();
    }

    public void SaveConfig()
    {
        Config.LastOpenedPage = SelectedTab;
        _configService.Save(Config);
    }

    public void SwitchProfile(string profileName)
    {
        if (Config.Profiles.ContainsKey(profileName))
        {
            Config.ActiveProfile = profileName;
            _physicsEngine.UpdateSettings(ActiveProfile.Physics);
            SaveConfig();
            OnPropertyChanged(nameof(ActiveProfile));
        }
    }
}
