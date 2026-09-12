using System.Windows;
using CrowLink.Services.Theming;
using CrowLink.ViewModels;
using CrowLink.Services.Updates;

namespace CrowLink.Views;

public partial class SettingsWindow : Window
{
    public SettingsWindow(SettingsViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
        SourceInitialized += (_, _) => WindowAppearance.ApplyFrame(this, viewModel.SelectedTheme.Key != ThemeService.CrowTheme);
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

    private async void UpdateButton_Click(object sender, RoutedEventArgs e)
    {
        UpdateButton.IsEnabled = false;
        UpdateButton.Content = "확인 중…";
        try
        {
            var result = await UpdateService.CheckAsync().ConfigureAwait(true);
            var (message, open) = result.State switch
            {
                UpdateCheckState.UpdateAvailable =>
                    ($"CrowLink {result.LatestVersion} 버전을 사용할 수 있습니다.\n\nGitHub 릴리스 페이지를 열어 Windows 설치 파일을 다운로드하시겠습니까? 다운로드 후 설치 파일을 실행하면 업그레이드됩니다.", true),
                UpdateCheckState.Current =>
                    ($"현재 CrowLink {result.CurrentVersion} 최신 버전을 사용 중입니다.\n\n릴리스 페이지를 여시겠습니까?", true),
                UpdateCheckState.SignInRequired =>
                    ("CrowLink 저장소가 비공개 상태입니다. GitHub에 로그인한 브라우저에서 릴리스와 설치 파일을 확인할 수 있습니다.\n\n릴리스 페이지를 여시겠습니까?", true),
                _ =>
                    ("최신 버전 번호를 자동으로 확인하지 못했습니다.\n\n릴리스 페이지에서 직접 확인하시겠습니까?", true),
            };
            if (open && MessageBox.Show(message, "CrowLink 업데이트", MessageBoxButton.YesNo, MessageBoxImage.Information) == MessageBoxResult.Yes)
            {
                UpdateService.OpenDownload(result);
            }
        }
        catch (Exception exception)
        {
            var open = MessageBox.Show(
                $"업데이트 서버에 연결하지 못했습니다.\n\n{exception.Message}\n\n릴리스 페이지를 직접 여시겠습니까?",
                "CrowLink 업데이트",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);
            if (open == MessageBoxResult.Yes)
            {
                UpdateService.OpenReleasePage();
            }
        }
        finally
        {
            UpdateButton.Content = "업데이트 확인 · GitHub 열기";
            UpdateButton.IsEnabled = true;
        }
    }
}
