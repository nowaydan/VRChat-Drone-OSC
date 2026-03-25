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

    private void ProfileListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (DataContext is ProfilesViewModel vm
            && sender is ListBox lb
            && lb.SelectedItem is string selectedName
            && selectedName != vm.SelectedProfileName)
        {
            vm.SwitchProfileCommand.Execute(selectedName);
        }
    }
}
