using System;
using System.Collections.Generic;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;

namespace novelgen.Common
{
    public class NavigationHelper : DependencyObject
    {
        public static readonly DependencyProperty PageKeyProperty =
            DependencyProperty.RegisterAttached("PageKey", typeof(string), typeof(NavigationHelper), new PropertyMetadata(null));

        public static string GetPageKey(DependencyObject obj) => (string)obj.GetValue(PageKeyProperty);
        public static void SetPageKey(DependencyObject obj, string value) => obj.SetValue(PageKeyProperty, value);

        public static readonly DependencyProperty NavigatedHandlerProperty =
            DependencyProperty.RegisterAttached("NavigatedHandler", typeof(object), typeof(NavigationHelper), new PropertyMetadata(null, OnNavigatedHandlerChanged));

        public static object GetNavigatedHandler(DependencyObject obj) => obj.GetValue(NavigatedHandlerProperty);
        public static void SetNavigatedHandler(DependencyObject obj, object value) => obj.SetValue(NavigatedHandlerProperty, value);

        private static void OnNavigatedHandlerChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            var page = d as Page;
            if (page == null) return;
            // 先退订再订阅：该回调每次属性变化都会触发，否则会重复挂接
            page.Loaded -= Page_Loaded;
            if (e.NewValue != null) page.Loaded += Page_Loaded;
        }

        private static void Page_Loaded(object sender, RoutedEventArgs e)
        {
            // placeholder for future per-page setup
        }
    }
}
