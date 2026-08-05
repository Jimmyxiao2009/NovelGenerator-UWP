using System;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Navigation;
using novelgen.AI;
using novelgen.Common;
using novelgen.Core;
using novelgen.Models;
using novelgen.Pipeline;

namespace novelgen.Pages.Generation
{
    public sealed partial class ChapterGenerationPage : Page
    {
        private NovelPipeline _pipeline;
        private int _chapterIndex;
        private CancellationTokenSource _cts;     // 单章生成用
        private CancellationTokenSource _aiCts;   // AI 引导修改用
        private bool _isSubmitting;               // 防止「接受」重入

        public ChapterGenerationPage()
        {
            this.InitializeComponent();
        }

        protected override void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);
            _pipeline = PipelineHost.Current;
            if (_pipeline == null) { if (Frame.CanGoBack) Frame.GoBack(); return; }
            if (e.Parameter is int order)
            {
                var idx = _pipeline.Novel.Chapters?.FindIndex(c => c.Order == order) ?? -1;
                if (idx >= 0) _chapterIndex = idx;
                else _chapterIndex = 0;
            }
            else
            {
                _chapterIndex = 0;
            }
            RenderCurrent();
        }

        protected override void OnNavigatedFrom(NavigationEventArgs e)
        {
            base.OnNavigatedFrom(e);
            _cts?.Cancel();    // 只取消，由生成方法的 finally 负责释放
            _aiCts?.Cancel();  // 取消进行中的 AI 修改
        }

        private void RenderCurrent()
        {
            var n = _pipeline.Novel;
            if (n.Chapters == null || n.Chapters.Count == 0)
            {
                ContentText.Text = "（没有章节 — 请检查骨架是否已生成）";
                ChapterTitle.Text = "无章节";
                ChapterMeta.Text = "0 / 0";
                return;
            }
            if (_chapterIndex < 0) _chapterIndex = 0;
            if (_chapterIndex >= n.Chapters.Count) _chapterIndex = n.Chapters.Count - 1;

            var ch = n.Chapters[_chapterIndex];
            ChapterTitle.Text = $"第{ch.Order}章 · {ch.Outline?.Title ?? "未命名"}";
            ChapterMeta.Text = $"{_chapterIndex + 1} / {n.Chapters.Count} · 状态：{ch.Status}";

            // 显示当前已润色的内容
            ContentText.Text = !string.IsNullOrEmpty(ch.PolishedContent) ? ch.PolishedContent
                              : !string.IsNullOrEmpty(ch.Content) ? ch.Content
                              : "（还没生成）";
            UpdateWordCount();

            // 大纲
            var ol = ch.Outline;
            if (ol != null)
            {
                var sb = new StringBuilder();
                sb.AppendLine("【梗概】" + ol.Synopsis);
                sb.AppendLine("【氛围】" + ol.Mood);
                if (ol.KeyBeats != null)
                {
                    sb.AppendLine("\n【节拍】");
                    foreach (var b in ol.KeyBeats) sb.AppendLine("  · " + b);
                }
                OutlineText.Text = sb.ToString();
            }
            else
            {
                OutlineText.Text = "（无）";
            }

            // 审查
            ReviewText.Text = ch.AiReview != null ? FormatReview(ch.AiReview) : "（尚未审查）";

            // 按钮
            PrevBtn.IsEnabled = _chapterIndex > 0;
            NextBtn.IsEnabled = _chapterIndex < n.Chapters.Count - 1;
            GenerateBtn.IsEnabled = true;
            AiModifyBtn.IsEnabled = !string.IsNullOrEmpty(ch.PolishedContent) || !string.IsNullOrEmpty(ch.Content);
            AcceptBtn.IsEnabled = !string.IsNullOrEmpty(ch.PolishedContent);
        }

        private void UpdateWordCount()
        {
            var ch = _pipeline.Novel.Chapters[_chapterIndex];
            var text = ch.PolishedContent ?? ch.Content ?? "";
            int n = 0;
            foreach (char c in text) if (c >= 0x4E00 && c <= 0x9FFF) n++;
            WordCountText.Text = n + " 字";
        }

        private string FormatReview(AiReviewReport r)
        {
            var sb = new StringBuilder();
            sb.AppendLine("【质量评分】" + r.QualityScore + " / 10");
            if (!string.IsNullOrEmpty(r.Summary)) sb.AppendLine("\n【总结】" + r.Summary);
            if (r.Notes != null && r.Notes.Count > 0)
            {
                sb.AppendLine("\n【修改建议】");
                foreach (var n in r.Notes)
                {
                    sb.AppendLine($"  · [{n.Type}/{n.Severity}] {n.Suggestion}");
                    if (!string.IsNullOrEmpty(n.Excerpt)) sb.AppendLine("    引用：" + n.Excerpt);
                }
            }
            if (r.Issues != null && r.Issues.Count > 0)
            {
                sb.AppendLine("\n【一致性问题】");
                foreach (var i in r.Issues)
                    sb.AppendLine($"  · [{i.Type}/{i.Severity}] {i.Description}\n    建议：{i.Suggestion}");
            }
            return sb.ToString();
        }

        private async void OnGenerateClick(object sender, RoutedEventArgs e)
        {
            if (_pipeline == null) return;
            if (_cts != null) { _cts.Cancel(); return; } // 生成中再点一次 = 取消
            _cts = new CancellationTokenSource();
            GenerateBtn.Label = "取消"; // 保持可点击，用于取消
            StreamProgress.Visibility = Visibility.Visible;
            ContentText.Text = "";
            ChapterMeta.Text = "生成中…";

            try
            {
                var ctx = new StageContext
                {
                    Profile = DataManager.FindApiProfile(_pipeline.Novel.ApiProfileId) ?? DataManager.ApiProfiles.FirstOrDefault(),
                    PolishProfile = DataManager.FindApiProfile(AppSettings.PolishApiProfileId) ?? DataManager.ApiProfiles.FirstOrDefault(),
                    Progress = new Progress<StageProgress>(p =>
                    {
                        if (p.Phase == "chunk" && !string.IsNullOrEmpty(p.Text))
                        {
                            ContentText.Text += p.Text;
                            ContentScroller.ChangeView(null, double.MaxValue, null, true);
                            UpdateWordCount();
                        }
                    }),
                    Ct = _cts.Token,
                };
                await _pipeline.GenerateAndPolishChapterAsync(_chapterIndex, ctx);
                RenderCurrent();
            }
            catch (OperationCanceledException)
            {
                ChapterMeta.Text = "已取消";
            }
            catch (Exception ex)
            {
                ChapterMeta.Text = "✗ 失败：" + ex.Message;
            }
            finally
            {
                StreamProgress.Visibility = Visibility.Collapsed;
                GenerateBtn.Label = "生成";
                GenerateBtn.IsEnabled = true;
                if (_cts != null) { _cts.Dispose(); _cts = null; }
            }
        }

        private void OnDirectoryClick(object sender, RoutedEventArgs e)
        {
            if (_pipeline == null) return;
            Frame.Navigate(typeof(DirectoryPage), _pipeline.Novel.Id);
        }

        private async void OnAcceptClick(object sender, RoutedEventArgs e)
        {
            if (_pipeline == null || _isSubmitting) return; // 防止双击重复提交
            var ch = _pipeline.Novel.Chapters[_chapterIndex];
            if (string.IsNullOrEmpty(ch.PolishedContent))
            {
                await new Windows.UI.Popups.MessageDialog("请先生成章节内容。").ShowAsync();
                return;
            }
            _isSubmitting = true;
            AcceptBtn.IsEnabled = false;
            try
            {
                await _pipeline.SubmitReview2Async(_chapterIndex, new UserReviewDecision { Action = ReviewAction.Accept });
                // 跳到下一章或者合并页
                if (_chapterIndex < _pipeline.Novel.Chapters.Count - 1)
                {
                    _chapterIndex++;
                    RenderCurrent();
                }
                else
                {
                    Frame.Navigate(typeof(ComposePage), _pipeline.Novel.Id);
                }
            }
            catch (Exception ex)
            {
                ChapterMeta.Text = "✗ 提交失败：" + ex.Message;
            }
            finally
            {
                _isSubmitting = false;
                AcceptBtn.IsEnabled = !string.IsNullOrEmpty(_pipeline.Novel.Chapters[_chapterIndex].PolishedContent);
            }
        }

        private void OnPrevClick(object sender, RoutedEventArgs e)
        {
            if (_chapterIndex > 0) { _chapterIndex--; RenderCurrent(); }
        }

        private void OnNextClick(object sender, RoutedEventArgs e)
        {
            if (_chapterIndex < _pipeline.Novel.Chapters.Count - 1) { _chapterIndex++; RenderCurrent(); }
        }

        private void OnBackClick(object sender, RoutedEventArgs e)
        {
            if (Frame.CanGoBack) Frame.GoBack(); else Frame.Navigate(typeof(Pages.Library.MainPage));
        }

        private async void OnAiModifyClick(object sender, RoutedEventArgs e)
        {
            if (_pipeline == null) return;
            var idx = _chapterIndex; // 固定本次操作的章节，防止翻页后写错章
            var ch = _pipeline.Novel.Chapters[idx];
            var baseText = ch.PolishedContent ?? ch.Content ?? "";
            if (string.IsNullOrEmpty(baseText))
            {
                await new Windows.UI.Popups.MessageDialog("该章还没有正文，请先生成。").ShowAsync();
                return;
            }

            var profile = DataManager.FindApiProfile(AppSettings.PolishApiProfileId)
                          ?? DataManager.FindApiProfile(_pipeline.Novel.ApiProfileId)
                          ?? DataManager.ApiProfiles.FirstOrDefault();
            if (profile == null)
            {
                await new Windows.UI.Popups.MessageDialog("请先在 API 配置中添加一个 API Profile。").ShowAsync();
                return;
            }

            var instruction = await GuidanceDialog.ShowAsync(
                "AI 引导修改（章节）",
                "例：让主角更冷静 / 在结尾加一段反派独白 / 把对话改得更口语化 / 缩短到 1500 字…",
                "应用");
            if (string.IsNullOrEmpty(instruction)) return;

            AiModifyBtn.IsEnabled = false;
            GenerateBtn.IsEnabled = false;
            AcceptBtn.IsEnabled = false;
            PrevBtn.IsEnabled = false;   // 修改期间禁止翻页，防止写入错误章节
            NextBtn.IsEnabled = false;
            StreamProgress.Visibility = Visibility.Visible;
            ContentText.Text = "";
            ChapterMeta.Text = "AI 修改中…";
            _aiCts = new CancellationTokenSource();

            try
            {
                var client = new ApiClient(profile);
                var outline = ch.Outline;
                var outlineSummary = outline == null ? ""
                    : $"标题：{outline.Title}\n梗概：{outline.Synopsis}\n氛围：{outline.Mood}";

                var progress = new Progress<string>(chunk =>
                {
                    ContentText.Text += chunk;
                    ContentScroller.ChangeView(null, double.MaxValue, null, true);
                });

                var newText = await AiGuidanceService.ModifyChapterContentAsync(
                    client, baseText, outlineSummary, instruction, progress, _aiCts.Token);

                // ch 本身就是列表里的引用，直接改属性即可
                ch.PolishedContent = newText;
                ch.Content = newText;
                ch.Status = ChapterStatus.AwaitingUser;
                ch.PolishedAt = DateTime.Now;
                await DataManager.SaveNovelAsync(_pipeline.Novel);
                RenderCurrent();
                ChapterMeta.Text = "✓ AI 修改完成";
            }
            catch (OperationCanceledException)
            {
                ChapterMeta.Text = "已取消";
                ContentText.Text = baseText;
            }
            catch (Exception ex)
            {
                ChapterMeta.Text = "✗ 失败：" + ex.Message;
                ContentText.Text = baseText;
            }
            finally
            {
                StreamProgress.Visibility = Visibility.Collapsed;
                AiModifyBtn.IsEnabled = true;
                GenerateBtn.IsEnabled = true;
                AcceptBtn.IsEnabled = !string.IsNullOrEmpty(ch.PolishedContent);
                PrevBtn.IsEnabled = _chapterIndex > 0;
                NextBtn.IsEnabled = _chapterIndex < _pipeline.Novel.Chapters.Count - 1;
                if (_aiCts != null) { _aiCts.Dispose(); _aiCts = null; }
            }
        }
    }
}
