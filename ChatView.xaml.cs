using System;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;

namespace YMM4ChatPlugin
{
    public partial class ChatView : UserControl
    {
        private bool _termsClicked = false;
        private bool _privacyClicked = false;
        private bool _eventsRegistered = false;

        public ChatView()
        {
            InitializeComponent();
            this.Loaded += OnLoaded;
            this.Unloaded += OnUnloaded;

            // YMM4 が自動的に DataContext を設定するので、ここでは何もしない
            if (DataContext is ChatViewModel vm)
            {
                vm.PropertyChanged += (_, args) =>
                {
                    if (args.PropertyName == nameof(vm.IsAgreed))
                        UpdateAgreementVisibility(vm.IsAgreed);
                    if (args.PropertyName == nameof(vm.CurrentRoomName))
                    {
                        var window = Window.GetWindow(this);
                        if (window != null)
                            window.Title = $"YMM4チャット - ルーム: {vm.CurrentRoomName}";
                    }
                };
                UpdateAgreementVisibility(vm.IsAgreed);
                var window2 = Window.GetWindow(this);
                if (window2 != null && !string.IsNullOrEmpty(vm.CurrentRoomName))
                    window2.Title = $"YMM4チャット - ルーム: {vm.CurrentRoomName}";
            }
        }

        private void UpdateAgreementVisibility(bool isAgreed)
        {
            AgreementBorder.Visibility = isAgreed ? Visibility.Collapsed : Visibility.Visible;
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            if (_eventsRegistered) return;
            _eventsRegistered = true;

            TermsButton.Click += (s, ev) =>
            {
                _termsClicked = true;
                OpenUrl("https://github.com/Yukkuripit/YMM4ChatPlugin/blob/main/%E5%88%A9%E7%94%A8%E8%A6%8F%E7%B4%84.md");
                UpdateAgreeCheckBox();
            };
            PrivacyButton.Click += (s, ev) =>
            {
                _privacyClicked = true;
                OpenUrl("https://github.com/Yukkuripit/YMM4ChatPlugin/blob/main/%E3%83%97%E3%83%A9%E3%82%A4%E3%83%90%E3%82%B7%E3%83%BC%E3%83%9D%E3%83%AA%E3%82%B7%E3%83%BC.md");
                UpdateAgreeCheckBox();
            };

            AgreeCheckBox.Checked += (s, ev) => SetAgreed(true);
            AgreeCheckBox.Unchecked += (s, ev) => SetAgreed(false);

            this.Focus();
        }

        private void OnUnloaded(object sender, RoutedEventArgs e)
        {
            if (DataContext is ChatViewModel vm)
            {
                vm.Dispose();
                // DataContext = null;  // YMM4が管理するのでやらない
            }
        }

        private void OpenUrl(string url)
        {
            try
            {
                Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
            }
            catch (Exception ex)
            {
                MessageBox.Show($"ブラウザを開けませんでした: {ex.Message}", "エラー", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void UpdateAgreeCheckBox()
        {
            AgreeCheckBox.IsEnabled = _termsClicked && _privacyClicked;
        }

        private void SetAgreed(bool agreed)
        {
            if (DataContext is ChatViewModel vm)
            {
                vm.IsAgreed = agreed;
            }
        }
    }
}