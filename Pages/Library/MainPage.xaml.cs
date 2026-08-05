using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Navigation;
using novelgen.Core;
using novelgen.Models;
using novelgen.Pipeline;

namespace novelgen.Pages.Library
{
    public sealed partial class MainPage : Page
    {
        private ObservableCollection<Novel> _all = new ObservableCollection<Novel>();
        private ObservableCollection<Novel> _active = new ObservableCollection<Novel>();
        private ObservableCollection<Novel> _done = new ObservableCollection<Novel>();

        public MainPage()
        {
            this.InitializeComponent();
        }

        protected override async void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);
            await DataManager.LoadAsync();
            Refresh();
            UpdateThemeButton();
        }

        private void UpdateThemeButton()
        {
            ThemeBtn.Label = AppSettings.IsDark ? "浅色" : "深色";
        }

        private void OnToggleThemeClick(object sender, RoutedEventArgs e)
        {
            AppSettings.IsDark = !AppSettings.IsDark;
            var frame = Window.Current.Content as Frame;
            if (frame != null)
            {
                frame.RequestedTheme = AppSettings.IsDark ? ElementTheme.Dark : ElementTheme.Light;
            }
            App.ApplyTheme(AppSettings.IsDark);
            UpdateThemeButton();
        }

        private void Refresh()
        {
            var all = new ObservableCollection<Novel>();
            var active = new ObservableCollection<Novel>();
            var done = new ObservableCollection<Novel>();

            if (DataManager.Data?.Novels != null)
            {
                foreach (var n in DataManager.Data.Novels.OrderByDescending(x => x.UpdatedAt))
                {
                    all.Add(n);
                    if (IsActiveState(n.State)) active.Add(n);
                    else done.Add(n);
                }
            }

            _all = all;
            _active = active;
            _done = done;
            AllList.ItemsSource = _all;
            ActiveList.ItemsSource = _active;
            DoneList.ItemsSource = _done;
            EmptyHint.Visibility = _all.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            ActiveEmptyHint.Visibility = _active.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            DoneEmptyHint.Visibility = _done.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        }

        private static bool IsActiveState(PipelineState s)
        {
            switch (s)
            {
                case PipelineState.Exported:
                case PipelineState.AwaitingReview2:
                case PipelineState.Error:
                    return false;
                default:
                    return true;
            }
        }

        private async void OnNewNovelClick(object sender, RoutedEventArgs e)
        {
            if (DataManager.ApiProfiles.Count == 0)
            {
                var r = await ConfirmBeforeNav("尚未配置 API", "需要先添加一个 API 才能开始生成。\n现在去配置？");
                if (r) Frame.Navigate(typeof(Pages.Settings.ApiProfilesPage));
                return;
            }
            Frame.Navigate(typeof(Pages.Wizard.NewNovelPage));
        }

        private async System.Threading.Tasks.Task<bool> ConfirmBeforeNav(string title, string message)
        {
            var dlg = new Windows.UI.Popups.MessageDialog(message, title);
            dlg.Commands.Add(new Windows.UI.Popups.UICommand("是", null, "yes"));
            dlg.Commands.Add(new Windows.UI.Popups.UICommand("稍后", null, "no"));
            dlg.DefaultCommandIndex = 0;
            var res = await dlg.ShowAsync();
            return (res.Id as string) == "yes";
        }

        private void OnApiSettingsClick(object sender, RoutedEventArgs e)
        {
            Frame.Navigate(typeof(Pages.Settings.ApiProfilesPage));
        }

        private void OnRefreshClick(object sender, RoutedEventArgs e)
        {
            Refresh();
        }

        private bool _isNavigating;

        private async void OnNovelClick(object sender, ItemClickEventArgs e)
        {
            if (_isNavigating) return;
            var novel = e.ClickedItem as Novel;
            if (novel == null) return;
            await NavigateToNovelAsync(novel);
        }

        private async void OnEnterClick(object sender, RoutedEventArgs e)
        {
            if (_isNavigating) return;
            var btn = sender as Button;
            if (btn == null) return;
            var novel = btn.Tag as Novel;
            if (novel == null) return;
            await NavigateToNovelAsync(novel);
        }

        private async System.Threading.Tasks.Task NavigateToNovelAsync(Novel novel)
        {
            _isNavigating = true;
            try
            {
                var p = await PipelineHost.ResumeAsync(novel.Id);
                if (p == null) return;
                Frame.Navigate(RouteForState(p.State), p.Novel.Id);
            }
            catch (Exception ex)
            {
                var dlg = new Windows.UI.Popups.MessageDialog("进入失败：" + ex.Message + "\n\n" + ex.StackTrace, "错误");
                await dlg.ShowAsync();
            }
            finally
            {
                _isNavigating = false;
            }
        }

        private async void OnDeleteClick(object sender, RoutedEventArgs e)
        {
            var btn = sender as Button;
            if (!(btn?.Tag is Novel novel)) return;

            var dlg = new Windows.UI.Popups.MessageDialog($"确定要删除「{novel.Title}」吗？此操作不可恢复。", "确认删除");
            dlg.Commands.Add(new Windows.UI.Popups.UICommand("删除", null, "del"));
            dlg.Commands.Add(new Windows.UI.Popups.UICommand("取消", null, "cancel"));
            dlg.DefaultCommandIndex = 1;
            var res = await dlg.ShowAsync();
            if ((res.Id as string) != "del") return;

            try
            {
                await DataManager.DeleteNovelAsync(novel.Id);
                Refresh();
            }
            catch (Exception ex)
            {
                var errDlg = new Windows.UI.Popups.MessageDialog("删除失败：" + ex.Message);
                await errDlg.ShowAsync();
            }
        }

        public static Type RouteForState(PipelineState state)
        {
            switch (state)
            {
                case PipelineState.Draft:
                case PipelineState.WorldSettingDraft:
                    return typeof(Pages.WorldView.WorldSettingPage);
                case PipelineState.SkeletonDraft:
                case PipelineState.AwaitingReview1:
                    return typeof(Pages.ReviewGate1Page);
                case PipelineState.ChapterDrafting:
                case PipelineState.ChapterPolishing:
                    return typeof(Pages.Generation.BatchGenerationPage);
                case PipelineState.AwaitingReview2:
                    return typeof(Pages.Generation.DirectoryPage);
                case PipelineState.Composing:
                case PipelineState.Composed:
                case PipelineState.Exporting:
                case PipelineState.Exported:
                    return typeof(Pages.Generation.ExportPage);
                default:
                    return typeof(Pages.Library.MainPage);
            }
        }
    }
}
