using System;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Windows.Foundation;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Navigation;
using Windows.UI.Popups;
using novelgen.AI;
using novelgen.Common;
using novelgen.Core;
using novelgen.Models;
using novelgen.Pipeline;

namespace novelgen.Pages
{
    public sealed partial class ReviewGate1Page : Page
    {
        private NovelPipeline _pipeline;
        private CancellationTokenSource _aiCts;
        private bool _isSubmitting; // 防止 Gate1 按钮重入（双击双导航/双启动）

        public ReviewGate1Page()
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

            WorldText.Text = FormatWorld(_pipeline.Novel.WorldSetting);
            SkeletonText.Text = FormatSkeleton(_pipeline.Novel.Skeleton);
        }

        protected override void OnNavigatedFrom(NavigationEventArgs e)
        {
            base.OnNavigatedFrom(e);
            if (_aiCts != null) { _aiCts.Cancel(); _aiCts.Dispose(); _aiCts = null; }
        }

        private string FormatWorld(Models.WorldSetting ws)
        {
            if (ws == null) return "（未生成）";
            var sb = new StringBuilder();
            sb.AppendLine("【梗概】" + ws.Synopsis);
            if (ws.Themes != null) sb.AppendLine("\n【主题】" + string.Join("、", ws.Themes));
            sb.AppendLine("\n【人物】");
            if (ws.Characters != null)
                foreach (var c in ws.Characters) sb.AppendLine($"  · {c.Name}（{c.Role}）\n    {c.Personality}\n    目标：{c.Goal}");
            sb.AppendLine("\n【地点】");
            if (ws.Locations != null)
                foreach (var l in ws.Locations) sb.AppendLine($"  · {l.Name}\n    {l.Description}");
            sb.AppendLine("\n【硬规则】");
            if (ws.Rules != null)
                foreach (var r in ws.Rules) sb.AppendLine($"  · [{r.Category}] {r.Description}" + (r.IsHard ? "  [硬]" : ""));
            return sb.ToString();
        }

        private string FormatSkeleton(Models.StorySkeleton sk)
        {
            if (sk == null) return "（未生成）";
            var sb = new StringBuilder();
            sb.AppendLine("【一句话】" + sk.Logline);
            sb.AppendLine("\n【三幕】" + sk.ThreeAct);
            sb.AppendLine($"\n【共 {sk.TotalChapters} 章 / {sk.Volumes?.Count ?? 0} 卷】");
            foreach (var vol in sk.Volumes ?? new System.Collections.Generic.List<Models.VolumeOutline>())
            {
                sb.AppendLine($"\n  ▶ 卷：{vol.Title}");
                sb.AppendLine($"    目标：{vol.PlotGoal}");
                sb.AppendLine($"    高潮：{vol.Climax}");
                int n = 0;
                foreach (var c in vol.Chapters ?? new System.Collections.Generic.List<Models.ChapterOutline>())
                {
                    n++;
                    sb.AppendLine($"    {n}. {c.Title}（{c.Mood}）");
                    sb.AppendLine($"       {c.Synopsis}");
                }
            }
            if (sk.PlantedForeshadowings != null && sk.PlantedForeshadowings.Count > 0)
            {
                sb.AppendLine("\n【伏笔】");
                foreach (var f in sk.PlantedForeshadowings)
                    sb.AppendLine($"  · [{f.PlantedAtChapter}→{f.ExpectedPayoffChapter}] {f.Description}");
            }
            return sb.ToString();
        }

        private void OnBackClick(object sender, RoutedEventArgs e)
        {
            if (Frame.CanGoBack) Frame.GoBack(); else Frame.Navigate(typeof(Pages.Library.MainPage));
        }

        private async void OnReviseWorldClick(object sender, RoutedEventArgs e)
        {
            if (_isSubmitting || _pipeline == null) return;
            _isSubmitting = true;
            var btn = sender as Control;
            if (btn != null) btn.IsEnabled = false;
            try
            {
                // 必须先提交评审（内部先留自动快照），再清空字段——否则快照拍到的是已销毁的状态，回滚失效
                await _pipeline.SubmitReview1Async(new Models.UserReviewDecision { Action = Models.ReviewAction.RequestRevision });
                _pipeline.Novel.WorldSetting = null;
                _pipeline.Novel.Skeleton = null;
                // 调用时 Skeleton 尚在，SubmitReview1Async 落在 SkeletonDraft，这里修正为世界草稿
                _pipeline.TransitionTo(PipelineState.WorldSettingDraft, "Gate1 重做世界");
                await Core.DataManager.SaveNovelAsync(_pipeline.Novel);
                Frame.Navigate(typeof(Pages.WorldView.WorldSettingPage), _pipeline.Novel.Id);
            }
            catch (OperationCanceledException) { /* 用户取消，静默 */ }
            catch (Exception ex)
            {
                WorldStatus.Text = "✗ 失败：" + ex.Message;
            }
            finally
            {
                _isSubmitting = false;
                if (btn != null) btn.IsEnabled = true;
            }
        }

        private async void OnReviseSkeletonClick(object sender, RoutedEventArgs e)
        {
            if (_isSubmitting || _pipeline == null) return;
            _isSubmitting = true;
            var btn = sender as Control;
            if (btn != null) btn.IsEnabled = false;
            try
            {
                // 先提交评审（内部先留自动快照），再清空骨架，保证快照里还有完整数据
                await _pipeline.SubmitReview1Async(new Models.UserReviewDecision { Action = Models.ReviewAction.RequestRevision });
                _pipeline.Novel.Skeleton = null;
                await Core.DataManager.SaveNovelAsync(_pipeline.Novel);
                Frame.Navigate(typeof(Pages.Skeleton.SkeletonPage), _pipeline.Novel.Id);
            }
            catch (OperationCanceledException) { /* 用户取消，静默 */ }
            catch (Exception ex)
            {
                SkeletonStatus.Text = "✗ 失败：" + ex.Message;
            }
            finally
            {
                _isSubmitting = false;
                if (btn != null) btn.IsEnabled = true;
            }
        }

        private async void OnAcceptClick(object sender, RoutedEventArgs e)
        {
            if (_isSubmitting || _pipeline == null) return;
            _isSubmitting = true;
            var btn = sender as Control;
            if (btn != null) btn.IsEnabled = false;
            try
            {
                await _pipeline.SubmitReview1Async(new Models.UserReviewDecision { Action = Models.ReviewAction.Accept });
                Frame.Navigate(typeof(Pages.Generation.BatchGenerationPage), _pipeline.Novel.Id);
            }
            catch (OperationCanceledException) { /* 用户取消，静默 */ }
            catch (Exception ex)
            {
                var dlg = new ContentDialog
                {
                    Title = "提交失败",
                    Content = ex.Message,
                    PrimaryButtonText = "确定",
                };
                await dlg.ShowAsync();
            }
            finally
            {
                _isSubmitting = false;
                if (btn != null) btn.IsEnabled = true;
            }
        }

        private async void OnAiModifyWorldClick(object sender, RoutedEventArgs e)
        {
            if (_pipeline == null) return;
            var profile = DataManager.FindApiProfile(_pipeline.Novel.ApiProfileId) ?? DataManager.ApiProfiles.FirstOrDefault();
            if (profile == null)
            {
                await new MessageDialog("请先在 API 配置中添加一个 API Profile。").ShowAsync();
                return;
            }
            if (_pipeline.Novel.WorldSetting == null)
            {
                await new MessageDialog("还没有世界设定，无法修改。").ShowAsync();
                return;
            }

            var instruction = await GuidanceDialog.ShowAsync(
                "AI 引导修改（世界设定）",
                "例：把主角改成女性 / 时代背景换到唐代 / 加一名亦敌亦友的女配 / 把硬规则改为禁止使用火焰…",
                "应用");
            if (string.IsNullOrEmpty(instruction)) return;

            if (_aiCts != null) { _aiCts.Cancel(); _aiCts.Dispose(); }
            _aiCts = new CancellationTokenSource();
            AiModifyWorldBtn.IsEnabled = false;
            AiModifySkeletonBtn.IsEnabled = false;
            WorldStatus.Text = "AI 修改中…";

            try
            {
                var client = new ApiClient(profile);
                var newWs = await AiGuidanceService.ModifyWorldSettingAsync(
                    client, _pipeline.Novel.WorldSetting, instruction, _aiCts.Token);

                _pipeline.Novel.WorldSetting = newWs;
                _pipeline.Novel.Skeleton = null; // 世界改了，骨架必须重做
                _pipeline.Novel.Chapters = null;
                await DataManager.SaveNovelAsync(_pipeline.Novel);

                WorldText.Text = FormatWorld(newWs);
                SkeletonText.Text = "（世界已修改，骨架已清空，请点击「重做骨架」重新生成）";
                WorldStatus.Text = "✓ 已保存，骨架已清空";
            }
            catch (OperationCanceledException) { WorldStatus.Text = "已取消"; }
            catch (Exception ex)
            {
                WorldStatus.Text = "✗ 失败：" + ex.Message;
            }
            finally
            {
                AiModifyWorldBtn.IsEnabled = true;
                AiModifySkeletonBtn.IsEnabled = true;
                if (_aiCts != null) { _aiCts.Dispose(); _aiCts = null; }
            }
        }

        private async void OnAiModifySkeletonClick(object sender, RoutedEventArgs e)
        {
            if (_pipeline == null) return;
            var profile = DataManager.FindApiProfile(_pipeline.Novel.ApiProfileId) ?? DataManager.ApiProfiles.FirstOrDefault();
            if (profile == null)
            {
                await new MessageDialog("请先在 API 配置中添加一个 API Profile。").ShowAsync();
                return;
            }
            if (_pipeline.Novel.Skeleton == null)
            {
                await new MessageDialog("还没有骨架，无法修改。").ShowAsync();
                return;
            }

            var instruction = await GuidanceDialog.ShowAsync(
                "AI 引导修改（故事骨架）",
                "例：把第一章和第二章合并 / 在第三卷加一段战争高潮 / 把伏笔 X 提前到第 2 章 / 给反派加一条人物线…",
                "应用");
            if (string.IsNullOrEmpty(instruction)) return;

            if (_aiCts != null) { _aiCts.Cancel(); _aiCts.Dispose(); }
            _aiCts = new CancellationTokenSource();
            AiModifyWorldBtn.IsEnabled = false;
            AiModifySkeletonBtn.IsEnabled = false;
            SkeletonStatus.Text = "AI 修改中…";

            try
            {
                var client = new ApiClient(profile);
                var newSk = await AiGuidanceService.ModifySkeletonAsync(
                    client, _pipeline.Novel.Skeleton, instruction, _aiCts.Token);

                _pipeline.Novel.Skeleton = newSk;
                // 重建 Chapters 占位
                _pipeline.Novel.Chapters = new System.Collections.Generic.List<Chapter>();
                int order = 0;
                foreach (var vol in newSk.Volumes)
                {
                    foreach (var co in vol.Chapters)
                    {
                        _pipeline.Novel.Chapters.Add(new Chapter
                        {
                            Order = ++order,
                            VolumeId = vol.Id,
                            Outline = co,
                            Status = ChapterStatus.Pending,
                        });
                    }
                }
                await DataManager.SaveNovelAsync(_pipeline.Novel);

                SkeletonText.Text = FormatSkeleton(newSk);
                SkeletonStatus.Text = $"✓ 已保存，共 {_pipeline.Novel.Chapters.Count} 章";
            }
            catch (OperationCanceledException) { SkeletonStatus.Text = "已取消"; }
            catch (Exception ex)
            {
                SkeletonStatus.Text = "✗ 失败：" + ex.Message;
            }
            finally
            {
                AiModifyWorldBtn.IsEnabled = true;
                AiModifySkeletonBtn.IsEnabled = true;
                if (_aiCts != null) { _aiCts.Dispose(); _aiCts = null; }
            }
        }
    }
}
