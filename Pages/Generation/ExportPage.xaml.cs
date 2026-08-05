using System;
using System.Linq;
using System.Text;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Navigation;
using novelgen.AI;
using novelgen.Core;
using novelgen.Models;
using novelgen.Pipeline;

namespace novelgen.Pages.Generation
{
    public sealed partial class ExportPage : Page
    {
        private NovelPipeline _pipeline;

        public ExportPage()
        {
            this.InitializeComponent();
        }

        protected override async void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);
            _pipeline = PipelineHost.Current;
            if (_pipeline == null && e.Parameter is string nid)
                _pipeline = await PipelineHost.ResumeAsync(nid);
            if (_pipeline == null) { if (Frame.CanGoBack) Frame.GoBack(); return; }
            RenderRecord();
        }

        private void RenderRecord()
        {
            var rec = _pipeline.Novel.LastExport;
            if (rec == null) { RecordText.Text = "（还没有导出）"; return; }
            RecordText.Text = $"格式：{rec.Format}\r\n时间：{rec.ExportedAt}\r\n章节：{rec.ChapterCount}\r\n字数：{rec.WordCount}\r\n大小：{rec.FileSizeBytes / 1024.0:F1} KB\r\n路径：{rec.FilePath}";
        }

        private async void OnExportClick(object sender, RoutedEventArgs e)
        {
            var format = FormatEpub.IsChecked == true ? "epub" : "txt";
            ExportBtn.IsEnabled = false; // 防止双击触发两个并发导出
            Progress.Visibility = Visibility.Visible;
            StatusText.Text = "正在导出…";

            try
            {
                var ctx = new StageContext
                {
                    Profile = DataManager.FindApiProfile(_pipeline.Novel.ApiProfileId) ?? DataManager.ApiProfiles.FirstOrDefault(),
                };
                var rec = await _pipeline.ExportAsync(format, ctx);
                if (rec == null)
                {
                    // 用户取消了保存对话框，不算成功也不算失败
                    StatusText.Text = "";
                    return;
                }
                StatusText.Text = "✓ 导出完成";
                RenderRecord();
            }
            catch (OperationCanceledException)
            {
                StatusText.Text = "已取消";
            }
            catch (Exception ex)
            {
                StatusText.Text = "✗ 失败：" + ex.Message;
            }
            finally
            {
                Progress.Visibility = Visibility.Collapsed;
                ExportBtn.IsEnabled = true;
            }
        }

        private void OnBackClick(object sender, RoutedEventArgs e)
        {
            if (Frame.CanGoBack) Frame.GoBack(); else Frame.Navigate(typeof(Pages.Library.MainPage));
        }
    }
}
