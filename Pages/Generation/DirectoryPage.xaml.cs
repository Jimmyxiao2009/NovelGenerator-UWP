using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Windows.Storage.Pickers;
using Windows.UI;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Media;
using Windows.UI.Xaml.Navigation;
using novelgen.AI;
using novelgen.Common;
using novelgen.Core;
using novelgen.Models;
using novelgen.Pipeline;

namespace novelgen.Pages.Generation
{
    public sealed partial class DirectoryPage : Page
    {
        private NovelPipeline _pipeline;
        private bool _firstLoad = true;
        private CancellationTokenSource _aiCts;

        public DirectoryPage()
        {
            this.InitializeComponent();
        }

        protected override void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);
            _pipeline = PipelineHost.Current;
            if (_pipeline == null && e.Parameter is string nid)
            {
                var _ = LoadAndShow(nid);
                return;
            }
            if (_pipeline == null) { if (Frame.CanGoBack) Frame.GoBack(); return; }
            if (_firstLoad) Render();
            _firstLoad = false;
        }

        protected override void OnNavigatedFrom(NavigationEventArgs e)
        {
            base.OnNavigatedFrom(e);
            if (_aiCts != null) { _aiCts.Cancel(); _aiCts.Dispose(); _aiCts = null; }
        }

        private async Task LoadAndShow(string nid)
        {
            // fire-and-forget 调用，必须自己兜住异常，否则页面会白屏
            try
            {
                _pipeline = await PipelineHost.ResumeAsync(nid);
                if (_pipeline == null) { if (Frame.CanGoBack) Frame.GoBack(); return; }
                Render();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[Directory] 加载失败: {ex}");
                TitleText.Text = "加载失败";
                ProgressText.Text = ex.Message;
            }
        }

        private void Render()
        {
            var n = _pipeline.Novel;
            TitleText.Text = n.Title;
            var total = n.Chapters?.Count ?? 0;
            var done = n.Chapters?.Count(c => !string.IsNullOrEmpty(c.PolishedContent) || !string.IsNullOrEmpty(c.Content)) ?? 0;
            ProgressText.Text = $"{done}/{total} 章已完成";

            VolumePanel.Children.Clear();
            var vols = n.Skeleton?.Volumes;
            if (vols == null) return;

            foreach (var vol in vols)
            {
                var chs = n.Chapters.Where(c => c.VolumeId == vol.Id).OrderBy(c => c.Order).ToList();
                if (chs.Count == 0) continue;

                var card = new Border { Style = AppResource<Style>("PhoneListItemBorderStyle"), Margin = new Thickness(0, 0, 0, 4) };
                var stack = new StackPanel { Margin = new Thickness(8, 8, 8, 6) };
                stack.Children.Add(new TextBlock { Text = $"◇ {vol.Title}", FontSize = 16, FontWeight = Windows.UI.Text.FontWeights.SemiBold, Foreground = AppResource<SolidColorBrush>("PhoneForegroundBrush") });
                if (!string.IsNullOrEmpty(vol.Synopsis))
                    stack.Children.Add(new TextBlock { Text = vol.Synopsis, FontSize = 12, Foreground = AppResource<SolidColorBrush>("PhoneSubtleBrush"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(8, 2, 0, 4) });

                foreach (var ch in chs)
                {
                    var row = new Grid { Margin = new Thickness(12, 2, 0, 2) };
                    row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                    row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(48) });
                    row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(48) });

                    var hasContent = !string.IsNullOrEmpty(ch.PolishedContent) || !string.IsNullOrEmpty(ch.Content);
                    var color = hasContent
                        ? Color.FromArgb(255, 0x2E, 0x7D, 0x32)
                        : Color.FromArgb(255, 0x88, 0x88, 0x88);
                    var dot = hasContent ? "●" : "○";
                    var tb = new TextBlock
                    {
                        Text = $"{dot} 第{ch.Order}章 {ch.Outline?.Title ?? "未命名"}",
                        FontSize = 14,
                        Foreground = new SolidColorBrush(color),
                        VerticalAlignment = VerticalAlignment.Center,
                    };
                    Grid.SetColumn(tb, 0);
                    row.Children.Add(tb);

                    var editBtn = new Button
                    {
                        Content = "编辑",
                        Tag = ch.Order,
                        MinWidth = 44,
                        FontSize = 12,
                        Padding = new Thickness(4, 2, 4, 2),
                        VerticalAlignment = VerticalAlignment.Center,
                    };
                    editBtn.Click += OnEnterChapterClick;
                    Grid.SetColumn(editBtn, 1);
                    row.Children.Add(editBtn);

                    var aiBtn = new Button
                    {
                        Content = "AI",
                        Tag = ch,
                        MinWidth = 40,
                        FontSize = 12,
                        Padding = new Thickness(4, 2, 4, 2),
                        VerticalAlignment = VerticalAlignment.Center,
                        IsEnabled = hasContent,
                    };
                    aiBtn.Click += OnAiModifyChapterClick;
                    Grid.SetColumn(aiBtn, 2);
                    row.Children.Add(aiBtn);

                    stack.Children.Add(row);
                }

                card.Child = stack;
                VolumePanel.Children.Add(card);
            }
        }

        private void OnEnterChapterClick(object sender, RoutedEventArgs e)
        {
            var btn = sender as Button;
            if (btn?.Tag is int order)
            {
                Frame.Navigate(typeof(ChapterGenerationPage), order);
            }
        }

        private async void OnAiModifyChapterClick(object sender, RoutedEventArgs e)
        {
            if (_pipeline == null) return;
            var btn = sender as Button;
            var ch = btn?.Tag as Chapter;
            if (ch == null) return;

            var baseText = ch.PolishedContent ?? ch.Content ?? "";
            if (string.IsNullOrEmpty(baseText))
            {
                await new Windows.UI.Popups.MessageDialog("该章还没有正文。").ShowAsync();
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
                $"AI 引导修改 · 第{ch.Order}章「{ch.Outline?.Title ?? "未命名"}」",
                "例：让主角更冷静 / 在结尾加一段反派独白 / 把对话改得更口语化 / 缩短到 1500 字…",
                "应用");
            if (string.IsNullOrEmpty(instruction)) return;

            if (_aiCts != null) { _aiCts.Cancel(); _aiCts.Dispose(); }
            _aiCts = new CancellationTokenSource();
            var oldText = btn.Content;
            btn.Content = "…";
            btn.IsEnabled = false;

            try
            {
                var client = new ApiClient(profile);
                var outline = ch.Outline;
                var outlineSummary = outline == null ? ""
                    : $"标题：{outline.Title}\n梗概：{outline.Synopsis}\n氛围：{outline.Mood}";

                var newText = await AiGuidanceService.ModifyChapterContentAsync(
                    client, baseText, outlineSummary, instruction, null, _aiCts.Token);

                ch.PolishedContent = newText;
                ch.Content = newText;
                ch.Status = ChapterStatus.AwaitingUser;
                ch.PolishedAt = DateTime.Now;
                // ch 本身就是列表里的引用，无需按 Order-1 回写（索引与 Order 并不保证对应）
                await DataManager.SaveNovelAsync(_pipeline.Novel);
                Render();
                await new Windows.UI.Popups.MessageDialog($"第 {ch.Order} 章已按要求修改并保存。").ShowAsync();
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                await new Windows.UI.Popups.MessageDialog("AI 修改失败：" + ex.Message).ShowAsync();
            }
            finally
            {
                if (btn != null)
                {
                    btn.Content = oldText;
                    btn.IsEnabled = true;
                }
                if (_aiCts != null) { _aiCts.Dispose(); _aiCts = null; }
            }
        }

        private async void OnSaveClick(object sender, RoutedEventArgs e)
        {
            SaveBtn.IsEnabled = false;
            try
            {
                await DataManager.SaveNovelAsync(_pipeline.Novel);
                SaveBtn.Content = "已保存";
                await Task.Delay(1500);
                SaveBtn.Content = "保存";
            }
            catch (Exception ex)
            {
                SaveBtn.Content = "✗ 失败";
                System.Diagnostics.Debug.WriteLine($"[Directory] 保存失败: {ex}");
            }
            finally { SaveBtn.IsEnabled = true; }
        }

        private void OnRegenerateClick(object sender, RoutedEventArgs e)
        {
            Frame.Navigate(typeof(BatchGenerationPage), _pipeline.Novel.Id);
        }

        private void OnHomeClick(object sender, RoutedEventArgs e)
        {
            Frame.Navigate(typeof(Pages.Library.MainPage));
        }

        private async void OnExportTxtClick(object sender, RoutedEventArgs e)
        {
            if (_pipeline.Novel.Chapters == null || _pipeline.Novel.Chapters.Count == 0)
            {
                await new Windows.UI.Popups.MessageDialog("没有章节可导出。").ShowAsync();
                return;
            }

            try
            {
                var picker = new FileSavePicker
                {
                    SuggestedStartLocation = PickerLocationId.DocumentsLibrary,
                    SuggestedFileName = string.IsNullOrWhiteSpace(_pipeline.Novel.Title) ? "Novel" : _pipeline.Novel.Title
                };
                picker.FileTypeChoices.Add("文本文件", new List<string> { ".txt" });
                var file = await picker.PickSaveFileAsync();
                if (file == null) return;

                var sb = new StringBuilder();
                sb.AppendLine(_pipeline.Novel.Title);
                sb.AppendLine(new string('=', 40));
                sb.AppendLine();

                var vols = _pipeline.Novel.Skeleton?.Volumes;
                if (vols != null)
                {
                    foreach (var vol in vols)
                    {
                        sb.AppendLine($"◇ {vol.Title}");
                        sb.AppendLine(new string('-', 20));
                        var chs = _pipeline.Novel.Chapters.Where(c => c.VolumeId == vol.Id).OrderBy(c => c.Order);
                        foreach (var ch in chs)
                        {
                            sb.AppendLine($"\n第{ch.Order}章 {ch.Outline?.Title ?? ""}");
                            sb.AppendLine(new string('-', 10));
                            sb.AppendLine(ch.PolishedContent ?? ch.Content ?? "");
                        }
                        sb.AppendLine();
                    }
                }

                await Windows.Storage.FileIO.WriteTextAsync(file, sb.ToString());
                await new Windows.UI.Popups.MessageDialog($"TXT 已导出到:\n{file.Path}").ShowAsync();
            }
            catch (Exception ex)
            {
                await new Windows.UI.Popups.MessageDialog("导出失败：" + ex.Message).ShowAsync();
            }
        }

        private async void OnExportEpubClick(object sender, RoutedEventArgs e)
        {
            if (_pipeline.Novel.Chapters == null || _pipeline.Novel.Chapters.Count == 0)
            {
                await new Windows.UI.Popups.MessageDialog("没有章节可导出。").ShowAsync();
                return;
            }

            try
            {
                var picker = new FileSavePicker
                {
                    SuggestedStartLocation = PickerLocationId.DocumentsLibrary,
                    SuggestedFileName = string.IsNullOrWhiteSpace(_pipeline.Novel.Title) ? "Novel" : _pipeline.Novel.Title
                };
                picker.FileTypeChoices.Add("EPUB 电子书", new List<string> { ".epub" });
                var file = await picker.PickSaveFileAsync();
                if (file == null) return;

                await novelgen.Services.EpubExporter.WriteAsync(file, _pipeline.Novel);
                await new Windows.UI.Popups.MessageDialog($"EPUB 已导出到:\n{file.Path}").ShowAsync();
            }
            catch (Exception ex)
            {
                await new Windows.UI.Popups.MessageDialog("EPUB 导出失败：" + ex.Message).ShowAsync();
            }
        }

        private void OnBackClick(object sender, RoutedEventArgs e)
        {
            if (Frame.CanGoBack) Frame.GoBack();
            else Frame.Navigate(typeof(Pages.Library.MainPage));
        }

        private static T AppResource<T>(string key) where T : class
            => Application.Current.Resources[key] as T;
    }
}
