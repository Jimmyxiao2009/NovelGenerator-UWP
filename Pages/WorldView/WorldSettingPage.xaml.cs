using System.Linq;
using System.Threading;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Navigation;
using novelgen.Core;
using novelgen.Models;
using novelgen.Pipeline;

namespace novelgen.Pages.WorldView
{
    public sealed partial class WorldSettingPage : Page
    {
        private NovelPipeline _pipeline;
        private CancellationTokenSource _cts;   // 离开页面时取消生成
        private bool _isGenerating;             // 防止重入导致并发生成

        public WorldSettingPage()
        {
            this.InitializeComponent();
        }

        protected override async void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);
            _pipeline = PipelineHost.Current;
            if (_pipeline == null)
            {
                if (e.Parameter is string nid)
                {
                    _pipeline = await PipelineHost.ResumeAsync(nid);
                }
            }
            if (_pipeline == null)
            {
                if (Frame.CanGoBack) Frame.GoBack(); else Frame.Navigate(typeof(Pages.Library.MainPage));
                return;
            }

            if (_pipeline.Novel.WorldSetting != null)
            {
                Render(_pipeline.Novel.WorldSetting);
                NextButton.IsEnabled = true;
                return;
            }

            if (_isGenerating) return; // 已在生成中，避免再起一个并发任务
            _isGenerating = true;
            _cts = new CancellationTokenSource();
            var ct = _cts.Token;
            Progress.Visibility = Visibility.Visible;
            StatusText.Text = "调用 AI 生成世界设定…";
            try
            {
                var ctx = BuildContext();
                ctx.Ct = ct;
                var ws = await _pipeline.GenerateWorldSettingAsync(ctx);
                if (ct.IsCancellationRequested) return; // 页面已离开，不再碰 UI
                Render(ws);
                NextButton.IsEnabled = true;
                StatusText.Text = "已生成。请检查后点「继续 →」";
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
            var profile = ResolveProfile(AppSettings.DraftApiProfileId);
            return new StageContext { Profile = profile, PolishProfile = ResolveProfile(AppSettings.PolishApiProfileId) };
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

        private void Render(WorldSetting ws)
        {
            ResultScroller.Visibility = Visibility.Visible;
            ResultPanel.Children.Clear();

            AddSection("梗概", ws.Synopsis);
            if (ws.Themes != null && ws.Themes.Length > 0)
                AddSection("主题", string.Join("、", ws.Themes));

            AddSection($"人物 ({ws.Characters.Count})", "");
            foreach (var c in ws.Characters)
                AddItem($"• {c.Name}（{c.Role}）", c.Personality, c.Goal);

            AddSection($"地点 ({ws.Locations.Count})", "");
            foreach (var l in ws.Locations)
                AddItem($"• {l.Name}（{l.Type}）", l.Description, "");

            AddSection($"势力 ({ws.Factions.Count})", "");
            foreach (var f in ws.Factions)
                AddItem($"• {f.Name}", f.Ideology, f.Influence);

            AddSection($"硬规则 ({ws.Rules.Count})", "");
            foreach (var r in ws.Rules)
                AddItem($"• [{r.Category}]", r.Description, r.IsHard ? "硬规则" : "");
        }

        private void AddSection(string title, string body)
        {
            var t = new TextBlock
            {
                Text = title,
                FontSize = 20,
                FontWeight = Windows.UI.Text.FontWeights.SemiLight,
                Foreground = (Windows.UI.Xaml.Media.Brush)Application.Current.Resources["PhoneForegroundBrush"],
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
                    Foreground = (Windows.UI.Xaml.Media.Brush)Application.Current.Resources["PhoneAccentBrush"],
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
            Frame.Navigate(typeof(Pages.Skeleton.SkeletonPage), _pipeline.Novel.Id);
        }

        private void OnEditClick(object sender, RoutedEventArgs e)
        {
            Frame.Navigate(typeof(WorldEditPage), _pipeline.Novel.Id);
        }
    }
}
