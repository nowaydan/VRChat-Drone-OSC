using System.Windows;
using System.Windows.Controls;
using VRCDroneOSC.ViewModels;

namespace VRCDroneOSC.Views;

public partial class FlightView : UserControl
{
    public FlightView()
    {
        InitializeComponent();
        Loaded += (_, _) =>
        {
            if (Window.GetWindow(this)?.DataContext is MainViewModel main)
                DataContext = new FlightViewModel(main);
        };
    }
}
