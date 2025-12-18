using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Monica.Windows.Dialogs;
using Monica.Windows.Models;
using Monica.Windows.Services;
using Monica.Windows.ViewModels;
using System;

namespace Monica.Windows.Views
{
    public sealed partial class SecureNotesPage : Page
    {
        public SecureItemsViewModel ViewModel { get; }
        private readonly ISecurityService _securityService;
        private IServiceScope _scope;

        public SecureNotesPage()
        {
            this.InitializeComponent();
            
            // Create a scope for this page instance
            _scope = ((App)App.Current).Services.CreateScope();
            ViewModel = _scope.ServiceProvider.GetRequiredService<SecureItemsViewModel>();
            _securityService = _scope.ServiceProvider.GetRequiredService<ISecurityService>();
            
            ViewModel.Initialize(ItemType.Note);
            this.Loaded += SecureNotesPage_Loaded;
            this.Unloaded += SecureNotesPage_Unloaded;
        }

        private void SecureNotesPage_Unloaded(object sender, RoutedEventArgs e)
        {
            _scope?.Dispose();
        }

        private async void SecureNotesPage_Loaded(object sender, RoutedEventArgs e)
        {
            LoadingRing.IsActive = true;
            await ViewModel.LoadDataAsync();
            LoadingRing.IsActive = false;
        }

        private async void AddButton_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new ContentDialog
            {
                Title = "添加笔记",
                PrimaryButtonText = "保存",
                CloseButtonText = "取消",
                DefaultButton = ContentDialogButton.Primary,
                XamlRoot = this.XamlRoot
            };

            var stack = new StackPanel { Spacing = 12 };
            var titleBox = new TextBox { Header = "标题", PlaceholderText = "笔记标题" };
            var contentBox = new TextBox { Header = "内容", PlaceholderText = "笔记内容", AcceptsReturn = true, Height = 120, TextWrapping = TextWrapping.Wrap };
            stack.Children.Add(titleBox);
            stack.Children.Add(contentBox);
            var scrollViewer = new ScrollViewer 
            { 
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto, 
                Content = stack 
            };
            dialog.Content = scrollViewer;

            var result = await dialog.ShowAsync();
            if (result == ContentDialogResult.Primary)
            {
                if (string.IsNullOrWhiteSpace(titleBox.Text)) return;

                // For Notes, we store content in ItemData (Encrypted)
                var encryptedContent = _securityService.Encrypt(contentBox.Text);

                var item = new SecureItem
                {
                    Title = titleBox.Text,
                    ItemData = encryptedContent
                };

                await ViewModel.AddItemAsync(item);
            }
        }

        private async void DeleteButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is SecureItem item)
            {
                var dialog = new ContentDialog
                {
                    Title = "删除确认",
                    Content = "确定要删除此笔记吗？",
                    PrimaryButtonText = "删除",
                    CloseButtonText = "取消",
                    XamlRoot = this.XamlRoot
                };
                if (await dialog.ShowAsync() == ContentDialogResult.Primary)
                {
                    await ViewModel.DeleteItemAsync(item);
                }
            }
        }

        private async void ViewButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is SecureItem item)
            {
                // Decrypt and show content
                string content;
                try
                {
                    content = _securityService.Decrypt(item.ItemData);
                }
                catch
                {
                    content = "(无法解密内容)";
                }

                var dialog = new ContentDialog
                {
                    Title = item.Title,
                    CloseButtonText = "关闭",
                    XamlRoot = this.XamlRoot
                };

                var contentStack = new StackPanel { Spacing = 12, MinWidth = 400 };
                
                // Note content display
                var contentText = new TextBlock
                {
                    Text = string.IsNullOrEmpty(content) ? "(空笔记)" : content,
                    TextWrapping = TextWrapping.Wrap,
                    IsTextSelectionEnabled = true,
                    FontSize = 14,
                    LineHeight = 24
                };
                
                // Metadata
                var metaText = new TextBlock
                {
                    Text = $"创建时间: {item.CreatedAt:yyyy-MM-dd HH:mm}",
                    Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Gray),
                    FontSize = 12
                };

                contentStack.Children.Add(contentText);
                contentStack.Children.Add(metaText);

                dialog.Content = new ScrollViewer 
                { 
                    Content = contentStack, 
                    MaxHeight = 400,
                    VerticalScrollBarVisibility = ScrollBarVisibility.Auto
                };

                await dialog.ShowAsync();
            }
        }

        private void Card_PointerEntered(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
        {
            if (sender is Grid grid)
            {
                grid.Scale = new System.Numerics.Vector3(1.01f, 1.01f, 1f);
            }
        }

        private void Card_PointerExited(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
        {
            if (sender is Grid grid)
            {
                grid.Scale = new System.Numerics.Vector3(1f, 1f, 1f);
            }
        }
    }
}
