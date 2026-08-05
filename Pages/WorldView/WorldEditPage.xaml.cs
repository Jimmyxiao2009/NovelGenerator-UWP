using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Windows.UI;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Media;
using Windows.UI.Xaml.Navigation;
using novelgen.Core;
using novelgen.Models;
using novelgen.Pipeline;

namespace novelgen.Pages.WorldView
{
    public sealed partial class WorldEditPage : Page
    {
        private NovelPipeline _pipeline;
        private WorldSetting _draft;
        private bool _loaded;

        // 跟踪列表项的 TextBox 引用，便于回写
        private readonly List<TextBox> _charNames = new List<TextBox>();
        private readonly List<TextBox> _charRoles = new List<TextBox>();
        private readonly List<TextBox> _charPersonality = new List<TextBox>();
        private readonly List<TextBox> _charBackground = new List<TextBox>();
        private readonly List<TextBox> _charAppearance = new List<TextBox>();
        private readonly List<TextBox> _charAbility = new List<TextBox>();
        private readonly List<TextBox> _charGoal = new List<TextBox>();
        private readonly List<Button>  _charDelete = new List<Button>();
        private readonly List<Character> _charOriginal = new List<Character>();

        private readonly List<TextBox> _locNames = new List<TextBox>();
        private readonly List<TextBox> _locTypes = new List<TextBox>();
        private readonly List<TextBox> _locDesc = new List<TextBox>();
        private readonly List<Button>  _locDelete = new List<Button>();
        private readonly List<Location> _locOriginal = new List<Location>();

        private readonly List<TextBox> _facNames = new List<TextBox>();
        private readonly List<TextBox> _facIdeology = new List<TextBox>();
        private readonly List<TextBox> _facTerritory = new List<TextBox>();
        private readonly List<TextBox> _facInfluence = new List<TextBox>();
        private readonly List<TextBox> _facDescription = new List<TextBox>();
        private readonly List<Button>  _facDelete = new List<Button>();
        private readonly List<Faction> _facOriginal = new List<Faction>();

        private readonly List<TextBox> _ruleCategories = new List<TextBox>();
        private readonly List<TextBox> _ruleDescriptions = new List<TextBox>();
        private readonly List<CheckBox> _ruleIsHard = new List<CheckBox>();
        private readonly List<Button>   _ruleDelete = new List<Button>();
        private readonly List<WorldRule> _ruleOriginal = new List<WorldRule>();

        public WorldEditPage()
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
            LoadAndRender();
        }

        private void LoadAndRender()
        {
            _draft = _pipeline.Novel.WorldSetting ?? new WorldSetting();
            // 复制出来一份编辑
            _draft = CloneWorld(_draft);
            if (_pipeline.Novel.WorldSetting == null)
                EmptyHint.Visibility = Visibility.Visible;
            else
                EmptyHint.Visibility = Visibility.Collapsed;
            Render();
            _loaded = true;
        }

        private static WorldSetting CloneWorld(WorldSetting src)
        {
            var copy = new WorldSetting
            {
                Id = src.Id,
                Synopsis = src.Synopsis,
                Themes = src.Themes == null ? new string[0] : (string[])src.Themes.Clone(),
                Characters = (src.Characters ?? new List<Character>()).Select(c => new Character
                {
                    Id = c.Id, Name = c.Name, Role = c.Role, Personality = c.Personality,
                    Background = c.Background, Appearance = c.Appearance, Ability = c.Ability,
                    Goal = c.Goal,
                }).ToList(),
                Locations = (src.Locations ?? new List<Location>()).Select(l => new Location
                {
                    Id = l.Id, Name = l.Name, Type = l.Type, Description = l.Description,
                }).ToList(),
                Factions = (src.Factions ?? new List<Faction>()).Select(f => new Faction
                {
                    Id = f.Id, Name = f.Name, Ideology = f.Ideology, Territory = f.Territory,
                    Influence = f.Influence, Description = f.Description,
                }).ToList(),
                Rules = (src.Rules ?? new List<WorldRule>()).Select(r => new WorldRule
                {
                    Id = r.Id, Category = r.Category, Description = r.Description, IsHard = r.IsHard,
                }).ToList(),
                Timeline = new List<WorldEvent>(src.Timeline ?? new List<WorldEvent>()),
                GeneratedAt = src.GeneratedAt,
            };
            return copy;
        }

        private TextBox _synopsisBox;
        private TextBox _themesBox;

        private void Render()
        {
            Editor.Children.Clear();
            ClearRefs();

            Editor.Children.Add(SectionHeader("梗概"));
            _synopsisBox = new TextBox
            {
                AcceptsReturn = true,
                TextWrapping = TextWrapping.Wrap,
                MinHeight = 100,
                Text = _draft.Synopsis ?? "",
                FontSize = 15,
            };
            Editor.Children.Add(_synopsisBox);

            Editor.Children.Add(SectionHeader("主题（用、隔开）"));
            _themesBox = new TextBox
            {
                Text = _draft.Themes == null ? "" : string.Join("、", _draft.Themes),
                FontSize = 15,
            };
            Editor.Children.Add(_themesBox);

            // 人物
            Editor.Children.Add(SectionHeader($"人物 ({_draft.Characters.Count})"));
            for (int i = 0; i < _draft.Characters.Count; i++)
                Editor.Children.Add(BuildCharacterCard(i, _draft.Characters[i]));
            Editor.Children.Add(AddRowButton("+ 添加人物", () => { ApplyToDraft(); _draft.Characters.Add(new Character { Id = Guid.NewGuid().ToString(), Name = "新人物" }); Render(); }));

            // 地点
            Editor.Children.Add(SectionHeader($"地点 ({_draft.Locations.Count})"));
            for (int i = 0; i < _draft.Locations.Count; i++)
                Editor.Children.Add(BuildLocationCard(i, _draft.Locations[i]));
            Editor.Children.Add(AddRowButton("+ 添加地点", () => { ApplyToDraft(); _draft.Locations.Add(new Location { Id = Guid.NewGuid().ToString(), Name = "新地点" }); Render(); }));

            // 势力
            Editor.Children.Add(SectionHeader($"势力 ({_draft.Factions.Count})"));
            for (int i = 0; i < _draft.Factions.Count; i++)
                Editor.Children.Add(BuildFactionCard(i, _draft.Factions[i]));
            Editor.Children.Add(AddRowButton("+ 添加势力", () => { ApplyToDraft(); _draft.Factions.Add(new Faction { Id = Guid.NewGuid().ToString(), Name = "新势力" }); Render(); }));

            // 规则
            Editor.Children.Add(SectionHeader($"规则 ({_draft.Rules.Count})"));
            for (int i = 0; i < _draft.Rules.Count; i++)
                Editor.Children.Add(BuildRuleCard(i, _draft.Rules[i]));
            Editor.Children.Add(AddRowButton("+ 添加规则", () => { ApplyToDraft(); _draft.Rules.Add(new WorldRule { Id = Guid.NewGuid().ToString(), Description = "新规则", IsHard = true }); Render(); }));
        }

        private void ClearRefs()
        {
            _charNames.Clear(); _charRoles.Clear(); _charPersonality.Clear();
            _charBackground.Clear(); _charAppearance.Clear(); _charAbility.Clear(); _charGoal.Clear();
            _charDelete.Clear(); _charOriginal.Clear();
            _locNames.Clear(); _locTypes.Clear(); _locDesc.Clear(); _locDelete.Clear(); _locOriginal.Clear();
            _facNames.Clear(); _facIdeology.Clear(); _facTerritory.Clear(); _facInfluence.Clear();
            _facDescription.Clear(); _facDelete.Clear(); _facOriginal.Clear();
            _ruleCategories.Clear(); _ruleDescriptions.Clear(); _ruleIsHard.Clear(); _ruleDelete.Clear(); _ruleOriginal.Clear();
        }

        private TextBlock SectionHeader(string title)
        {
            return new TextBlock
            {
                Text = title,
                FontSize = 18,
                FontWeight = Windows.UI.Text.FontWeights.SemiLight,
                Margin = new Thickness(12, 18, 12, 4),
                Foreground = (Brush)Application.Current.Resources["PhoneForegroundBrush"],
            };
        }

        private Border CardContainer(string headerText, out TextBlock header)
        {
            var border = new Border
            {
                Style = (Style)Application.Current.Resources["PhoneListItemBorderStyle"],
                Margin = new Thickness(8, 4, 8, 4),
            };
            var sp = new StackPanel { Margin = new Thickness(8, 4, 4, 6) };
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

        private Button DeleteButton(out int refIdx, List<Button> delList, int origIndex, Action<int> onClick)
        {
            var btn = new Button
            {
                Content = "✕",
                FontSize = 14,
                MinWidth = 36,
                Padding = new Thickness(6, 2, 6, 2),
                Margin = new Thickness(0, -4, 0, 0),
            };
            refIdx = delList.Count;
            int captured = origIndex;
            btn.Click += (s, ev) => { onClick(captured); };
            delList.Add(btn);
            return btn;
        }

        private StackPanel LabeledBox(string label, TextBox box)
        {
            var sp = new StackPanel { Margin = new Thickness(0, 4, 0, 0) };
            sp.Children.Add(new TextBlock
            {
                Text = label,
                FontSize = 12,
                Foreground = (Brush)Application.Current.Resources["PhoneSubtleBrush"],
            });
            box.Margin = new Thickness(0, 2, 0, 0);
            sp.Children.Add(box);
            return sp;
        }

        private Button AddRowButton(string text, Action onClick)
        {
            var b = new Button
            {
                Content = text,
                HorizontalAlignment = HorizontalAlignment.Left,
                Margin = new Thickness(12, 4, 0, 0),
                Padding = new Thickness(10, 4, 10, 4),
                FontSize = 13,
            };
            b.Click += (s, ev) => onClick();
            return b;
        }

        // ── 人物卡 ──────────────────────────────────────────────
        private Border BuildCharacterCard(int idx, Character c)
        {
            TextBlock header;
            var border = CardContainer($"人物 {idx + 1} · {c.Name}", out header);
            var sp = (StackPanel)border.Child;
            var grid = (Grid)sp.Children[0];

            int refIdx;
            var delBtn = DeleteButton(out refIdx, _charDelete, idx, i =>
            {
                ApplyToDraft(); // 先收集未保存的输入，避免重建 UI 时丢失
                _draft.Characters.RemoveAt(i);
                Render();
            });
            Grid.SetColumn(delBtn, 1);
            grid.Children.Add(delBtn);

            _charOriginal.Add(c);

            var name = new TextBox { Text = c.Name ?? "", FontSize = 14, FontWeight = Windows.UI.Text.FontWeights.SemiBold };
            _charNames.Add(name);
            sp.Children.Add(name);

            var role = new TextBox { Text = c.Role ?? "" };
            _charRoles.Add(role);
            sp.Children.Add(LabeledBox("角色（主角/配角/反派）", role));

            var personality = new TextBox { Text = c.Personality ?? "", AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, MinHeight = 40 };
            _charPersonality.Add(personality);
            sp.Children.Add(LabeledBox("性格", personality));

            var background = new TextBox { Text = c.Background ?? "", AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, MinHeight = 40 };
            _charBackground.Add(background);
            sp.Children.Add(LabeledBox("背景", background));

            var appearance = new TextBox { Text = c.Appearance ?? "" };
            _charAppearance.Add(appearance);
            sp.Children.Add(LabeledBox("外貌", appearance));

            var ability = new TextBox { Text = c.Ability ?? "" };
            _charAbility.Add(ability);
            sp.Children.Add(LabeledBox("能力/异能", ability));

            var goal = new TextBox { Text = c.Goal ?? "" };
            _charGoal.Add(goal);
            sp.Children.Add(LabeledBox("目标", goal));

            return border;
        }

        private Border BuildLocationCard(int idx, Location l)
        {
            TextBlock header;
            var border = CardContainer($"地点 {idx + 1} · {l.Name}", out header);
            var sp = (StackPanel)border.Child;
            var grid = (Grid)sp.Children[0];
            int refIdx;
            var delBtn = DeleteButton(out refIdx, _locDelete, idx, i =>
            {
                ApplyToDraft(); // 先收集未保存的输入，避免重建 UI 时丢失
                _draft.Locations.RemoveAt(i);
                Render();
            });
            Grid.SetColumn(delBtn, 1);
            grid.Children.Add(delBtn);
            _locOriginal.Add(l);

            var name = new TextBox { Text = l.Name ?? "", FontSize = 14, FontWeight = Windows.UI.Text.FontWeights.SemiBold };
            _locNames.Add(name);
            sp.Children.Add(name);

            var type = new TextBox { Text = l.Type ?? "" };
            _locTypes.Add(type);
            sp.Children.Add(LabeledBox("类型（城市/秘境/宗门…）", type));

            var desc = new TextBox { Text = l.Description ?? "", AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, MinHeight = 50 };
            _locDesc.Add(desc);
            sp.Children.Add(LabeledBox("描述", desc));

            return border;
        }

        private Border BuildFactionCard(int idx, Faction f)
        {
            TextBlock header;
            var border = CardContainer($"势力 {idx + 1} · {f.Name}", out header);
            var sp = (StackPanel)border.Child;
            var grid = (Grid)sp.Children[0];
            int refIdx;
            var delBtn = DeleteButton(out refIdx, _facDelete, idx, i =>
            {
                ApplyToDraft(); // 先收集未保存的输入，避免重建 UI 时丢失
                _draft.Factions.RemoveAt(i);
                Render();
            });
            Grid.SetColumn(delBtn, 1);
            grid.Children.Add(delBtn);
            _facOriginal.Add(f);

            var name = new TextBox { Text = f.Name ?? "", FontSize = 14, FontWeight = Windows.UI.Text.FontWeights.SemiBold };
            _facNames.Add(name);
            sp.Children.Add(name);

            var ideology = new TextBox { Text = f.Ideology ?? "", AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, MinHeight = 40 };
            _facIdeology.Add(ideology);
            sp.Children.Add(LabeledBox("理念/宗旨", ideology));

            var territory = new TextBox { Text = f.Territory ?? "" };
            _facTerritory.Add(territory);
            sp.Children.Add(LabeledBox("势力范围", territory));

            var influence = new TextBox { Text = f.Influence ?? "" };
            _facInfluence.Add(influence);
            sp.Children.Add(LabeledBox("影响力", influence));

            var desc = new TextBox { Text = f.Description ?? "", AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, MinHeight = 40 };
            _facDescription.Add(desc);
            sp.Children.Add(LabeledBox("描述", desc));

            return border;
        }

        private Border BuildRuleCard(int idx, WorldRule r)
        {
            TextBlock header;
            var border = CardContainer($"规则 {idx + 1} · [{r.Category}]", out header);
            var sp = (StackPanel)border.Child;
            var grid = (Grid)sp.Children[0];
            int refIdx;
            var delBtn = DeleteButton(out refIdx, _ruleDelete, idx, i =>
            {
                ApplyToDraft(); // 先收集未保存的输入，避免重建 UI 时丢失
                _draft.Rules.RemoveAt(i);
                Render();
            });
            Grid.SetColumn(delBtn, 1);
            grid.Children.Add(delBtn);
            _ruleOriginal.Add(r);

            var cat = new TextBox { Text = r.Category ?? "", FontSize = 14, FontWeight = Windows.UI.Text.FontWeights.SemiBold };
            _ruleCategories.Add(cat);
            sp.Children.Add(cat);

            var desc = new TextBox { Text = r.Description ?? "", AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, MinHeight = 50 };
            _ruleDescriptions.Add(desc);
            sp.Children.Add(LabeledBox("描述", desc));

            var hardRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 4, 0, 0) };
            var cb = new CheckBox { Content = "硬规则（AI 不可违反）", IsChecked = r.IsHard };
            _ruleIsHard.Add(cb);
            hardRow.Children.Add(cb);
            sp.Children.Add(hardRow);

            return border;
        }

        // ── 添加按钮（CommandBar 也调这几个）；先 ApplyToDraft 收集未保存输入，再重建 UI ──
        private void OnAddCharacterClick(object sender, RoutedEventArgs e) { ApplyToDraft(); _draft.Characters.Add(new Character { Id = Guid.NewGuid().ToString(), Name = "新人物" }); Render(); }
        private void OnAddLocationClick(object sender, RoutedEventArgs e) { ApplyToDraft(); _draft.Locations.Add(new Location { Id = Guid.NewGuid().ToString(), Name = "新地点" }); Render(); }
        private void OnAddFactionClick(object sender, RoutedEventArgs e) { ApplyToDraft(); _draft.Factions.Add(new Faction { Id = Guid.NewGuid().ToString(), Name = "新势力" }); Render(); }
        private void OnAddRuleClick(object sender, RoutedEventArgs e) { ApplyToDraft(); _draft.Rules.Add(new WorldRule { Id = Guid.NewGuid().ToString(), Description = "新规则", IsHard = true }); Render(); }

        // ── 保存 ────────────────────────────────────────────────
        private async void OnSaveClick(object sender, RoutedEventArgs e)
        {
            try
            {
                // 保存会清掉骨架和章节，有内容时先让用户确认
                if (_pipeline.Novel.Skeleton != null || (_pipeline.Novel.Chapters?.Count ?? 0) > 0)
                {
                    var dlg = new ContentDialog
                    {
                        Title = "确认保存",
                        Content = "保存世界设定将清除已生成的骨架和所有章节，确定吗？",
                        PrimaryButtonText = "确定清除并保存",
                        SecondaryButtonText = "取消",
                    };
                    if (await dlg.ShowAsync() != ContentDialogResult.Primary) return;
                }
                ApplyToDraft();
                _pipeline.Novel.WorldSetting = _draft;
                // 改完世界 = 骨架作废
                _pipeline.Novel.Skeleton = null;
                _pipeline.Novel.Chapters = null;
                await DataManager.SaveNovelAsync(_pipeline.Novel);
                if (Frame.CanGoBack) Frame.GoBack();
                else Frame.Navigate(typeof(WorldSettingPage), _pipeline.Novel.Id);
            }
            catch (Exception ex)
            {
                await new Windows.UI.Popups.MessageDialog("保存失败：" + ex.Message).ShowAsync();
            }
        }

        private void ApplyToDraft()
        {
            _draft.Synopsis = _synopsisBox?.Text ?? "";
            _draft.Themes = (_themesBox?.Text ?? "")
                .Split(new[] { '、', ',', '，', ' ', ';' }, StringSplitOptions.RemoveEmptyEntries);

            for (int i = 0; i < _draft.Characters.Count && i < _charNames.Count; i++)
            {
                _draft.Characters[i].Name = _charNames[i].Text;
                _draft.Characters[i].Role = _charRoles[i].Text;
                _draft.Characters[i].Personality = _charPersonality[i].Text;
                _draft.Characters[i].Background = _charBackground[i].Text;
                _draft.Characters[i].Appearance = _charAppearance[i].Text;
                _draft.Characters[i].Ability = _charAbility[i].Text;
                _draft.Characters[i].Goal = _charGoal[i].Text;
            }
            for (int i = 0; i < _draft.Locations.Count && i < _locNames.Count; i++)
            {
                _draft.Locations[i].Name = _locNames[i].Text;
                _draft.Locations[i].Type = _locTypes[i].Text;
                _draft.Locations[i].Description = _locDesc[i].Text;
            }
            for (int i = 0; i < _draft.Factions.Count && i < _facNames.Count; i++)
            {
                _draft.Factions[i].Name = _facNames[i].Text;
                _draft.Factions[i].Ideology = _facIdeology[i].Text;
                _draft.Factions[i].Territory = _facTerritory[i].Text;
                _draft.Factions[i].Influence = _facInfluence[i].Text;
                _draft.Factions[i].Description = _facDescription[i].Text;
            }
            for (int i = 0; i < _draft.Rules.Count && i < _ruleCategories.Count; i++)
            {
                _draft.Rules[i].Category = _ruleCategories[i].Text;
                _draft.Rules[i].Description = _ruleDescriptions[i].Text;
                _draft.Rules[i].IsHard = _ruleIsHard[i].IsChecked ?? true;
            }
        }

        private void OnBackClick(object sender, RoutedEventArgs e)
        {
            if (Frame.CanGoBack) Frame.GoBack();
            else Frame.Navigate(typeof(Pages.Library.MainPage));
        }
    }
}
