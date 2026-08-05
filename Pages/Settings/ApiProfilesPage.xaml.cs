using System;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Navigation;
using novelgen.Core;
using novelgen.Models;

namespace novelgen.Pages.Settings
{
    public sealed partial class ApiProfilesPage : Page
    {
        public ApiProfilesPage()
        {
            this.InitializeComponent();
        }

        protected override void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);
            Refresh();
        }

        private void Refresh()
        {
            var list = DataManager.ApiProfiles;
            ProfileList.ItemsSource = list;
            EmptyHint.Visibility = list.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        }

        private void OnBackClick(object sender, RoutedEventArgs e)
        {
            if (Frame.CanGoBack) Frame.GoBack();
            else Frame.Navigate(typeof(Pages.Library.MainPage));
        }

        private void OnAddClick(object sender, RoutedEventArgs e)
        {
            var p = new ApiProfile { Name = "新 API" };
            Frame.Navigate(typeof(ApiProfileEditPage), p);
        }

        private void OnProfileClick(object sender, ItemClickEventArgs e)
        {
            var p = e.ClickedItem as ApiProfile;
            if (p == null) return;
            Frame.Navigate(typeof(ApiProfileEditPage), p);
        }
    }
}
