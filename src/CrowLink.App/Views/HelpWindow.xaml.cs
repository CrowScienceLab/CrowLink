using System.Windows;
using CrowLink.Services.Theming;
using CrowLink.Services.Updates;

namespace CrowLink.Views;

public partial class HelpWindow : Window
{
    public HelpWindow(bool isSkyTheme)
    {
        InitializeComponent();
        SourceInitialized += (_, _) => WindowAppearance.ApplyFrame(this, isSkyTheme);
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e) => Close();

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
                    ($"CrowLink {result.LatestVersion} 버전을 사용할 수 있습니다.\n\n설치 파일 다운로드 페이지를 여시겠습니까?", true),
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
            UpdateButton.Content = "업데이트 확인 · 다운로드";
            UpdateButton.IsEnabled = true;
        }
    }
}
