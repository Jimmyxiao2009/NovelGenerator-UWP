using System.Linq;
using System.Threading;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Navigation;
using novelgen.Core;
using novelgen.Models;
using novelgen.Pipeline;

namespace novelgen.Pages.Skeleton
{
    public sealed partial class SkeletonPage : Page
    {
        private NovelPipeline _pipeline;
        private CancellationTokenSource _cts;   // 离开页面时取消生成
        private bool _isGenerating;             // 防止重入导致并发生成

        public SkeletonPage()
        {
            this.InitializeComponent();
        }

        protected override async void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);
            _pipeline = PipelineHost.Current;
            if (_pipeline == null && e.Parameter is string nid)
                _pipeline = await PipelineHost.ResumeAsync(nid);
            if (_pipeline == null)
            {
                if (Frame.CanGoBack) Frame.GoBack(); else Frame.Navigate(typeof(Pages.Library.MainPage));
                return;
            }

            if (_pipeline.Novel.Skeleton != null)
            {
                Render(_pipeline.Novel.Skeleton);
                NextButton.IsEnabled = true;
                return;
            }

            if (_isGenerating) return; // 已在生成中，避免再起一个并发任务
            _isGenerating = true;
            _cts = new CancellationTokenSource();
            var ct = _cts.Token;
            Progress.Visibility = Visibility.Visible;
            StatusText.Text = "调用 AI 生成故事骨架…";
            try
            {
                var ctx = BuildContext();
                ctx.Ct = ct;
                var sk = await _pipeline.GenerateStorySkeletonAsync(ctx);
                if (ct.IsCancellationRequested) return; // 页面已离开，不再碰 UI
                Render(sk);
                NextButton.IsEnabled = true;
                StatusText.Text = "已生成。点「Gate1 审查 →」继续。";
            }
            catch (System.OperationCanceledException) { /* 用户取消，静默 */ }
            catch (System.Exception ex)
            {
                if (!ct.IsCancellationRequested)
                    StatusText.Text = "✗ 生成失败：" + ex.Message;
            }
            finally
            {
                _isGenerating = false;
                if (!ct.IsCancellationRequested)
                    Progress.Visibility = Visibility.Collapsed;
            }
        }

        protected override void OnNavigatedFrom(NavigationEventArgs e)
        {
            base.OnNavigatedFrom(e);
            if (_cts != null) { _cts.Cancel(); _cts.Dispose(); _cts = null; }
        }

        private StageContext BuildContext()
        {
            return new StageContext
            {
                Profile = ResolveProfile(AppSettings.DraftApiProfileId),
                PolishProfile = ResolveProfile(AppSettings.PolishApiProfileId),
            };
        }

        private ApiProfile ResolveProfile(string id)
        {
            if (!string.IsNullOrEmpty(id))
            {
                var p = DataManager.FindApiProfile(id);
                if (p != null) return p;
            }
            if (!string.IsNullOrEmpty(_pipeline.Novel.ApiProfileId))
            {
                var p = DataManager.FindApiProfile(_pipeline.Novel.ApiProfileId);
                if (p != null) return p;
            }
            return DataManager.ApiProfiles.FirstOrDefault();
        }

        private void Render(StorySkeleton sk)
        {
            ResultScroller.Visibility = Visibility.Visible;
            ResultPanel.Children.Clear();

            AddSection("一句话", sk.Logline);
            AddSection("三幕结构", sk.ThreeAct);
            AddSection($"共 {sk.TotalChapters} 章 / {sk.Volumes.Count} 卷", "");

            foreach (var vol in sk.Volumes)
            {
                AddSection("卷 · " + vol.Title, vol.Synopsis);
                AddItem("目标", vol.PlotGoal, "");
                AddItem("高潮", vol.Climax, "");

                int n = 0;
                foreach (var c in vol.Chapters)
                {
                    n++;
                    AddItem($"第{n}章 · {c.Title}（{c.Mood}）", c.Synopsis,
                        c.KeyBeats != null ? string.Join(" / ", c.KeyBeats) : "");
                }
            }

            if (sk.PlantedForeshadowings != null && sk.PlantedForeshadowings.Count > 0)
            {
                AddSection("伏笔", "");
                foreach (var f in sk.PlantedForeshadowings)
                    AddItem($"[{f.PlantedAtChapter}→{f.ExpectedPayoffChapter}]", f.Description, "");
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

        private void AddItem(string title, string sub, string right)
        {
            var border = new Border { Style = (Style)Application.Current.Resources["PhoneListItemBorderStyle"] };
            var sp = new StackPanel();
            sp.Children.Add(new TextBlock
            {
                Text = title,
                Style = (Style)Application.Current.Resources["PhoneListItemTitleStyle"],
            });
            if (!string.IsNullOrEmpty(sub))
                sp.Children.Add(new TextBlock
                {
                    Text = sub,
                    Style = (Style)Application.Current.Resources["PhoneListItemSubtitleStyle"],
                    TextWrapping = TextWrapping.Wrap,
                });
            if (!string.IsNullOrEmpty(right))
                sp.Children.Add(new TextBlock
                {
                    Text = right,
                    Style = (Style)Application.Current.Resources["PhoneListItemSubtitleStyle"],
                    TextWrapping = TextWrapping.Wrap,
                });
            border.Child = sp;
            ResultPanel.Children.Add(border);
        }

        private void OnBackClick(object sender, RoutedEventArgs e)
        {
            if (Frame.CanGoBack) Frame.GoBack(); else Frame.Navigate(typeof(Pages.Library.MainPage));
        }

        private void OnNextClick(object sender, RoutedEventArgs e)
        {
            // 传入 NovelId，让目标页在 PipelineHost.Current 为空时也能恢复
            Frame.Navigate(typeof(Pages.ReviewGate1Page), _pipeline.Novel.Id);
        }

        private void OnEditClick(object sender, RoutedEventArgs e)
        {
            Frame.Navigate(typeof(SkeletonEditPage), _pipeline.Novel.Id);
        }
    }
}
