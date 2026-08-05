using System;
using System.Threading.Tasks;
using Windows.ApplicationModel.Activation;
using Windows.UI;
using Windows.UI.Core;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Media;
using novelgen.Common;
using novelgen.Core;
using novelgen.Pipeline;

namespace novelgen
{
    public sealed partial class App : Application
    {
        public static Frame RootFrame { get; private set; }

        public App()
        {
            this.InitializeComponent();
            this.Suspending += OnSuspending;
            this.Resuming += OnResuming;
            this.UnhandledException += OnUnhandledException;
        }

        protected override async void OnLaunched(LaunchActivatedEventArgs e)
        {
            await DataManager.LoadAsync();
            PipelineHost.Initialize();

            // 运行中再次激活（磁贴/协议）时复用现有 Frame，不丢导航栈、不泄漏旧 Frame
            var rootFrame = Window.Current.Content as Frame;
            if (rootFrame == null)
            {
                rootFrame = new Frame();
                rootFrame.RequestedTheme = AppSettings.IsDark ? ElementTheme.Dark : ElementTheme.Light;
                ApplyTheme(AppSettings.IsDark);
                rootFrame.Navigated += (s, args) =>
                {
                    SuspensionManager.SessionState["LastPage"] = args.SourcePageType.FullName;
                };

                // 让操作系统 / 手机硬件返回键走 Frame.GoBack，而不是退出 App。
                // 10240+ 上手机硬件返回键会自动路由到 BackRequested，桌面标题栏返回箭头亦同。
                var navManager = SystemNavigationManager.GetForCurrentView();
                rootFrame.Navigated += (s, args) =>
                {
                    navManager.AppViewBackButtonVisibility = rootFrame.CanGoBack
                        ? AppViewBackButtonVisibility.Visible
                        : AppViewBackButtonVisibility.Collapsed;
                };
                navManager.BackRequested += (s, args) =>
                {
                    if (!args.Handled && rootFrame.CanGoBack)
                    {
                        args.Handled = true;
                        rootFrame.GoBack();
                    }
                };

                SuspensionManager.RegisterFrame(rootFrame, "AppFrame");

                if (e.PreviousExecutionState == ApplicationExecutionState.Terminated)
                    await SuspensionManager.RestoreAsync();

                Window.Current.Content = rootFrame;
            }
            RootFrame = rootFrame;

            if (rootFrame.Content == null)
                rootFrame.Navigate(typeof(Pages.Library.MainPage), e.Arguments);

            Window.Current.Activate();
        }

        public static void ApplyTheme(bool isDark)
        {
            var d = Current.Resources;
            Set(d, "PhoneBackgroundBrush",      isDark ? "#1B1B1B" : "#F2F2F2");
            Set(d, "PhoneChromeBrush",          isDark ? "#2A2A2A" : "#FFFFFF");
            Set(d, "PhoneForegroundBrush",      isDark ? "#EEEEEE" : "#222222");
            Set(d, "PhoneSubtleBrush",          isDark ? "#888888" : "#999999");
            Set(d, "PhoneBorderBrush",          isDark ? "#3A3A3A" : "#DDDDDD");
            Set(d, "PhoneAccentForegroundBrush", isDark ? "#FFFFFF" : "#FFFFFF");
            Set(d, "PhoneDisabledBrush",        isDark ? "#555555" : "#CCCCCC");
        }

        private static void Set(ResourceDictionary d, string key, string hex)
        {
            byte r = Convert.ToByte(hex.Substring(1, 2), 16);
            byte g = Convert.ToByte(hex.Substring(3, 2), 16);
            byte b = Convert.ToByte(hex.Substring(5, 2), 16);
            var brush = d[key] as SolidColorBrush;
            if (brush != null) brush.Color = Color.FromArgb(0xFF, r, g, b);
        }

        private async void OnSuspending(object sender, Windows.ApplicationModel.SuspendingEventArgs e)
        {
            var deferral = e.SuspendingOperation.GetDeferral();
            try
            {
                await SuspensionManager.SaveAsync();
                await DataManager.SaveAsync();
            }
            finally
            {
                deferral.Complete();
            }
        }

        private void OnResuming(object sender, object e)
        {
        }

        private void OnUnhandledException(object sender, Windows.UI.Xaml.UnhandledExceptionEventArgs e)
        {
            e.Handled = true;
            var ex = e.Exception;
            System.Diagnostics.Debug.WriteLine($"[Unhandled] {ex.GetType().Name}: {ex.Message}\n{ex.StackTrace}");
            // 兜底抢救一次数据（fire-and-forget：崩溃路径上没有可等待的上下文）
            var _ = DataManager.SaveAsync();
            // 尽量让用户知道出了问题，而不是静默吞掉
            TryShowCrashDialog(ex);
        }

        private static bool _crashDialogShowing;
        private static async void TryShowCrashDialog(Exception ex)
        {
            if (_crashDialogShowing || Window.Current?.Content == null) return;
            _crashDialogShowing = true;
            try
            {
                var dlg = new ContentDialog
                {
                    Title = "发生了一个未处理的错误",
                    Content = $"{ex.GetType().Name}: {ex.Message}\n\n数据已尝试自动保存。",
                    // 不用 CloseButtonText：那是 16299+ 的 API，本应用最低支持 10240
                    PrimaryButtonText = "继续",
                };
                await dlg.ShowAsync();
            }
            catch { /* 已有 ContentDialog 打开时 ShowAsync 会抛，忽略 */ }
            finally { _crashDialogShowing = false; }
        }
    }
}
