using System;
using System.Threading.Tasks;
using Windows.UI;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Media;
using Windows.UI.Xaml.Navigation;
using novelgen.AI;
using novelgen.Core;
using novelgen.Models;

namespace novelgen.Pages.Settings
{
    public sealed partial class ApiProfileEditPage : Page
    {
        private ApiProfile _profile;
        private bool _isNew;

        public ApiProfileEditPage()
        {
            this.InitializeComponent();
            // 模型名/上下文长度改动时实时刷新「自动检测」提示
            ModelBox.TextChanged += (s, ev) => UpdateContextHint();
            ContextBox.TextChanged += (s, ev) => UpdateContextHint();
        }

        protected override void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);
            var incoming = e.Parameter as ApiProfile ?? new ApiProfile();
            _isNew = DataManager.FindApiProfile(incoming.Id) == null;
            // 在编辑副本上工作：测试/设为默认/输入前的 SyncFromUI 不应污染列表里的持久实例，
            // 必须等用户点「保存/设为默认」才落盘（按 Id 替换）。复制全部字段，避免漏字段被默认值覆盖。
            _profile = Clone(incoming);

            BindUI();
        }

        private static ApiProfile Clone(ApiProfile src)
        {
            return new ApiProfile
            {
                Id = src.Id,
                Name = src.Name,
                Url = src.Url,
                ApiKey = src.ApiKey,
                Model = src.Model,
                ProviderType = src.ProviderType,
                MaxContextTokens = src.MaxContextTokens,
                MaxOutputTokens = src.MaxOutputTokens,
                CreatedAt = src.CreatedAt,
            };
        }

        private void BindUI()
        {
            HeaderText.Text = _isNew ? "添加 API" : "编辑 API";
            NameBox.Text = _profile.Name ?? "";
            UrlBox.Text = _profile.Url ?? "";
            KeyBox.Password = _profile.ApiKey ?? "";
            ModelBox.Text = _profile.Model ?? "";
            ContextBox.Text = _profile.MaxContextTokens > 0 ? _profile.MaxContextTokens.ToString() : "0";
            SetProvider(_profile.ProviderType ?? "openai");
            UpdateContextHint();
        }

        private void SetProvider(string provider)
        {
            _suppressProviderChange = true;
            try
            {
                for (int i = 0; i < ProviderBox.Items.Count; i++)
                {
                    var item = ProviderBox.Items[i] as ComboBoxItem;
                    if (item != null && (item.Tag as string ?? "") == provider)
                    {
                        ProviderBox.SelectedIndex = i;
                        return;
                    }
                }
                ProviderBox.SelectedIndex = 0;
            }
            finally
            {
                _suppressProviderChange = false;
            }
        }

        private string GetProvider()
        {
            var item = ProviderBox.SelectedItem as ComboBoxItem;
            return item?.Tag as string ?? "openai";
        }

        private bool _suppressProviderChange;

        private void OnProviderChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_suppressProviderChange) return;
            if (UrlBox == null) return;   // XAML 还没初始化完
            var p = GetProvider();
            if (p == "claude")
            {
                if (string.IsNullOrEmpty(UrlBox.Text) || UrlBox.Text.Contains("openai.com") || UrlBox.Text.Contains("deepseek"))
                {
                    UrlBox.Text = "https://api.anthropic.com/v1/messages";
                }
            }
            else if (p == "openai")
            {
                if (string.IsNullOrEmpty(UrlBox.Text) || UrlBox.Text.Contains("anthropic"))
                {
                    UrlBox.Text = "https://api.deepseek.com/v1/chat/completions";
                }
            }
            else if (p == "plaa")
            {
                if (string.IsNullOrEmpty(UrlBox.Text))
                {
                    UrlBox.Text = "https://plaa.example.com/v1/chat/completions";
                }
            }
        }

        private void UpdateContextHint()
        {
            int v;
            if (int.TryParse(ContextBox.Text, out v) && v > 0)
            {
                ContextHint.Text = "当前：" + v.ToString() + " tokens";
            }
            else
            {
                var auto = ModelContextLimits.GetLimit(ModelBox.Text);
                ContextHint.Text = "自动检测（" + ModelBox.Text + "）：" + auto + " tokens";
            }
        }

        private void SyncFromUI()
        {
            _profile.Name = NameBox.Text?.Trim() ?? "未命名";
            _profile.ProviderType = GetProvider();
            _profile.Url = UrlBox.Text?.Trim() ?? "";
            _profile.ApiKey = KeyBox.Password ?? "";
            _profile.Model = ModelBox.Text?.Trim() ?? "";
            int v;
            _profile.MaxContextTokens = int.TryParse(ContextBox.Text, out v) ? v : 0;
        }

        private async void OnTestClick(object sender, RoutedEventArgs e)
        {
            SyncFromUI();
            if (string.IsNullOrEmpty(_profile.ApiKey) || string.IsNullOrEmpty(_profile.Url))
            {
                ShowTestResult(false, "未填写完整", "请先填写 URL 和 API Key。", 0);
                return;
            }
            TestBtn.IsEnabled = false;
            TestProgress.Visibility = Visibility.Visible;
            ShowTestResult(false, "测试中...", "正在发送 ping 请求。", 0);

            try
            {
                var client = new ApiClient(_profile);
                var result = await client.TestConnectionAsync();
                ShowTestResult(result.Ok, result.Message, result.Detail, result.ElapsedMs);
            }
            catch (Exception ex)
            {
                ShowTestResult(false, ex.GetType().Name, ex.Message, 0);
            }
            finally
            {
                TestBtn.IsEnabled = true;
                TestProgress.Visibility = Visibility.Collapsed;
            }
        }

        private void ShowTestResult(bool ok, string message, string detail, int elapsed)
        {
            TestResult.Text = (ok ? "✓ " : "✗ ") + message + (elapsed > 0 ? " (" + elapsed + "ms)" : "")
                + (string.IsNullOrEmpty(detail) ? "" : "\n" + detail);
            TestResult.Foreground = new SolidColorBrush(ok ? Color.FromArgb(255, 0x2E, 0x7D, 0x32) : Color.FromArgb(255, 0xC6, 0x28, 0x28));
        }

        private async void OnSaveClick(object sender, RoutedEventArgs e)
        {
            SyncFromUI();
            if (string.IsNullOrEmpty(_profile.Name))
            {
                TestResult.Text = "✗ 请填写名称";
                TestResult.Foreground = new SolidColorBrush(Colors.Red);
                return;
            }
            await DataManager.SaveApiProfileAsync(_profile);
            if (Frame.CanGoBack) Frame.GoBack();
        }

        private async void OnDeleteClick(object sender, RoutedEventArgs e)
        {
            var dlg = new Windows.UI.Popups.MessageDialog("确定要删除这个 API 配置吗？");
            dlg.Commands.Add(new Windows.UI.Popups.UICommand("删除", null, "yes"));
            dlg.Commands.Add(new Windows.UI.Popups.UICommand("取消", null, "no"));
            dlg.DefaultCommandIndex = 1;
            var res = await dlg.ShowAsync();
            if (res.Id as string != "yes") return;
            await DataManager.DeleteApiProfileAsync(_profile.Id);
            if (AppSettings.DefaultApiProfileId == _profile.Id) AppSettings.DefaultApiProfileId = "";
            // 两处默认值保持同步
            if (DataManager.Data.DefaultApiProfileId == _profile.Id)
            {
                DataManager.Data.DefaultApiProfileId = "";
                await DataManager.SaveAsync();
            }
            if (Frame.CanGoBack) Frame.GoBack();
        }

        private async void OnSetDefaultClick(object sender, RoutedEventArgs e)
        {
            SyncFromUI();
            // 先保存配置本身，避免默认 Id 指向一个从未保存过的配置（用户直接返回时悬空）
            await DataManager.SaveApiProfileAsync(_profile);
            _isNew = false;
            AppSettings.DefaultApiProfileId = _profile.Id;
            DataManager.Data.DefaultApiProfileId = _profile.Id;
            await DataManager.SaveAsync();
            TestResult.Text = "✓ 已设为默认";
            TestResult.Foreground = new SolidColorBrush(Color.FromArgb(255, 0x2E, 0x7D, 0x32));
        }

        private void OnBackClick(object sender, RoutedEventArgs e)
        {
            if (Frame.CanGoBack) Frame.GoBack();
        }
    }
}
