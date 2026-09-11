using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

namespace WpfSample;

/// <summary>Sample window used by the flaui-cli tests.</summary>
public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
    }

    private void OnSubmit(object sender, RoutedEventArgs e)
    {
        var color = (colorCombo.SelectedItem as ComboBoxItem)?.Content ?? "none";
        var fruit = (fruitList.SelectedItem as ListBoxItem)?.Content ?? "none";
        resultText.Text = $"Hello, {nameInput.Text}! color={color}, fruit={fruit}, agree={agreeCheck.IsChecked == true}, password={passwordInput.Password.Length}";
        statusText.Text = "Submitted";
    }

    private void OnOpenDialog(object sender, RoutedEventArgs e)
    {
        var ok = new Button { Content = "OK", Width = 80, Margin = new Thickness(10), IsDefault = true };
        System.Windows.Automation.AutomationProperties.SetAutomationId(ok, "dialogOkButton");
        var dialog = new Window
        {
            Title = "Confirm",
            Owner = this,
            Width = 260,
            Height = 140,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Content = new StackPanel
            {
                Children = { new TextBlock { Text = "Are you sure?", Margin = new Thickness(10) }, ok },
            },
        };
        ok.Click += (_, _) => dialog.DialogResult = true;
        statusText.Text = dialog.ShowDialog() == true ? "Dialog accepted" : "Dialog cancelled";
    }

    private void OnDelayed(object sender, RoutedEventArgs e)
    {
        statusText.Text = "Waiting...";
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1200) };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            statusText.Text = "Delayed done";
        };
        timer.Start();
    }

    private void OnReset(object sender, RoutedEventArgs e)
    {
        nameInput.Text = "";
        passwordInput.Password = "";
        colorCombo.SelectedIndex = -1;
        fruitList.SelectedIndex = -1;
        agreeCheck.IsChecked = false;
        resultText.Text = "";
        statusText.Text = "Ready";
    }

    private void OnExit(object sender, RoutedEventArgs e) => Close();
}
