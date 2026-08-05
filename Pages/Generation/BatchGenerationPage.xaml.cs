using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Windows.UI;
using Windows.UI.Core;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Media;
using Windows.UI.Xaml.Navigation;
using novelgen.Core;
using novelgen.Models;
using novelgen.Pipeline;

namespace novelgen.Pages.Generation
{
    public sealed partial class BatchGenerationPage : Page
    {
        private NovelPipeline _pipeline;
        private CancellationTokenSource _cts;
        private DispatcherTimer _timer;
        private DateTime _startTime;

        private Dictionary<int, TextBlock> _agentSlots = new Dictionary<int, TextBlock>();
        private List<VolumeCard> _volumeCards = new List<VolumeCard>();
        private Dictionary<int, ChapterRow> _chapterRows = new Dictionary<int, ChapterRow>();

        private volatile bool _isRunning;
        private bool _suppressConcurrencyChange;
        private bool _navigated; // 防止完成/取消路径重复导航

        public BatchGenerationPage()
        {
            this.InitializeComponent();
            _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            _timer.Tick += OnTimerTick;
        }

        protected override void OnNavigatedFrom(NavigationEventArgs e)
        {
            base.OnNavigatedFrom(e);
            _timer.Stop();
            _isRunning = false;
            _cts?.Cancel(); // 离开页面时停止后台生成，避免孤儿任务（只取消，不释放）
        }

        private void OnTimerTick(object sender, object e)
        {
            var elapsed = DateTime.Now - _startTime;
            TimerText.Text = elapsed.TotalHours >= 1
                ? $"{(int)elapsed.TotalHours:D2}:{elapsed.Minutes:D2}:{elapsed.Seconds:D2}"
                : $"{elapsed.Minutes:D2}:{elapsed.Seconds:D2}";
        }

        private void OnBackClick(object sender, RoutedEventArgs e)
        {
            if (_isRunning)
            {
                _cts?.Cancel();
                _isRunning = false;
            }
            if (_navigated) return;
            if (Frame.CanGoBack)
            {
                _navigated = true;
                Frame.GoBack();
            }
        }

        private void BuildVolumeCards()
        {
            var vols = _pipeline.Novel.Skeleton?.Volumes;
            if (vols == null) return;
            VolumeListPanel.Children.Clear();
            _volumeCards.Clear();
            _chapterRows.Clear();

            int globalOrder = 1;
            for (int vi = 0; vi < vols.Count; vi++)
            {
                var vol = vols[vi];
                var chs = _pipeline.Novel.Chapters
                    .Where(c => c.VolumeId == vol.Id)
                    .OrderBy(c => c.Order).ToList();
                if (chs.Count == 0) continue;
                int chCount = chs.Count;

                var card = new VolumeCard
                {
                    VolumeIndex = vi,
                    Title = $"◇ 卷{vi + 1}「{vol.Title}」",
                    ChapterTotal = chCount,
                    FirstChapterOrder = globalOrder,
                };
                globalOrder += chCount;
                _volumeCards.Add(card);

                var border = new Border { Margin = new Thickness(0, 1, 0, 1) };
                var stack = new StackPanel { Margin = new Thickness(0, 4, 0, 8) };

                var titleRow = new Grid { Margin = new Thickness(0, 0, 0, 4) };
                titleRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                titleRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                card.TitleBlock = new TextBlock
                {
                    Text = card.Title,
                    FontSize = 15,
                    FontWeight = Windows.UI.Text.FontWeights.SemiBold,
                    Foreground = Application.Current.Resources["PhoneForegroundBrush"] as SolidColorBrush,
                    VerticalAlignment = VerticalAlignment.Center,
                };
                Grid.SetColumn(card.TitleBlock, 0);
                titleRow.Children.Add(card.TitleBlock);
                card.StatusBlock = new TextBlock
                {
                    Text = "等待中",
                    FontSize = 11,
                    Foreground = Application.Current.Resources["PhoneSubtleBrush"] as SolidColorBrush,
                    VerticalAlignment = VerticalAlignment.Center,
                };
                Grid.SetColumn(card.StatusBlock, 1);
                titleRow.Children.Add(card.StatusBlock);
                stack.Children.Add(titleRow);

                foreach (var ch in chs)
                {
                    var row = new Grid { Margin = new Thickness(8, 3, 0, 3) };
                    row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(18) });
                    row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                    row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

                    var icon = new TextBlock
                    {
                        Text = "○",
                        FontSize = 12,
                        Foreground = Application.Current.Resources["PhoneSubtleBrush"] as SolidColorBrush,
                        VerticalAlignment = VerticalAlignment.Center,
                        HorizontalAlignment = HorizontalAlignment.Center,
                    };
                    Grid.SetColumn(icon, 0);
                    row.Children.Add(icon);

                    var title = new TextBlock
                    {
                        Text = $"第{ch.Order}章 · {ch.Outline?.Title ?? "未命名"}",
                        FontSize = 13,
                        Foreground = Application.Current.Resources["PhoneSubtleBrush"] as SolidColorBrush,
                        VerticalAlignment = VerticalAlignment.Center,
                        TextTrimming = TextTrimming.CharacterEllipsis,
                    };
                    Grid.SetColumn(title, 1);
                    row.Children.Add(title);

                    var status = new TextBlock
                    {
                        Text = "",
                        FontSize = 11,
                        Foreground = Application.Current.Resources["PhoneSubtleBrush"] as SolidColorBrush,
                        VerticalAlignment = VerticalAlignment.Center,
                        Margin = new Thickness(8, 0, 0, 0),
                    };
                    Grid.SetColumn(status, 2);
                    row.Children.Add(status);

                    var initStatus = GetChapterInitStatus(ch);
                    if (!string.IsNullOrEmpty(initStatus))
                    {
                        icon.Text = "●";
                        icon.Foreground = new SolidColorBrush(Color.FromArgb(255, 0x2E, 0x7D, 0x32));
                        title.Foreground = Application.Current.Resources["PhoneForegroundBrush"] as SolidColorBrush;
                        status.Text = "✓";
                        status.Foreground = new SolidColorBrush(Color.FromArgb(255, 0x2E, 0x7D, 0x32));
                    }

                    stack.Children.Add(row);
                    _chapterRows[ch.Order] = new ChapterRow
                    {
                        Icon = icon,
                        Title = title,
                        Status = status,
                    };
                }

                border.Child = stack;
                VolumeListPanel.Children.Add(border);
            }
        }

        private static string GetChapterInitStatus(Chapter ch)
        {
            if (!string.IsNullOrEmpty(ch.PolishedContent)) return "polished";
            if (!string.IsNullOrEmpty(ch.Content)) return "drafted";
            return null;
        }

        private void BuildAgentSlots(int concurrency)
        {
            AgentPanel.Children.Clear();
            _agentSlots.Clear();
            for (int i = 0; i < concurrency; i++)
            {
                var pill = new Border
                {
                    Background = Application.Current.Resources["PhoneBorderBrush"] as SolidColorBrush,
                    CornerRadius = new CornerRadius(3),
                    Padding = new Thickness(8, 3, 8, 3),
                    Margin = new Thickness(0, 0, 4, 0),
                };
                var tb = new TextBlock
                {
                    Text = $"A{i + 1}",
                    FontSize = 11,
                    Foreground = Application.Current.Resources["PhoneSubtleBrush"] as SolidColorBrush,
                };
                pill.Child = tb;
                _agentSlots[i + 1] = tb;
                AgentPanel.Children.Add(pill);
            }
            AgentBorder.Visibility = Visibility.Visible;
        }

        private async void StartBatch()
        {
            _isRunning = true;
            _startTime = DateTime.Now;
            _timer.Start();
            _cts = new CancellationTokenSource();
            int concurrency = AppSettings.AgentConcurrency;
            BuildAgentSlots(concurrency);
            OverallProgress.Value = 0;
            PercentText.Text = "0%";
            CancelBtn.IsEnabled = true;

            try
            {
                var ctx = new StageContext
                {
                    Profile = DataManager.FindApiProfile(_pipeline.Novel.ApiProfileId) ?? DataManager.ApiProfiles.FirstOrDefault(),
                    PolishProfile = DataManager.FindApiProfile(AppSettings.PolishApiProfileId) ?? DataManager.ApiProfiles.FirstOrDefault(),
                    Progress = new Progress<StageProgress>(OnProgress),
                    Ct = _cts.Token,
                    OnChapterDone = async ch =>
                    {
                        try { await DataManager.SaveNovelAsync(_pipeline.Novel); }
                        catch { }
                    },
                };
                await _pipeline.GenerateAllVolumesBatchAsync(ctx, concurrency);
                StatusText.Text = "全部卷生成完毕";
                PercentText.Text = "100%";
                _isRunning = false;
                _timer.Stop();
                CancelBtn.IsEnabled = false; // 等待期间禁止点击，避免重复导航
                CancelBtn.Content = "返回目录 →";
                CancelBtn.Background = Application.Current.Resources["PhoneAccentBrush"] as SolidColorBrush;
                await Task.Delay(600);
                if (!_navigated)
                {
                    _navigated = true;
                    Frame.Navigate(typeof(DirectoryPage), _pipeline.Novel.Id);
                }
            }
            catch (OperationCanceledException)
            {
                StatusText.Text = "已取消";
                // 已取消：_isRunning 已为 false，按钮行为变为返回，文案与之对齐
                CancelBtn.Content = "返回";
                CancelBtn.Background = Application.Current.Resources["PhoneAccentBrush"] as SolidColorBrush;
            }
            catch (Exception ex)
            {
                StatusText.Text = "✗ " + ex.Message;
                // 失败：_isRunning 已为 false，OnCancelClick 会执行返回，故文案改为「返回」以匹配行为
                CancelBtn.Content = "返回";
                CancelBtn.Background = Application.Current.Resources["PhoneAccentBrush"] as SolidColorBrush;
            }
            finally
            {
                _isRunning = false;
                _timer.Stop();
                if (_cts != null) { _cts.Dispose(); _cts = null; }
            }
        }

        private void OnProgress(StageProgress p)
        {
            if (!_isRunning) return;
            try
            {
                var _ = Dispatcher.RunAsync(CoreDispatcherPriority.Normal, () =>
                {
                    if (!_isRunning) return;
                    try
                    {
                        if (p.Percent >= 0)
                        {
                            OverallProgress.Value = p.Percent;
                            PercentText.Text = $"{(int)p.Percent}%";
                        }
                        StatusText.Text = p.Text;

                        if (p.AgentId > 0 && _agentSlots.TryGetValue(p.AgentId, out var slot))
                        {
                            string phase;
                            Color color;
                            switch (p.Phase)
                            {
                                case "drafting": phase = "▷"; color = Color.FromArgb(255, 0x00, 0xAA, 0xDD); break;
                                case "polishing": phase = "✦"; color = Color.FromArgb(255, 0xDD, 0xA0, 0x00); break;
                                case "drafted":
                                case "polished": phase = "✓"; color = Color.FromArgb(255, 0x2E, 0x7D, 0x32); break;
                                default: phase = ""; color = Color.FromArgb(255, 0x66, 0x66, 0x66); break;
                            }
                            slot.Text = $"A{p.AgentId} {phase} {p.ChapterTitle ?? ""}";
                            slot.Foreground = new SolidColorBrush(color);
                        }

                        if (p.VolumeIndex >= 0 && p.VolumeIndex < _volumeCards.Count)
                        {
                            var card = _volumeCards[p.VolumeIndex];
                            switch (p.Phase)
                            {
                                case "start":
                                    card.TitleBlock.Foreground = new SolidColorBrush(Color.FromArgb(255, 0x4F, 0xC3, 0xF7));
                                    card.StatusBlock.Text = "生成中…";
                                    card.StatusBlock.Foreground = new SolidColorBrush(Color.FromArgb(255, 0x4F, 0xC3, 0xF7));
                                    break;
                                case "done":
                                    card.TitleBlock.Foreground = Application.Current.Resources["PhoneForegroundBrush"] as SolidColorBrush;
                                    card.StatusBlock.Text = "✓ 完成";
                                    card.StatusBlock.Foreground = new SolidColorBrush(Color.FromArgb(255, 0x2E, 0x7D, 0x32));
                                    break;
                                case "connecting":
                                    card.StatusBlock.Text = "🔗 衔接…";
                                    card.StatusBlock.Foreground = new SolidColorBrush(Color.FromArgb(255, 0xFF, 0xA0, 0x00));
                                    break;
                            }

                            if (p.ChapterOrder > 0 && _chapterRows.TryGetValue(p.ChapterOrder, out var row))
                            {
                                switch (p.Phase)
                                {
                                    case "drafting":
                                        row.Icon.Text = "◉";
                                        row.Icon.Foreground = new SolidColorBrush(Color.FromArgb(255, 0x00, 0xAA, 0xDD));
                                        row.Title.Foreground = new SolidColorBrush(Color.FromArgb(255, 0x00, 0xAA, 0xDD));
                                        row.Status.Text = "生成中…";
                                        row.Status.Foreground = new SolidColorBrush(Color.FromArgb(255, 0x00, 0xAA, 0xDD));
                                        break;
                                    case "drafted":
                                        row.Icon.Text = "●";
                                        row.Icon.Foreground = new SolidColorBrush(Color.FromArgb(255, 0x2E, 0x7D, 0x32));
                                        row.Title.Foreground = Application.Current.Resources["PhoneForegroundBrush"] as SolidColorBrush;
                                        row.Status.Text = "已生成";
                                        row.Status.Foreground = new SolidColorBrush(Color.FromArgb(255, 0x2E, 0x7D, 0x32));
                                        break;
                                    case "polishing":
                                        row.Icon.Text = "✦";
                                        row.Icon.Foreground = new SolidColorBrush(Color.FromArgb(255, 0xDD, 0xA0, 0x00));
                                        row.Title.Foreground = new SolidColorBrush(Color.FromArgb(255, 0xDD, 0xA0, 0x00));
                                        row.Status.Text = "润色中…";
                                        row.Status.Foreground = new SolidColorBrush(Color.FromArgb(255, 0xDD, 0xA0, 0x00));
                                        break;
                                    case "polished":
                                        row.Icon.Text = "●";
                                        row.Icon.Foreground = new SolidColorBrush(Color.FromArgb(255, 0x2E, 0x7D, 0x32));
                                        row.Title.Foreground = Application.Current.Resources["PhoneForegroundBrush"] as SolidColorBrush;
                                        row.Status.Text = "✓";
                                        row.Status.Foreground = new SolidColorBrush(Color.FromArgb(255, 0x2E, 0x7D, 0x32));
                                        break;
                                }
                            }
                        }
                    }
                    catch { }
                });
            }
            catch { }
        }

        private void OnCancelClick(object sender, RoutedEventArgs e)
        {
            if (_isRunning)
            {
                _cts?.Cancel();
                _isRunning = false;
                _timer.Stop();
                StatusText.Text = "已取消，返回中…";
            }
            if (_navigated) return;
            if (Frame.CanGoBack)
            {
                _navigated = true;
                Frame.GoBack();
            }
        }

        protected override async void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);
            _navigated = false;
            try
            {
                _pipeline = PipelineHost.Current;
                if (_pipeline == null && e.Parameter is string nid)
                    _pipeline = await PipelineHost.ResumeAsync(nid);
                if (_pipeline == null) { if (Frame.CanGoBack) Frame.GoBack(); return; }
                HeaderText.Text = string.IsNullOrEmpty(_pipeline.Novel.Title)
                    ? "批量生成" : _pipeline.Novel.Title;
                BuildVolumeCards();
                HighlightConcurrency(AppSettings.AgentConcurrency);
                if (e.NavigationMode != NavigationMode.Back)
                    StartBatch();
                else
                {
                    StatusText.Text = _pipeline.Novel.Chapters?.Count > 0
                        ? $"共 {_pipeline.Novel.Skeleton?.Volumes?.Count ?? 0} 卷" : "准备中…";
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[BatchPage] OnNavigatedTo 失败: {ex}");
                if (Frame.CanGoBack) Frame.GoBack();
            }
        }

        private void OnConcurrencyChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_isRunning) return;
            if (_suppressConcurrencyChange) return;
            if (ConcurrencyBox.SelectedItem is ComboBoxItem ci && ci.Content is string s)
            {
                if (int.TryParse(s, out int n))
                {
                    n = ClampConcurrency(n);
                    AppSettings.AgentConcurrency = n;
                    ConcurrencyInput.Text = n.ToString();
                    BuildAgentSlots(n);
                }
            }
        }

        private void OnConcurrencyInputKeyDown(object sender, Windows.UI.Xaml.Input.KeyRoutedEventArgs e)
        {
            if (e.Key == Windows.System.VirtualKey.Enter) ApplyConcurrencyFromInput();
        }

        private void OnConcurrencyApplyClick(object sender, RoutedEventArgs e)
        {
            ApplyConcurrencyFromInput();
        }

        private void ApplyConcurrencyFromInput()
        {
            if (int.TryParse(ConcurrencyInput.Text, out int n))
            {
                n = ClampConcurrency(n);
                AppSettings.AgentConcurrency = n;
                ConcurrencyInput.Text = n.ToString();
                HighlightConcurrency(n);
                if (!_isRunning) BuildAgentSlots(n);
            }
        }

        private static int ClampConcurrency(int n)
        {
            if (n < 1) return 1;
            if (n > 32) return 32;
            return n;
        }

        private void HighlightConcurrency(int n)
        {
            _suppressConcurrencyChange = true;
            try
            {
                string s = n.ToString();
                foreach (var item in ConcurrencyBox.Items)
                {
                    if (item is ComboBoxItem ci && (ci.Content as string) == s)
                    {
                        ConcurrencyBox.SelectedItem = ci;
                        ConcurrencyInput.Text = n.ToString();
                        return;
                    }
                }
                ConcurrencyBox.SelectedIndex = -1;
                ConcurrencyInput.Text = n.ToString();
            }
            finally
            {
                _suppressConcurrencyChange = false;
            }
        }

        private class VolumeCard
        {
            public int VolumeIndex;
            public string Title;
            public int ChapterTotal;
            public int FirstChapterOrder;
            public TextBlock TitleBlock;
            public TextBlock StatusBlock;
        }

        private class ChapterRow
        {
            public TextBlock Icon;
            public TextBlock Title;
            public TextBlock Status;
        }
    }
}
