using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Monica.Windows.Data;
using Monica.Windows.Dialogs;
using Monica.Windows.Services;
using System;
using System.Reflection;
using System.Threading.Tasks;
using Windows.ApplicationModel;


namespace Monica.Windows.ViewModels
{
    public partial class SettingsViewModel : ObservableObject
    {
        private readonly ISecurityService _securityService;

        [ObservableProperty]
        private string _appVersion;

        public SettingsViewModel(ISecurityService securityService)
        {
            _securityService = securityService;
            AppVersion = GetAppVersion();
        }

        private string GetAppVersion()
        {
            try
            {
                var package = Package.Current;
                var version = package.Id.Version;
                return $"{version.Major}.{version.Minor}.{version.Build}.{version.Revision}";
            }
            catch
            {
                return "1.0.0 (Dev)";
            }
        }

        [RelayCommand]
        private async Task ChangeMasterPassword()
        {
            // TODO: Implement Change Password Dialog
            // For now, simple alert
            var dialog = new ContentDialog
            {
                Title = "功能开发中",
                Content = "修改主密码功能即将上线。",
                CloseButtonText = "确定",
                XamlRoot = App.MainWindow.Content.XamlRoot
            };
            await dialog.ShowAsync();
        }

        [RelayCommand]
        private async Task ClearDatabase()
        {
            // Create a dialog with selection options
            var stack = new StackPanel { Spacing = 16 };
            
            var radioAll = new RadioButton { Content = "清空所有数据 (密码、2FA、笔记、卡片)", GroupName = "ClearOption", IsChecked = true };
            var radioPasswords = new RadioButton { Content = "仅清空密码", GroupName = "ClearOption" };
            var radioSecure = new RadioButton { Content = "仅清空安全项目 (2FA、笔记、卡片)", GroupName = "ClearOption" };
            
            var confirmTextBox = new TextBox 
            { 
                Header = "请输入 \"我确认清空全部数据\" 以确认",
                PlaceholderText = "我确认清空全部数据"
            };
            
            stack.Children.Add(new TextBlock { Text = "选择要清空的数据类型:", TextWrapping = TextWrapping.Wrap });
            stack.Children.Add(radioAll);
            stack.Children.Add(radioPasswords);
            stack.Children.Add(radioSecure);
            stack.Children.Add(new TextBlock 
            { 
                Text = "⚠️ 此操作无法撤销！请确保已备份重要数据。", 
                Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Orange),
                FontSize = 12,
                TextWrapping = TextWrapping.Wrap
            });
            stack.Children.Add(confirmTextBox);

            var dialog = new ContentDialog
            {
                Title = "清空数据",
                Content = stack,
                PrimaryButtonText = "确认删除",
                CloseButtonText = "取消",
                DefaultButton = ContentDialogButton.Close,
                XamlRoot = App.MainWindow.Content.XamlRoot
            };

            var result = await dialog.ShowAsync();
            if (result == ContentDialogResult.Primary)
            {
                // Validate confirmation text
                if (confirmTextBox.Text != "我确认清空全部数据")
                {
                    await ShowMessageAsync("验证失败", "请输入正确的确认文字");
                    return;
                }
                
                try
                {
                    var dbContext = ((App)App.Current).Services.GetRequiredService<AppDbContext>();
                    
                    if (radioAll.IsChecked == true)
                    {
                        // Clear all data
                        dbContext.PasswordEntries.RemoveRange(dbContext.PasswordEntries);
                        dbContext.SecureItems.RemoveRange(dbContext.SecureItems);
                        await dbContext.SaveChangesAsync();
                        
                        // Also clear security config and restart
                        var configPath = System.IO.Path.Combine(
                            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), 
                            "Monica", "security.json");
                        if (System.IO.File.Exists(configPath))
                        {
                            System.IO.File.Delete(configPath);
                        }
                        
                        await ShowMessageAsync("成功", "所有数据已清空。应用将重启。");
                        Application.Current.Exit();
                    }
                    else if (radioPasswords.IsChecked == true)
                    {
                        // Clear only passwords
                        dbContext.PasswordEntries.RemoveRange(dbContext.PasswordEntries);
                        await dbContext.SaveChangesAsync();
                        await ShowMessageAsync("成功", $"已清空所有密码条目。");
                    }
                    else if (radioSecure.IsChecked == true)
                    {
                        // Clear only secure items (TOTP, notes, cards)
                        dbContext.SecureItems.RemoveRange(dbContext.SecureItems);
                        await dbContext.SaveChangesAsync();
                        await ShowMessageAsync("成功", "已清空所有安全项目 (2FA、笔记、卡片)。");
                    }
                }
                catch (Exception ex)
                {
                    await ShowMessageAsync("错误", $"清空数据失败: {ex.Message}");
                }
            }
        }

        private async Task ShowMessageAsync(string title, string message)
        {
            var msgDialog = new ContentDialog
            {
                Title = title,
                Content = message,
                CloseButtonText = "确定",
                XamlRoot = App.MainWindow.Content.XamlRoot
            };
            await msgDialog.ShowAsync();
        }

        [RelayCommand]
        private async Task OpenGitHub()
        {
            await global::Windows.System.Launcher.LaunchUriAsync(new Uri("https://github.com/joyins/Monica"));
        }
    }
}
