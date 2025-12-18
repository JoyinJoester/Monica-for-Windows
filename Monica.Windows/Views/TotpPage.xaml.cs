using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Monica.Windows.Models;
using Monica.Windows.Services;
using Monica.Windows.ViewModels;
using System;
using System.Collections.Generic;
using Windows.ApplicationModel.DataTransfer;

namespace Monica.Windows.Views
{
    public sealed partial class TotpPage : Page
    {
        public SecureItemsViewModel ViewModel { get; }
        private readonly ISecurityService _securityService;
        private DispatcherTimer _timer;
        
        // Track UI elements for per-item updates
        private readonly Dictionary<long, (TextBlock code, TextBlock time, ProgressRing progress)> _itemControls = new();

        public TotpPage()
        {
            this.InitializeComponent();
            ViewModel = ((App)App.Current).Services.GetRequiredService<SecureItemsViewModel>();
            _securityService = ((App)App.Current).Services.GetRequiredService<ISecurityService>();
            
            ViewModel.Initialize(ItemType.Totp);
            this.Loaded += TotpPage_Loaded;
            this.Unloaded += TotpPage_Unloaded;

            _timer = new DispatcherTimer();
            _timer.Interval = TimeSpan.FromSeconds(1);
            _timer.Tick += Timer_Tick;
        }

        private async void TotpPage_Loaded(object sender, RoutedEventArgs e)
        {
            LoadingRing.IsActive = true;
            await ViewModel.LoadDataAsync();
            LoadingRing.IsActive = false;
            
            _timer.Start();
        }

        private void TotpPage_Unloaded(object sender, RoutedEventArgs e)
        {
            _timer.Stop();
            _itemControls.Clear();
        }

        private void Timer_Tick(object sender, object e)
        {
            // Traverse visual tree to find and update all progress rings directly
            // This avoids issues with virtualization and control registration
            FindChildren<ProgressRing>(TotpListView, pr =>
            {
                if (pr.Tag is SecureItem item)
                {
                    UpdateItemProgress(pr, item);
                }
            });
            
            FindChildren<TextBlock>(TotpListView, tb =>
            {
                if (tb.Tag is SecureItem item)
                {
                    // Determine if this is code or time by checking current content format
                    if (tb.FontFamily?.Source == "Consolas" || tb.FontSize >= 24)
                    {
                        UpdateItemCode(tb, item);
                    }
                    else if (tb.Text?.EndsWith("s") == true || tb.Text == "")
                    {
                        UpdateItemTime(tb, item);
                    }
                }
            });
        }

        private void CodeText_Loaded(object sender, RoutedEventArgs e)
        {
            if (sender is TextBlock tb && tb.Tag is SecureItem item)
            {
                RegisterOrUpdateControl(item.Id, code: tb);
                UpdateItemCode(tb, item);
            }
        }

        private void TimeRemaining_Loaded(object sender, RoutedEventArgs e)
        {
            if (sender is TextBlock tb && tb.Tag is SecureItem item)
            {
                RegisterOrUpdateControl(item.Id, time: tb);
                UpdateItemTime(tb, item);
            }
        }

        private void ItemProgress_Loaded(object sender, RoutedEventArgs e)
        {
            if (sender is ProgressRing pr && pr.Tag is SecureItem item)
            {
                RegisterOrUpdateControl(item.Id, progress: pr);
                UpdateItemProgress(pr, item);
            }
        }

        private void RegisterOrUpdateControl(long itemId, TextBlock? code = null, TextBlock? time = null, ProgressRing? progress = null)
        {
            if (!_itemControls.ContainsKey(itemId))
            {
                _itemControls[itemId] = (null!, null!, null!);
            }

            var current = _itemControls[itemId];
            _itemControls[itemId] = (
                code ?? current.code,
                time ?? current.time,
                progress ?? current.progress
            );
        }

        private void UpdateItemDisplay(SecureItem item, TextBlock? codeBlock, TextBlock? timeBlock, ProgressRing? progressRing)
        {
            if (codeBlock != null) UpdateItemCode(codeBlock, item);
            if (timeBlock != null) UpdateItemTime(timeBlock, item);
            if (progressRing != null) UpdateItemProgress(progressRing, item);
        }

        private TotpData? GetTotpData(SecureItem item)
        {
            try
            {
                var decrypted = _securityService.Decrypt(item.ItemData);
                if (string.IsNullOrEmpty(decrypted)) return null;

                try
                {
                    return System.Text.Json.JsonSerializer.Deserialize<TotpData>(decrypted);
                }
                catch
                {
                    // Legacy format: just the secret
                    return new TotpData { Secret = decrypted, Period = 30, Digits = 6, OtpType = "TOTP" };
                }
            }
            catch
            {
                return null;
            }
        }

        private void UpdateItemCode(TextBlock tb, SecureItem item)
        {
            try
            {
                var data = GetTotpData(item);
                if (data != null && !string.IsNullOrEmpty(data.Secret))
                {
                    tb.Text = TotpHelper.GenerateCode(data.Secret, data.Period, data.Digits, data.OtpType);
                }
                else
                {
                    tb.Text = "------";
                }
            }
            catch { tb.Text = "Error"; }
        }

        private void UpdateItemTime(TextBlock tb, SecureItem item)
        {
            try
            {
                var data = GetTotpData(item);
                if (data != null && data.OtpType != "HOTP")
                {
                    var period = data.Period > 0 ? data.Period : 30;
                    var remaining = TotpHelper.GetRemainingSeconds(period);
                    tb.Text = $"{remaining}s";
                }
                else
                {
                    tb.Text = ""; // HOTP doesn't have time
                }
            }
            catch { tb.Text = ""; }
        }

        private void UpdateItemProgress(ProgressRing pr, SecureItem item)
        {
            try
            {
                var data = GetTotpData(item);
                if (data != null && data.OtpType != "HOTP")
                {
                    var period = data.Period > 0 ? data.Period : 30;
                    var remaining = TotpHelper.GetRemainingSeconds(period);
                    pr.Value = (remaining * 100.0) / period;
                    pr.IsIndeterminate = false;
                }
                else
                {
                    pr.Value = 100;
                    pr.IsIndeterminate = false;
                }
            }
            catch { pr.Value = 0; }
        }

        private static void FindChildren<T>(DependencyObject parent, Action<T> action) where T : DependencyObject
        {
            int count = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChildrenCount(parent);
            for (int i = 0; i < count; i++)
            {
                var child = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChild(parent, i);
                if (child is T typed) action(typed);
                FindChildren(child, action);
            }
        }

        private async void AddButton_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new ContentDialog
            {
                Title = "添加验证器",
                PrimaryButtonText = "保存",
                CloseButtonText = "取消",
                DefaultButton = ContentDialogButton.Primary,
                XamlRoot = this.XamlRoot
            };

            var stack = new StackPanel { Spacing = 12, MinWidth = 400 };
            
            // Basic fields
            var titleBox = new TextBox { Header = "名称 *", PlaceholderText = "例如: Google" };
            var secretBox = new TextBox { Header = "密钥 (Base32) *", PlaceholderText = "JBSWY3DPEHPK3PXP" };
            var issuerBox = new TextBox { Header = "发行者", PlaceholderText = "例如: Google" };
            var accountBox = new TextBox { Header = "账户", PlaceholderText = "user@example.com" };
            var notesBox = new TextBox { Header = "备注", PlaceholderText = "可选备注...", AcceptsReturn = true, Height = 60 };
            
            stack.Children.Add(titleBox);
            stack.Children.Add(secretBox);
            stack.Children.Add(issuerBox);
            stack.Children.Add(accountBox);
            stack.Children.Add(notesBox);

            // Advanced settings expander
            var advancedExpander = new Expander
            {
                Header = "高级选项",
                HorizontalAlignment = HorizontalAlignment.Stretch,
                HorizontalContentAlignment = HorizontalAlignment.Stretch
            };

            var advancedStack = new StackPanel { Spacing = 12 };

            // OTP Type selector
            var otpTypeCombo = new ComboBox { Header = "OTP 类型", HorizontalAlignment = HorizontalAlignment.Stretch };
            otpTypeCombo.Items.Add(new ComboBoxItem { Content = "TOTP (基于时间)", Tag = "TOTP" });
            otpTypeCombo.Items.Add(new ComboBoxItem { Content = "HOTP (基于计数器)", Tag = "HOTP" });
            otpTypeCombo.Items.Add(new ComboBoxItem { Content = "Steam Guard", Tag = "STEAM" });
            otpTypeCombo.Items.Add(new ComboBoxItem { Content = "Yandex", Tag = "YANDEX" });
            otpTypeCombo.Items.Add(new ComboBoxItem { Content = "mOTP (移动OTP)", Tag = "MOTP" });
            otpTypeCombo.SelectedIndex = 0;

            // Period
            var periodBox = new NumberBox 
            { 
                Header = "时间周期 (秒)", 
                Value = 30, 
                Minimum = 10, 
                Maximum = 120,
                SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Compact
            };

            // Digits
            var digitsBox = new NumberBox 
            { 
                Header = "验证码位数", 
                Value = 6, 
                Minimum = 5, 
                Maximum = 8,
                SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Compact
            };

            // Counter (for HOTP)
            var counterBox = new NumberBox 
            { 
                Header = "初始计数器 (HOTP)", 
                Value = 0, 
                Minimum = 0,
                Visibility = Visibility.Collapsed,
                SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Compact
            };

            // PIN (for mOTP)
            var pinBox = new PasswordBox 
            { 
                Header = "PIN 码 (mOTP)", 
                PlaceholderText = "4位数字",
                Visibility = Visibility.Collapsed
            };

            // Show/hide fields based on OTP type
            otpTypeCombo.SelectionChanged += (s, args) =>
            {
                var selected = (otpTypeCombo.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "TOTP";
                counterBox.Visibility = selected == "HOTP" ? Visibility.Visible : Visibility.Collapsed;
                pinBox.Visibility = selected == "MOTP" ? Visibility.Visible : Visibility.Collapsed;
                periodBox.Visibility = selected == "HOTP" ? Visibility.Collapsed : Visibility.Visible;
                
                // Steam uses 5 digits, Yandex uses 8 digits
                if (selected == "STEAM")
                {
                    digitsBox.Value = 5;
                    digitsBox.IsEnabled = false;
                }
                else if (selected == "YANDEX")
                {
                    digitsBox.Value = 8;
                    digitsBox.IsEnabled = false;
                }
                else
                {
                    digitsBox.IsEnabled = true;
                    if (digitsBox.Value == 5 || digitsBox.Value == 8) digitsBox.Value = 6;
                }
            };

            advancedStack.Children.Add(otpTypeCombo);
            advancedStack.Children.Add(periodBox);
            advancedStack.Children.Add(digitsBox);
            advancedStack.Children.Add(counterBox);
            advancedStack.Children.Add(pinBox);

            advancedExpander.Content = advancedStack;
            stack.Children.Add(advancedExpander);

            var scrollViewer = new ScrollViewer 
            { 
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto, 
                Content = stack,
                MaxHeight = 500
            };
            dialog.Content = scrollViewer;

            var result = await dialog.ShowAsync();
            if (result == ContentDialogResult.Primary)
            {
                if (string.IsNullOrWhiteSpace(titleBox.Text) || string.IsNullOrWhiteSpace(secretBox.Text)) return;

                var selectedOtpType = (otpTypeCombo.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "TOTP";

                var totpData = new TotpData
                {
                    Secret = secretBox.Text.Trim().ToUpperInvariant(),
                    Issuer = issuerBox.Text.Trim(),
                    AccountName = accountBox.Text.Trim(),
                    Period = (int)periodBox.Value,
                    Digits = (int)digitsBox.Value,
                    Algorithm = "SHA1",
                    OtpType = selectedOtpType,
                    Counter = (long)counterBox.Value,
                    Pin = pinBox.Password
                };

                var json = System.Text.Json.JsonSerializer.Serialize(totpData);
                var encryptedData = _securityService.Encrypt(json);

                var item = new SecureItem
                {
                    Title = titleBox.Text.Trim(),
                    Notes = notesBox.Text.Trim(),
                    ItemData = encryptedData,
                    ItemType = ItemType.Totp
                };

                await ViewModel.AddItemAsync(item);
            }
        }
        
        private void CopyButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is SecureItem item)
            {
                try
                {
                    var data = GetTotpData(item);
                    if (data != null && !string.IsNullOrEmpty(data.Secret))
                    {
                        var code = TotpHelper.GenerateCode(data.Secret, data.Period, data.Digits, data.OtpType);
                        var pkg = new DataPackage();
                        pkg.SetText(code);
                        Clipboard.SetContent(pkg);
                    }
                }
                catch {}
            }
        }

        private async void DeleteButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is SecureItem item)
            {
                var dialog = new ContentDialog
                {
                    Title = "删除确认",
                    Content = "确定要删除此验证器吗？",
                    PrimaryButtonText = "删除",
                    CloseButtonText = "取消",
                    XamlRoot = this.XamlRoot
                };
                if (await dialog.ShowAsync() == ContentDialogResult.Primary)
                {
                    _itemControls.Remove(item.Id);
                    await ViewModel.DeleteItemAsync(item);
                }
            }
        }
    }
}
