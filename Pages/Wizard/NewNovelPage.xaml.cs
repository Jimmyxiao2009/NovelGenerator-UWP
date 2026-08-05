using System.Linq;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using novelgen.AI;
using novelgen.Core;
using novelgen.Models;
using novelgen.Pipeline;

namespace novelgen.Pages.Wizard
{
    public sealed partial class NewNovelPage : Page
    {
        private GenreCatalog _catalog;

        public NewNovelPage()
        {
            this.InitializeComponent();
            _catalog = new GenreCatalog
            {
                Genres = GenrePresets.BuiltInGenres(),
                Presets = GenrePresets.BuiltInPresets(),
            };
            GenreBox.ItemsSource = _catalog.Genres;
            GenreBox.SelectedIndex = 0;
        }

        protected override void OnNavigatedTo(Windows.UI.Xaml.Navigation.NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);
            LoadApiProfiles();
            // 用上次保存的「每章目标字数」偏好作为默认值（此前 DefaultTargetWords 从未被读取）
            TargetWordsBox.Text = AppSettings.DefaultTargetWords.ToString();
        }

        private void LoadApiProfiles()
        {
            var profiles = DataManager.ApiProfiles;
            if (profiles.Count == 0)
            {
                ApiHint.Text = "没有 API 配置。先到「API 配置」添加。";
                DraftApiBox.IsEnabled = false;
                PolishApiBox.IsEnabled = false;
                return;
            }
            DraftApiBox.ItemsSource = profiles;
            PolishApiBox.ItemsSource = profiles;
            DraftApiBox.SelectedItem = profiles.FirstOrDefault(p => p.Id == AppSettings.DraftApiProfileId) ?? profiles[0];
            PolishApiBox.SelectedItem = profiles.FirstOrDefault(p => p.Id == AppSettings.PolishApiProfileId) ?? profiles[0];
        }

        private void OnGenreChanged(object sender, SelectionChangedEventArgs e)
        {
            var g = GenreBox.SelectedItem as Genre;
            if (g == null) return;
            SubGenreBox.ItemsSource = g.SubGenres;
            SubGenreBox.SelectedIndex = g.SubGenres.Count > 0 ? 0 : -1;
            var presets = _catalog.Presets.Where(p => p.Genre == g.Name).ToList();
            if (presets.Count == 0) presets = _catalog.Presets.Where(p => p.Name == "通用").ToList();
            PresetBox.ItemsSource = presets;
            PresetBox.SelectedIndex = 0;
        }

        private void OnCancelClick(object sender, RoutedEventArgs e)
        {
            if (Frame.CanGoBack) Frame.GoBack(); else Frame.Navigate(typeof(Pages.Library.MainPage));
        }

        private async void OnStartClick(object sender, RoutedEventArgs e)
        {
            // 立即禁用按钮，防止双击创建两条流水线/两本小说
            var startBtn = sender as Button;
            if (startBtn != null && !startBtn.IsEnabled) return;
            if (startBtn != null) startBtn.IsEnabled = false;
            bool navigated = false;
            try
            {
                StatusText.Text = "";
                var prompt = PromptBox.Text?.Trim();
                if (string.IsNullOrEmpty(prompt))
                {
                    StatusText.Text = "请填写小说提示词";
                    return;
                }
                var g = GenreBox.SelectedItem as Genre;
                var sub = SubGenreBox.SelectedItem as string ?? "";
                var preset = PresetBox.SelectedItem as StylePreset;

                int.TryParse(TargetChaptersBox.Text, out int ch);
                int.TryParse(TargetWordsBox.Text, out int w);
                int.TryParse(TargetVolumesBox.Text, out int vols);
                int.TryParse(WorldCharsBox.Text, out int wCh);
                int.TryParse(WorldLocsBox.Text, out int wLoc);
                int.TryParse(WorldFactionsBox.Text, out int wFac);
                int.TryParse(WorldRulesBox.Text, out int wRu);

                var draft = DraftApiBox.SelectedItem as ApiProfile;
                var polish = PolishApiBox.SelectedItem as ApiProfile;
                if (draft == null)
                {
                    StatusText.Text = "请先到「API 配置」添加模型。";
                    return;
                }
                AppSettings.DraftApiProfileId = draft.Id;
                AppSettings.PolishApiProfileId = (polish ?? draft).Id;

                var title = TitleBox.Text?.Trim();
                var p = NovelPipeline.Create(new UserPromptLike { Text = prompt }, g?.Name ?? "通用", sub, preset?.Id, title);
                p.Novel.TargetChapterCount = ch > 0 ? ch : 20;
                p.Novel.TargetWordsPerChapter = w > 0 ? w : 2500;
                // 记住本次的每章目标字数，作为下次新建小说的默认值
                if (w > 0) AppSettings.DefaultTargetWords = w;
                p.Novel.TargetVolumeCount = vols >= 0 ? vols : 0;
                p.Novel.WorldCharacterCount = wCh > 0 ? wCh : 3;
                p.Novel.WorldLocationCount = wLoc > 0 ? wLoc : 2;
                p.Novel.WorldFactionCount = wFac > 0 ? wFac : 2;
                p.Novel.WorldRuleCount = wRu > 0 ? wRu : 3;
                p.Novel.ApiProfileId = draft.Id;

                PipelineHost.Start(p);
                await Core.DataManager.SaveNovelAsync(p.Novel);

                Frame.Navigate(typeof(Pages.WorldView.WorldSettingPage), p.Novel.Id);
                navigated = true; // 成功离开本页，按钮保持禁用即可
            }
            catch (System.Exception ex)
            {
                StatusText.Text = "✗ 创建失败：" + ex.Message;
            }
            finally
            {
                if (!navigated && startBtn != null) startBtn.IsEnabled = true;
            }
        }
    }
}
