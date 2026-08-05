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
    public sealed partial class ComposePage : Page
    {
        private NovelPipeline _pipeline;

        public ComposePage()
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
            if (_pipeline.Novel.Composition != null)
                Render(_pipeline.Novel.Composition);
            else
                StatusText.Text = "点「开始合并」生成简介与目录。";
        }

        private async void OnComposeClick(object sender, RoutedEventArgs e)
        {
            Progress.Visibility = Visibility.Visible;
            StatusText.Text = "AI 正在合并并生成简介 / 目录…";
            ComposeBtn.IsEnabled = false;

            try
            {
                var ctx = new StageContext
                {
                    Profile = DataManager.FindApiProfile(_pipeline.Novel.ApiProfileId) ?? DataManager.ApiProfiles.FirstOrDefault(),
                    PolishProfile = DataManager.FindApiProfile(AppSettings.PolishApiProfileId) ?? DataManager.ApiProfiles.FirstOrDefault(),
                };
                var comp = await _pipeline.ComposeAsync(ctx);
                Render(comp);
                NextBtn.IsEnabled = true;
                StatusText.Text = "合并完成。点「导出 →」继续。";
            }
            catch (System.Exception ex)
            {
                StatusText.Text = "✗ 失败：" + ex.Message;
            }
            finally
            {
                Progress.Visibility = Visibility.Collapsed;
                ComposeBtn.IsEnabled = true;
            }
        }

        private void Render(Composition comp)
        {
            ResultPanel.Children.Clear();

            AddSection("简介", comp.Description ?? "");
            if (comp.Tags != null && comp.Tags.Length > 0)
            {
                AddSection("标签", string.Join("、", comp.Tags));
            }
            AddSection($"合并结果（共 {comp.TotalChapters} 章，{comp.TotalWords} 字）", "");
            if (!string.IsNullOrEmpty(comp.TableOfContents))
            {
                var b = new Border { Style = (Style)Application.Current.Resources["PhoneListItemBorderStyle"] };
                var t = new TextBlock
                {
                    Text = comp.TableOfContents,
                    TextWrapping = TextWrapping.Wrap,
                    Style = (Style)Application.Current.Resources["PhoneListItemDescriptionStyle"],
                };
                b.Child = t;
                ResultPanel.Children.Add(b);
            }
        }

        private void AddSection(string title, string body)
        {
            var t = new TextBlock
            {
                Text = title,
                FontSize = 20,
                FontWeight = Windows.UI.Text.FontWeights.SemiLight,
                Margin = new Thickness(12, 16, 12, 4),
            };
            ResultPanel.Children.Add(t);
            if (!string.IsNullOrEmpty(body))
            {
                var b = new TextBlock
                {
                    Text = body,
                    TextWrapping = TextWrapping.Wrap,
                    Margin = new Thickness(12, 0, 12, 0),
                };
                ResultPanel.Children.Add(b);
            }
        }

        private void OnNextClick(object sender, RoutedEventArgs e)
        {
            Frame.Navigate(typeof(ExportPage), _pipeline.Novel.Id);
        }

        private void OnBackClick(object sender, RoutedEventArgs e)
        {
            if (Frame.CanGoBack) Frame.GoBack(); else Frame.Navigate(typeof(Pages.Library.MainPage));
        }
    }
}
