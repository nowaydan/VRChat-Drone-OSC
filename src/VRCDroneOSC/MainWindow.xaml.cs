using System.Windows;
using VRCDroneOSC.ViewModels;

namespace VRCDroneOSC;

public partial class MainWindow : Window
{
    private readonly MainViewModel _vm;

    public MainWindow()
    {
        InitializeComponent();
        _vm = new MainViewModel();
        DataContext = _vm;
        Loaded += (_, _) => _vm.Start();
        Closing += (_, _) =>
        {
            _vm.SaveConfig();
            _vm.Stop();
        };
    }
}
