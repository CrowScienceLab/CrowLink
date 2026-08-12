using System.Windows;
using CrowLink.Services.Theming;
using CrowLink.ViewModels;

namespace CrowLink.Views;

public partial class SettingsWindow : Window
{
    public SettingsWindow(SettingsViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
        SourceInitialized += (_, _) => WindowAppearance.ApplyFrame(this, viewModel.SelectedTheme.Key == ThemeService.SkyTheme);
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            ((SettingsViewModel)DataContext).Apply();
            DialogResult = true;
        }
        catch (Exception exception)
        {
            MessageBox.Show(exception.Message, "설정 확인", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;
}
