using System.Windows;
using System.Windows.Controls;
using VRCDroneOSC.ViewModels;

namespace VRCDroneOSC.Views;

public partial class ControllerView : UserControl
{
    public ControllerView()
    {
        InitializeComponent();
        Loaded += (_, _) =>
        {
            if (Window.GetWindow(this)?.DataContext is MainViewModel main)
                DataContext = new ControllerViewModel(main);
        };
    }
}
