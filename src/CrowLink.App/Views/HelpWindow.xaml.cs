using System.Windows;
using CrowLink.Services.Theming;

namespace CrowLink.Views;

public partial class HelpWindow : Window
{
    public HelpWindow(bool isSkyTheme)
    {
        InitializeComponent();
        SourceInitialized += (_, _) => WindowAppearance.ApplyFrame(this, isSkyTheme);
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e) => Close();
}
