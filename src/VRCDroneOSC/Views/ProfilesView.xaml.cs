using System.Windows;
using System.Windows.Controls;
using VRCDroneOSC.ViewModels;

namespace VRCDroneOSC.Views;

public partial class ProfilesView : UserControl
{
    public ProfilesView()
    {
        InitializeComponent();
        Loaded += (_, _) =>
        {
            if (Window.GetWindow(this)?.DataContext is MainViewModel main)
                DataContext = new ProfilesViewModel(main);
        };
    }
}
