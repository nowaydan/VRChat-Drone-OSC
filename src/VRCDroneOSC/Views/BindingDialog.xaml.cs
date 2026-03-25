using System.Windows;
using System.Windows.Controls;
using VRCDroneOSC.Models;

namespace VRCDroneOSC.Views;

public partial class BindingDialog : Window
{
    public ControllerBinding? Result { get; private set; }

    private readonly ControllerBinding? _existing;

    public BindingDialog(ControllerBinding? existing = null)
    {
        InitializeComponent();
        _existing = existing;

        if (existing != null)
        {
            TxtName.Text    = existing.DisplayName;
            TxtAddress.Text = existing.ParameterAddress;

            // Pre-select controller input from first control mapping if available
            if (existing.Controls.Count > 0)
            {
                var ctrl = existing.Controls[0];
                if (ctrl.SdlButton.HasValue)
                    SelectComboByTag(CmbInput, $"Btn{ctrl.SdlButton.Value}");
                else if (ctrl.SdlAxis.HasValue)
                    SelectComboByTag(CmbInput, $"Axis{ctrl.SdlAxis.Value}");
                else
                    SelectComboByTag(CmbInput, $"Axis{ctrl.JoystickOffset}");

                SelectComboByTag(CmbNormalize, ctrl.NormalizeMethod.ToString());
                SelectComboByTag(CmbBehavior, ctrl.Behavior.ToString());
            }
        }

        // Defaults when nothing pre-selected
        if (CmbInput.SelectedIndex < 0)     CmbInput.SelectedIndex = 0;
        if (CmbNormalize.SelectedIndex < 0) CmbNormalize.SelectedIndex = 0;
        if (CmbBehavior.SelectedIndex < 0)  CmbBehavior.SelectedIndex = 0;
    }

    private static void SelectComboByTag(ComboBox combo, string tag)
    {
        foreach (ComboBoxItem item in combo.Items)
        {
            if (item.Tag?.ToString() == tag)
            {
                combo.SelectedItem = item;
                return;
            }
        }
    }

    private void SaveClick(object sender, RoutedEventArgs e)
    {
        var name    = TxtName.Text.Trim();
        var address = TxtAddress.Text.Trim();

        if (string.IsNullOrEmpty(name) || string.IsNullOrEmpty(address))
        {
            TxtValidation.Text       = "Name and Parameter Address are required.";
            TxtValidation.Visibility = Visibility.Visible;
            return;
        }

        var inputTag    = (CmbInput.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "Axis0";
        var normalizeTag = (CmbNormalize.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "None";
        var behaviorTag = (CmbBehavior.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "Continuous";

        var mapping = new ControlMapping
        {
            Guid            = _existing?.Controls.Count > 0 ? _existing.Controls[0].Guid : System.Guid.NewGuid().ToString(),
            NormalizeMethod = Enum.TryParse<NormalizeMethod>(normalizeTag, out var nm) ? nm : NormalizeMethod.None,
            Behavior        = Enum.TryParse<BindingBehavior>(behaviorTag, out var beh) ? beh : BindingBehavior.Continuous
        };

        if (inputTag.StartsWith("Btn") && int.TryParse(inputTag[3..], out int btnIdx))
        {
            mapping.SdlButton      = btnIdx;
            mapping.SdlAxis        = null;
            mapping.JoystickOffset = 0;
        }
        else if (inputTag.StartsWith("Axis") && int.TryParse(inputTag[4..], out int axisIdx))
        {
            mapping.SdlAxis        = axisIdx;
            mapping.SdlButton      = null;
            mapping.JoystickOffset = axisIdx;
        }

        Result = new ControllerBinding
        {
            Guid             = _existing?.Guid ?? System.Guid.NewGuid().ToString(),
            DisplayName      = name,
            ParameterAddress = address,
            Controls         = new System.Collections.Generic.List<ControlMapping> { mapping }
        };

        DialogResult = true;
        Close();
    }

    private void CancelClick(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
