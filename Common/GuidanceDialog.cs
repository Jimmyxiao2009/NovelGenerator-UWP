using System;
using System.Threading.Tasks;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;

namespace novelgen.Common
{
    /// <summary>
    /// 复用的"AI 引导修改"提示词输入对话框。
    /// 返回用户输入的文本；取消或空输入返回 null。
    /// </summary>
    public static class GuidanceDialog
    {
        // UWP 同一时刻只允许一个 ContentDialog；并发 ShowAsync 会抛异常
        private static bool _showing;

        public static async Task<string> ShowAsync(
            string title,
            string placeholder,
            string primaryText = "应用",
            string initialText = "")
        {
            if (_showing) return null;
            _showing = true;
            try
            {
                return await ShowCoreAsync(title, placeholder, primaryText, initialText);
            }
            finally
            {
                _showing = false;
            }
        }

        private static async Task<string> ShowCoreAsync(
            string title,
            string placeholder,
            string primaryText,
            string initialText)
        {
            var box = new TextBox
            {
                AcceptsReturn = true,
                TextWrapping = TextWrapping.Wrap,
                MinHeight = 180,
                MaxHeight = 360,
                PlaceholderText = placeholder ?? "",
                Text = initialText ?? "",
                FontSize = 15,
            };
            var scroll = new ScrollViewer
            {
                Content = box,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                MinHeight = 180,
                MaxHeight = 360,
            };
            // 不用 CloseButtonText / DefaultButton：均为 16299+ API，
            // 在最低支持的 10240（Lumia / W10M）上会 MissingMethodException
            var dlg = new ContentDialog
            {
                Title = title ?? "AI 引导修改",
                Content = scroll,
                PrimaryButtonText = primaryText ?? "应用",
                SecondaryButtonText = "取消",
                FullSizeDesired = false,
            };
            var result = await dlg.ShowAsync();
            if (result == ContentDialogResult.Primary)
            {
                return string.IsNullOrWhiteSpace(box.Text) ? null : box.Text.Trim();
            }
            return null;
        }
    }
}
