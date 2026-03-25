using System.Windows;
using System.Windows.Controls;
using VRCDroneOSC.ViewModels;

namespace VRCDroneOSC.Views;

public partial class RatesView : UserControl
{
    public RatesView()
    {
        InitializeComponent();
        Loaded += (_, _) =>
        {
            if (Window.GetWindow(this)?.DataContext is MainViewModel main)
                DataContext = new RatesViewModel(main);
        };
    }
}
