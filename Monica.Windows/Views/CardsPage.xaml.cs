using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Monica.Windows.Models;
using Monica.Windows.Services;
using Monica.Windows.ViewModels;
using System;
using System.Text.Json;

namespace Monica.Windows.Views
{
    public sealed partial class CardsPage : Page
    {
        public SecureItemsViewModel ViewModel { get; }
        private readonly ISecurityService _securityService;

        public CardsPage()
        {
            this.InitializeComponent();
            ViewModel = ((App)App.Current).Services.GetRequiredService<SecureItemsViewModel>();
            _securityService = ((App)App.Current).Services.GetRequiredService<ISecurityService>();
            
            // Load both Document and BankCard types
            ViewModel.Initialize(ItemType.Document);
            this.Loaded += CardsPage_Loaded;
        }

        private async void CardsPage_Loaded(object sender, RoutedEventArgs e)
        {
            LoadingRing.IsActive = true;
            await ViewModel.LoadDataAsync();
            LoadingRing.IsActive = false;
        }

        private async void AddButton_Click(object sender, RoutedEventArgs e)
        {
            // First, show a dialog to select card type
            var typeDialog = new ContentDialog
            {
                Title = "选择类型",
                CloseButtonText = "取消",
                XamlRoot = this.XamlRoot
            };

            var typeStack = new StackPanel { Spacing = 8 };
            
            var documentBtn = new Button 
            { 
                Content = "证件 (身份证、护照、驾照等)",
                HorizontalAlignment = HorizontalAlignment.Stretch,
                Padding = new Thickness(16, 12, 16, 12)
            };
            
            var bankCardBtn = new Button 
            { 
                Content = "银行卡",
                HorizontalAlignment = HorizontalAlignment.Stretch,
                Padding = new Thickness(16, 12, 16, 12)
            };

            string selectedType = "";
            documentBtn.Click += (s, args) => { selectedType = "document"; typeDialog.Hide(); };
            bankCardBtn.Click += (s, args) => { selectedType = "bankcard"; typeDialog.Hide(); };

            typeStack.Children.Add(documentBtn);
            typeStack.Children.Add(bankCardBtn);
            typeDialog.Content = typeStack;

            await typeDialog.ShowAsync();

            if (selectedType == "document")
            {
                await ShowAddDocumentDialog();
            }
            else if (selectedType == "bankcard")
            {
                await ShowAddBankCardDialog();
            }
        }

        private async System.Threading.Tasks.Task ShowAddDocumentDialog()
        {
            var dialog = new ContentDialog
            {
                Title = "添加证件",
                PrimaryButtonText = "保存",
                CloseButtonText = "取消",
                DefaultButton = ContentDialogButton.Primary,
                XamlRoot = this.XamlRoot
            };

            var stack = new StackPanel { Spacing = 12, MinWidth = 400 };
            
            // Document Type Selector
            var typeCombo = new ComboBox 
            { 
                Header = "证件类型",
                HorizontalAlignment = HorizontalAlignment.Stretch
            };
            typeCombo.Items.Add(new ComboBoxItem { Content = "身份证", Tag = "ID_CARD" });
            typeCombo.Items.Add(new ComboBoxItem { Content = "护照", Tag = "PASSPORT" });
            typeCombo.Items.Add(new ComboBoxItem { Content = "驾驶证", Tag = "DRIVER_LICENSE" });
            typeCombo.Items.Add(new ComboBoxItem { Content = "社保卡", Tag = "SOCIAL_SECURITY" });
            typeCombo.Items.Add(new ComboBoxItem { Content = "其他", Tag = "OTHER" });
            typeCombo.SelectedIndex = 0;
            
            var titleBox = new TextBox { Header = "名称", PlaceholderText = "例如: 我的身份证" };
            var numberBox = new TextBox { Header = "证件号码 *", PlaceholderText = "110101199001011234" };
            var nameBox = new TextBox { Header = "持有人姓名", PlaceholderText = "张三" };
            var issuedDateBox = new TextBox { Header = "签发日期", PlaceholderText = "2020-01-01" };
            var expiryDateBox = new TextBox { Header = "有效期至", PlaceholderText = "2030-01-01 或 长期" };
            var issuedByBox = new TextBox { Header = "签发机关", PlaceholderText = "XX公安局" };
            var notesBox = new TextBox { Header = "备注", PlaceholderText = "可选备注...", AcceptsReturn = true, Height = 60 };
            
            stack.Children.Add(typeCombo);
            stack.Children.Add(titleBox);
            stack.Children.Add(numberBox);
            stack.Children.Add(nameBox);
            stack.Children.Add(issuedDateBox);
            stack.Children.Add(expiryDateBox);
            stack.Children.Add(issuedByBox);
            stack.Children.Add(notesBox);
            
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
                if (string.IsNullOrWhiteSpace(numberBox.Text)) return;

                var selectedType = (typeCombo.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "ID_CARD";
                
                var docData = new DocumentData
                {
                    DocumentNumber = numberBox.Text.Trim(),
                    FullName = nameBox.Text.Trim(),
                    IssuedDate = issuedDateBox.Text.Trim(),
                    ExpiryDate = expiryDateBox.Text.Trim(),
                    IssuedBy = issuedByBox.Text.Trim(),
                    DocumentTypeString = selectedType
                };

                var json = JsonSerializer.Serialize(docData);
                var encryptedData = _securityService.Encrypt(json);

                var title = titleBox.Text.Trim();
                if (string.IsNullOrEmpty(title))
                {
                    title = selectedType switch
                    {
                        "ID_CARD" => "身份证",
                        "PASSPORT" => "护照",
                        "DRIVER_LICENSE" => "驾驶证",
                        "SOCIAL_SECURITY" => "社保卡",
                        _ => "证件"
                    };
                }

                var item = new SecureItem
                {
                    Title = title,
                    Notes = notesBox.Text.Trim(),
                    ItemData = encryptedData,
                    ItemType = ItemType.Document
                };

                await ViewModel.AddItemAsync(item);
            }
        }

        private async System.Threading.Tasks.Task ShowAddBankCardDialog()
        {
            var dialog = new ContentDialog
            {
                Title = "添加银行卡",
                PrimaryButtonText = "保存",
                CloseButtonText = "取消",
                DefaultButton = ContentDialogButton.Primary,
                XamlRoot = this.XamlRoot
            };

            var stack = new StackPanel { Spacing = 12, MinWidth = 400 };
            
            // Card Type Selector
            var cardTypeCombo = new ComboBox 
            { 
                Header = "卡类型",
                HorizontalAlignment = HorizontalAlignment.Stretch
            };
            cardTypeCombo.Items.Add(new ComboBoxItem { Content = "借记卡", Tag = "DEBIT" });
            cardTypeCombo.Items.Add(new ComboBoxItem { Content = "信用卡", Tag = "CREDIT" });
            cardTypeCombo.Items.Add(new ComboBoxItem { Content = "预付卡", Tag = "PREPAID" });
            cardTypeCombo.SelectedIndex = 0;
            
            var titleBox = new TextBox { Header = "名称", PlaceholderText = "例如: 工行储蓄卡" };
            var bankNameBox = new TextBox { Header = "银行名称", PlaceholderText = "例如: 中国工商银行" };
            var cardNumberBox = new TextBox { Header = "卡号 *", PlaceholderText = "6222 0000 0000 0000" };
            var holderNameBox = new TextBox { Header = "持卡人姓名", PlaceholderText = "ZHANG SAN" };
            
            // Expiry Month/Year
            var expiryPanel = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
            var expiryMonthBox = new NumberBox 
            { 
                Header = "有效期月", 
                Value = 1, 
                Minimum = 1, 
                Maximum = 12,
                SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Compact,
                Width = 100
            };
            var expiryYearBox = new NumberBox 
            { 
                Header = "有效期年", 
                Value = DateTime.Now.Year + 3, 
                Minimum = DateTime.Now.Year, 
                Maximum = DateTime.Now.Year + 20,
                SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Compact,
                Width = 120
            };
            expiryPanel.Children.Add(expiryMonthBox);
            expiryPanel.Children.Add(expiryYearBox);
            
            var cvvBox = new PasswordBox { Header = "CVV/安全码", PlaceholderText = "3位数字" };
            var notesBox = new TextBox { Header = "备注", PlaceholderText = "可选备注...", AcceptsReturn = true, Height = 60 };
            
            stack.Children.Add(cardTypeCombo);
            stack.Children.Add(titleBox);
            stack.Children.Add(bankNameBox);
            stack.Children.Add(cardNumberBox);
            stack.Children.Add(holderNameBox);
            stack.Children.Add(expiryPanel);
            stack.Children.Add(cvvBox);
            stack.Children.Add(notesBox);
            
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
                if (string.IsNullOrWhiteSpace(cardNumberBox.Text)) return;

                var selectedType = (cardTypeCombo.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "DEBIT";
                
                var cardData = new BankCardData
                {
                    CardNumber = cardNumberBox.Text.Trim().Replace(" ", ""),
                    CardholderName = holderNameBox.Text.Trim(),
                    ExpiryMonth = ((int)expiryMonthBox.Value).ToString("D2"),
                    ExpiryYear = ((int)expiryYearBox.Value).ToString(),
                    Cvv = cvvBox.Password,
                    BankName = bankNameBox.Text.Trim(),
                    CardTypeString = selectedType
                };

                var json = JsonSerializer.Serialize(cardData);
                var encryptedData = _securityService.Encrypt(json);

                var title = titleBox.Text.Trim();
                if (string.IsNullOrEmpty(title))
                {
                    title = bankNameBox.Text.Trim();
                    if (string.IsNullOrEmpty(title)) title = "银行卡";
                }

                var item = new SecureItem
                {
                    Title = title,
                    Notes = notesBox.Text.Trim(),
                    ItemData = encryptedData,
                    ItemType = ItemType.BankCard
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
                    Content = "确定要删除吗？",
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
    }
}
