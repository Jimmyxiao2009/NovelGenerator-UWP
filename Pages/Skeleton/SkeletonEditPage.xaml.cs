using System;
using System.Collections.Generic;
using System.Linq;
using Windows.UI;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Input;
using Windows.UI.Xaml.Media;
using Windows.UI.Xaml.Navigation;
using novelgen.Core;
using novelgen.Models;
using novelgen.Pipeline;

namespace novelgen.Pages.Skeleton
{
    public sealed partial class SkeletonEditPage : Page
    {
        private NovelPipeline _pipeline;
        private StorySkeleton _draft;
        private List<TextBox> _volTitles = new List<TextBox>();
        private List<TextBox> _volSynopses = new List<TextBox>();
        private List<TextBox> _volPlotGoals = new List<TextBox>();
        private List<TextBox> _volClimaxs = new List<TextBox>();
        private List<Button>  _volDeletes = new List<Button>();
        private List<Button>  _volAddChapters = new List<Button>();

        private List<TextBox> _chTitles = new List<TextBox>();
        private List<TextBox> _chSynopses = new List<TextBox>();
        private List<TextBox> _chMoods = new List<TextBox>();
        private List<TextBox> _chBeats = new List<TextBox>();
        private List<Button>  _chDeletes = new List<Button>();

        private List<TextBox> _fsDescs = new List<TextBox>();
        private List<TextBox> _fsPlanteds = new List<TextBox>();
        private List<TextBox> _fsExpecteds = new List<TextBox>();
        private List<Button>  _fsDeletes = new List<Button>();

        private TextBox _loglineBox;
        private TextBox _threeActBox;

        public SkeletonEditPage()
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
                    _pipeline = await PipelineHost.ResumeAsync(nid);
            }
            if (_pipeline == null)
            {
                if (Frame.CanGoBack) Frame.GoBack(); else Frame.Navigate(typeof(Pages.Library.MainPage));
                return;
            }
            _draft = CloneSkeleton(_pipeline.Novel.Skeleton);
            Render();
        }

        private static StorySkeleton CloneSkeleton(StorySkeleton src)
        {
            src = src ?? new StorySkeleton();
            var copy = new StorySkeleton
            {
                Id = src.Id,
                Logline = src.Logline,
                ThreeAct = src.ThreeAct,
                Volumes = (src.Volumes ?? new List<VolumeOutline>()).Select(v => new VolumeOutline
                {
                    Id = v.Id,
                    Title = v.Title,
                    Synopsis = v.Synopsis,
                    PlotGoal = v.PlotGoal,
                    Climax = v.Climax,
                    Chapters = (v.Chapters ?? new List<ChapterOutline>()).Select(c => new ChapterOutline
                    {
                        Id = c.Id,
                        Title = c.Title,
                        Synopsis = c.Synopsis,
                        Mood = c.Mood,
                        KeyBeats = c.KeyBeats == null ? new string[0] : (string[])c.KeyBeats.Clone(),
                        CharacterIds = c.CharacterIds == null ? new string[0] : (string[])c.CharacterIds.Clone(),
                        LocationId = c.LocationId,
                    }).ToList(),
                }).ToList(),
                PlantedForeshadowings = (src.PlantedForeshadowings ?? new List<Foreshadowing>()).Select(f => new Foreshadowing
                {
                    Id = f.Id,
                    Description = f.Description,
                    PlantedAtChapter = f.PlantedAtChapter,
                    ExpectedPayoffChapter = f.ExpectedPayoffChapter,
                    PaidOffAtChapter = f.PaidOffAtChapter,
                    Status = f.Status,
                    Notes = f.Notes,
                }).ToList(),
                GeneratedAt = src.GeneratedAt,
            };
            return copy;
        }

        private void ClearRefs()
        {
            _volTitles.Clear(); _volSynopses.Clear(); _volPlotGoals.Clear(); _volClimaxs.Clear();
            _volDeletes.Clear(); _volAddChapters.Clear();
            _chTitles.Clear(); _chSynopses.Clear(); _chMoods.Clear(); _chBeats.Clear(); _chDeletes.Clear();
            _fsDescs.Clear(); _fsPlanteds.Clear(); _fsExpecteds.Clear(); _fsDeletes.Clear();
        }

        private TextBlock SectionHeader(string title, Brush color = null)
        {
            return new TextBlock
            {
                Text = title,
                FontSize = 18,
                FontWeight = Windows.UI.Text.FontWeights.SemiLight,
                Margin = new Thickness(12, 18, 12, 4),
                Foreground = color ?? (Brush)Application.Current.Resources["PhoneForegroundBrush"],
            };
        }

        private TextBlock SmallLabel(string s) => new TextBlock
        {
            Text = s,
            FontSize = 12,
            Foreground = (Brush)Application.Current.Resources["PhoneSubtleBrush"],
            Margin = new Thickness(0, 4, 0, 0),
        };

        private Border CardContainer(string headerText, out TextBlock header, out StackPanel sp, bool accent = false)
        {
            var border = new Border
            {
                Style = (Style)Application.Current.Resources["PhoneListItemBorderStyle"],
                Margin = new Thickness(8, 4, 8, 4),
                Background = accent ? new SolidColorBrush(Color.FromArgb(255, 0x1A, 0x29, 0x3B)) : null,
            };
            sp = new StackPanel { Margin = new Thickness(8, 4, 4, 6) };
            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            header = new TextBlock
            {
                Text = headerText,
                FontWeight = Windows.UI.Text.FontWeights.SemiBold,
                FontSize = 15,
            };
            Grid.SetColumn(header, 0);
            grid.Children.Add(header);
            sp.Children.Add(grid);
            border.Child = sp;
            return border;
        }

        private Button DelBtn(string glyph, Action<int> onClick, int idx, int fontSize = 12)
        {
            var b = new Button
            {
                Content = glyph,
                FontSize = fontSize,
                MinWidth = 36,
                Padding = new Thickness(6, 2, 6, 2),
                Margin = new Thickness(0, -4, 0, 0),
            };
            int captured = idx;
            b.Click += (s, ev) => onClick(captured);
            return b;
        }

        private void Render()
        {
            Editor.Children.Clear();
            ClearRefs();

            Editor.Children.Add(SectionHeader("一句话故事"));
            _loglineBox = new TextBox { Text = _draft.Logline ?? "", AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, MinHeight = 50 };
            Editor.Children.Add(_loglineBox);

            Editor.Children.Add(SectionHeader("三幕结构"));
            _threeActBox = new TextBox { Text = _draft.ThreeAct ?? "", AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, MinHeight = 60 };
            Editor.Children.Add(_threeActBox);

            // ── 卷 / 章 ──
            int globalOrder = 0;
            for (int vi = 0; vi < _draft.Volumes.Count; vi++)
            {
                var vol = _draft.Volumes[vi];
                TextBlock hdr; StackPanel sp;
                var card = CardContainer($"卷 {vi + 1} · {vol.Title}", out hdr, out sp, accent: true);
                var grid = (Grid)sp.Children[0];

                var delBtn = DelBtn("✕ 卷", i => { ApplyToDraft(); _draft.Volumes.RemoveAt(i); Render(); }, vi);
                _volDeletes.Add(delBtn);
                Grid.SetColumn(delBtn, 1);
                grid.Children.Add(delBtn);

                var title = new TextBox { Text = vol.Title ?? "", FontSize = 14, FontWeight = Windows.UI.Text.FontWeights.SemiBold };
                _volTitles.Add(title);
                sp.Children.Add(title);

                var syn = new TextBox { Text = vol.Synopsis ?? "", AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, MinHeight = 40 };
                _volSynopses.Add(syn);
                sp.Children.Add(SmallLabel("卷梗概"));
                sp.Children.Add(syn);

                var pg = new TextBox { Text = vol.PlotGoal ?? "" };
                _volPlotGoals.Add(pg);
                sp.Children.Add(SmallLabel("剧情目标"));
                sp.Children.Add(pg);

                var cx = new TextBox { Text = vol.Climax ?? "" };
                _volClimaxs.Add(cx);
                sp.Children.Add(SmallLabel("高潮"));
                sp.Children.Add(cx);

                // 章节
                sp.Children.Add(SmallLabel($"章节 ({vol.Chapters.Count})"));
                for (int ci = 0; ci < vol.Chapters.Count; ci++)
                {
                    var ch = vol.Chapters[ci];
                    globalOrder++;
                    var chGrid = new Grid { Margin = new Thickness(0, 6, 0, 4) };
                    chGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                    chGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

                    var chSp = new StackPanel();
                    chSp.Children.Add(new TextBlock
                    {
                        Text = $"第 {globalOrder} 章",
                        FontSize = 12,
                        Foreground = (Brush)Application.Current.Resources["PhoneSubtleBrush"],
                    });
                    var chTitle = new TextBox { Text = ch.Title ?? "", FontSize = 14, FontWeight = Windows.UI.Text.FontWeights.SemiBold };
                    _chTitles.Add(chTitle);
                    chSp.Children.Add(chTitle);
                    var chSyn = new TextBox { Text = ch.Synopsis ?? "", AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, MinHeight = 50 };
                    _chSynopses.Add(chSyn);
                    chSp.Children.Add(SmallLabel("梗概"));
                    chSp.Children.Add(chSyn);
                    var chMood = new TextBox { Text = ch.Mood ?? "" };
                    _chMoods.Add(chMood);
                    chSp.Children.Add(SmallLabel("氛围（紧张/温暖/悬疑…）"));
                    chSp.Children.Add(chMood);
                    var chBeats = new TextBox
                    {
                        Text = ch.KeyBeats == null ? "" : string.Join(" / ", ch.KeyBeats),
                        AcceptsReturn = true,
                        TextWrapping = TextWrapping.Wrap,
                        MinHeight = 50,
                    };
                    _chBeats.Add(chBeats);
                    chSp.Children.Add(SmallLabel("节拍（用 / 隔开）"));
                    chSp.Children.Add(chBeats);

                    Grid.SetColumn(chSp, 0);
                    chGrid.Children.Add(chSp);

                    int capVi = vi, capCi = ci;
                    var chDel = DelBtn("✕", i =>
                    {
                        ApplyToDraft(); // 先收集未保存的输入，避免重建 UI 时丢失
                        _draft.Volumes[capVi].Chapters.RemoveAt(capCi);
                        Render();
                    }, ci, 13);
                    _chDeletes.Add(chDel);
                    Grid.SetColumn(chDel, 1);
                    chGrid.Children.Add(chDel);

                    sp.Children.Add(chGrid);
                }

                var addChBtn = new Button
                {
                    Content = "+ 加章节",
                    FontSize = 12,
                    Padding = new Thickness(8, 2, 8, 2),
                    Margin = new Thickness(0, 4, 0, 0),
                };
                int capturedVi = vi;
                addChBtn.Click += (s, ev) =>
                {
                    ApplyToDraft(); // 先收集未保存的输入，避免重建 UI 时丢失
                    _draft.Volumes[capturedVi].Chapters.Add(new ChapterOutline
                    {
                        Id = Guid.NewGuid().ToString(),
                        Title = "新章节",
                        KeyBeats = new string[0],
                    });
                    Render();
                };
                _volAddChapters.Add(addChBtn);
                sp.Children.Add(addChBtn);

                Editor.Children.Add(card);
            }

            var addVolBtn = new Button
            {
                Content = "+ 加卷",
                FontSize = 13,
                Padding = new Thickness(10, 4, 10, 4),
                Margin = new Thickness(12, 8, 0, 0),
            };
            addVolBtn.Click += (s, ev) =>
            {
                ApplyToDraft(); // 先收集未保存的输入，避免重建 UI 时丢失
                _draft.Volumes.Add(new VolumeOutline
                {
                    Id = Guid.NewGuid().ToString(),
                    Title = "新卷",
                    Chapters = new List<ChapterOutline>(),
                });
                Render();
            };
            Editor.Children.Add(addVolBtn);

            // ── 伏笔 ──
            Editor.Children.Add(SectionHeader($"伏笔 ({_draft.PlantedForeshadowings.Count})"));
            for (int fi = 0; fi < _draft.PlantedForeshadowings.Count; fi++)
            {
                var f = _draft.PlantedForeshadowings[fi];
                TextBlock hdr; StackPanel sp;
                var card = CardContainer($"伏笔 {fi + 1}", out hdr, out sp);
                var grid = (Grid)sp.Children[0];
                var del = DelBtn("✕", i => { ApplyToDraft(); _draft.PlantedForeshadowings.RemoveAt(i); Render(); }, fi);
                _fsDeletes.Add(del);
                Grid.SetColumn(del, 1);
                grid.Children.Add(del);

                var desc = new TextBox { Text = f.Description ?? "", AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, MinHeight = 40 };
                _fsDescs.Add(desc);
                sp.Children.Add(SmallLabel("描述"));
                sp.Children.Add(desc);

                var planted = new TextBox
                {
                    Text = f.PlantedAtChapter.ToString(),
                    InputScope = new InputScope { Names = { new InputScopeName { NameValue = InputScopeNameValue.Number } } },
                };
                _fsPlanteds.Add(planted);
                sp.Children.Add(SmallLabel("埋设章号"));
                sp.Children.Add(planted);

                var exp = new TextBox
                {
                    Text = f.ExpectedPayoffChapter.ToString(),
                    InputScope = new InputScope { Names = { new InputScopeName { NameValue = InputScopeNameValue.Number } } },
                };
                _fsExpecteds.Add(exp);
                sp.Children.Add(SmallLabel("预期回收章号"));
                sp.Children.Add(exp);

                Editor.Children.Add(card);
            }
        }

        // helper for indexing the closure correctly
        private void OnAddVolumeClick(object sender, RoutedEventArgs e)
        {
            ApplyToDraft(); // 先收集未保存的输入，避免重建 UI 时丢失
            _draft.Volumes.Add(new VolumeOutline
            {
                Id = Guid.NewGuid().ToString(),
                Title = "新卷",
                Chapters = new List<ChapterOutline>(),
            });
            Render();
        }

        private void OnAddForeshadowingClick(object sender, RoutedEventArgs e)
        {
            ApplyToDraft(); // 先收集未保存的输入，避免重建 UI 时丢失
            _draft.PlantedForeshadowings.Add(new Foreshadowing
            {
                Id = Guid.NewGuid().ToString(),
                Description = "新伏笔",
                PlantedAtChapter = 1,
                ExpectedPayoffChapter = 5,
                Status = "Planted",
            });
            Render();
        }

        private async void OnSaveClick(object sender, RoutedEventArgs e)
        {
            try
            {
                ApplyToDraft();
                _pipeline.Novel.Skeleton = _draft;
                // 重建 Chapters 占位
                _pipeline.Novel.Chapters = new List<Chapter>();
                int order = 0;
                foreach (var vol in _draft.Volumes)
                {
                    foreach (var co in vol.Chapters)
                    {
                        order++;
                        _pipeline.Novel.Chapters.Add(new Chapter
                        {
                            Order = order,
                            VolumeId = vol.Id,
                            Outline = co,
                            Status = ChapterStatus.Pending,
                        });
                    }
                }
                await DataManager.SaveNovelAsync(_pipeline.Novel);
                if (Frame.CanGoBack) Frame.GoBack();
                else Frame.Navigate(typeof(SkeletonPage), _pipeline.Novel.Id);
            }
            catch (Exception ex)
            {
                await new Windows.UI.Popups.MessageDialog("保存失败：" + ex.Message).ShowAsync();
            }
        }

        private void ApplyToDraft()
        {
            _draft.Logline = _loglineBox?.Text ?? "";
            _draft.ThreeAct = _threeActBox?.Text ?? "";

            for (int i = 0; i < _draft.Volumes.Count && i < _volTitles.Count; i++)
            {
                _draft.Volumes[i].Title = _volTitles[i].Text;
                _draft.Volumes[i].Synopsis = _volSynopses[i].Text;
                _draft.Volumes[i].PlotGoal = _volPlotGoals[i].Text;
                _draft.Volumes[i].Climax = _volClimaxs[i].Text;
            }

            // 章节按全局顺序写入
            int gi = 0;
            for (int vi = 0; vi < _draft.Volumes.Count; vi++)
            {
                var vol = _draft.Volumes[vi];
                for (int ci = 0; ci < vol.Chapters.Count; ci++)
                {
                    if (gi >= _chTitles.Count) break;
                    vol.Chapters[ci].Title = _chTitles[gi].Text;
                    vol.Chapters[ci].Synopsis = _chSynopses[gi].Text;
                    vol.Chapters[ci].Mood = _chMoods[gi].Text;
                    vol.Chapters[ci].KeyBeats = (_chBeats[gi].Text ?? "")
                        .Split(new[] { '/', '、', ',', '，', ';', '\n' }, StringSplitOptions.RemoveEmptyEntries);
                    gi++;
                }
            }

            for (int i = 0; i < _draft.PlantedForeshadowings.Count && i < _fsDescs.Count; i++)
            {
                _draft.PlantedForeshadowings[i].Description = _fsDescs[i].Text;
                int.TryParse(_fsPlanteds[i].Text, out int p);
                int.TryParse(_fsExpecteds[i].Text, out int e);
                _draft.PlantedForeshadowings[i].PlantedAtChapter = p;
                _draft.PlantedForeshadowings[i].ExpectedPayoffChapter = e;
            }
        }

        private void OnBackClick(object sender, RoutedEventArgs e)
        {
            if (Frame.CanGoBack) Frame.GoBack();
            else Frame.Navigate(typeof(Pages.Library.MainPage));
        }
    }
}
