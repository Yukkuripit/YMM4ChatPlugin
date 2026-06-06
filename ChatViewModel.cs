using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using System.Windows.Input;
using System.Windows;
using System.Text.Json;
using Microsoft.Win32;
using System.IO;

namespace YMM4ChatPlugin
{
    public class ChatViewModel : INotifyPropertyChanged, IDisposable
    {
        private SupabaseService? _supabase;
        private bool _isHostMode;
        private string _messageText = "";
        private string _connectionInfo = "";
        private string _roomIdInput = "";
        private string _userName = "匿名";
        private string _currentRoomId = "";
        private ObservableCollection<ChatMessage> _messages = new();
        private bool _isDarkMode = false;

        public ObservableCollection<ChatMessage> Messages
        {
            get => _messages;
            set { _messages = value; OnPropertyChanged(); }
        }

        public string MessageText
        {
            get => _messageText;
            set { _messageText = value; OnPropertyChanged(); }
        }

        public string ConnectionInfo
        {
            get => _connectionInfo;
            set { _connectionInfo = value; OnPropertyChanged(); }
        }

        public string RoomIdInput
        {
            get => _roomIdInput;
            set { _roomIdInput = value; OnPropertyChanged(); }
        }

        public string UserName
        {
            get => _userName;
            set { _userName = value; OnPropertyChanged(); }
        }

        public string CurrentRoomId
        {
            get => _currentRoomId;
            set { _currentRoomId = value; OnPropertyChanged(); }
        }

        public bool IsHostMode
        {
            get => _isHostMode;
            set { _isHostMode = value; OnPropertyChanged(); }
        }

        public bool IsDarkMode
        {
            get => _isDarkMode;
            set { _isDarkMode = value; OnPropertyChanged(); OnPropertyChanged(nameof(DarkModeBackground)); OnPropertyChanged(nameof(DarkModeForeground)); }
        }
        public string DarkModeBackground => IsDarkMode ? "#1E1E1E" : "#FFFFFF";
        public string DarkModeForeground => IsDarkMode ? "#FFFFFF" : "#000000";

        public ICommand CreateRoomCommand { get; }
        public ICommand JoinRoomCommand { get; }
        public ICommand SendMessageCommand { get; }
        public ICommand ToggleDarkModeCommand { get; }
        public ICommand ExportHistoryCommand { get; }
        public ICommand ImportHistoryCommand { get; }
        public ICommand CopyRoomIdCommand { get; }

        public ChatViewModel()
        {
            CreateRoomCommand = new RelayCommand(_ => _ = CreateRoomAsync());
            JoinRoomCommand = new RelayCommand(_ => _ = JoinRoomAsync(), _ => !string.IsNullOrWhiteSpace(RoomIdInput));
            SendMessageCommand = new RelayCommand(_ => SendMessage(), _ => !string.IsNullOrWhiteSpace(MessageText) && _supabase?.IsConnected == true);
            ToggleDarkModeCommand = new RelayCommand(_ => IsDarkMode = !IsDarkMode);
            ExportHistoryCommand = new RelayCommand(_ => ExportHistory());
            ImportHistoryCommand = new RelayCommand(_ => ImportHistory());
            CopyRoomIdCommand = new RelayCommand(_ => CopyRoomId(), _ => !string.IsNullOrWhiteSpace(CurrentRoomId));
        }

        private async Task ConnectSupabaseAsync()
        {
            if (_supabase != null) return;
            _supabase = new SupabaseService();
            _supabase.SetCurrentUserName(UserName);
            _supabase.OnConnected += (msg) =>
            {
                Application.Current.Dispatcher.Invoke(() => AddSystemMessage("Supabaseに接続しました"));
            };
            _supabase.OnMessageReceived += (msg) =>
            {
                Application.Current.Dispatcher.Invoke(() => AddRemoteMessage(msg));
            };
            _supabase.OnError += (err) =>
            {
                Application.Current.Dispatcher.Invoke(() => AddSystemMessage($"エラー: {err}"));
            };
            await _supabase.InitializeAsync();
        }

        private async Task CreateRoomAsync()
        {
            await ConnectSupabaseAsync();
            if (_supabase == null) return;

            var roomId = await _supabase.CreateRoomAsync();
            if (!string.IsNullOrEmpty(roomId))
            {
                CurrentRoomId = roomId;
                IsHostMode = true;
                ConnectionInfo = $"ルームを作成しました！\nルームID: {roomId}\nこのIDを他の参加者に共有してください。";
                AddSystemMessage($"ルーム {roomId} を作成しました");

                // 履歴を読み込む
                var history = await _supabase.LoadHistoryAsync();
                Application.Current.Dispatcher.Invoke(() =>
                {
                    Messages.Clear();
                    foreach (var msg in history) Messages.Add(msg);
                });
            }
        }

        private async Task JoinRoomAsync()
        {
            if (string.IsNullOrWhiteSpace(RoomIdInput)) return;

            await ConnectSupabaseAsync();
            if (_supabase == null) return;

            var success = await _supabase.JoinRoomAsync(RoomIdInput.Trim());
            if (success)
            {
                CurrentRoomId = RoomIdInput.Trim();
                IsHostMode = false;
                ConnectionInfo = $"ルーム {CurrentRoomId} に参加しました";
                AddSystemMessage($"ルーム {CurrentRoomId} に参加しました");

                var history = await _supabase.LoadHistoryAsync();
                Application.Current.Dispatcher.Invoke(() =>
                {
                    Messages.Clear();
                    foreach (var msg in history) Messages.Add(msg);
                });
            }
            else
            {
                AddSystemMessage($"ルーム {RoomIdInput} が見つかりません");
            }
        }

        private void SendMessage()
        {
            if (string.IsNullOrWhiteSpace(MessageText) || _supabase == null || !_supabase.IsConnected) return;

            var msg = new ChatMessage
            {
                Type = ChatMessageType.Normal,
                UserName = UserName,
                Text = MessageText,
                Timestamp = DateTime.Now
            };

            // 自分のメッセージをすぐに表示
            AddLocalMessage(msg);

            // Supabaseに送信（fire-and-forget）
#pragma warning disable CS4014
            _supabase.SendMessageAsync(msg);
#pragma warning restore CS4014

            MessageText = string.Empty;
        }

        private void CopyRoomId()
        {
            if (!string.IsNullOrWhiteSpace(CurrentRoomId))
            {
                Clipboard.SetText(CurrentRoomId);
                AddSystemMessage("ルームIDをクリップボードにコピーしました");
            }
        }

        private void ExportHistory()
        {
            try
            {
                var saveDialog = new SaveFileDialog
                {
                    Filter = "JSONファイル (*.json)|*.json",
                    DefaultExt = "json",
                    FileName = $"ChatHistory_{DateTime.Now:yyyyMMdd_HHmmss}.json"
                };
                if (saveDialog.ShowDialog() == true)
                {
                    var json = JsonSerializer.Serialize(Messages, new JsonSerializerOptions { WriteIndented = true });
                    File.WriteAllText(saveDialog.FileName, json);
                    AddSystemMessage($"履歴をエクスポートしました: {saveDialog.FileName}");
                }
            }
            catch (Exception ex) { AddSystemMessage($"エクスポート失敗: {ex.Message}"); }
        }

        private void ImportHistory()
        {
            try
            {
                var openDialog = new OpenFileDialog { Filter = "JSONファイル (*.json)|*.json", DefaultExt = "json" };
                if (openDialog.ShowDialog() == true)
                {
                    var json = File.ReadAllText(openDialog.FileName);
                    var imported = JsonSerializer.Deserialize<ObservableCollection<ChatMessage>>(json);
                    if (imported != null && imported.Count > 0)
                    {
                        var result = MessageBox.Show("現在の履歴をクリアしてインポートしますか？\n「いいえ」の場合は追加します。", "履歴のインポート", MessageBoxButton.YesNoCancel, MessageBoxImage.Question);
                        Application.Current.Dispatcher.Invoke(() =>
                        {
                            if (result == MessageBoxResult.Yes)
                            {
                                Messages.Clear();
                                foreach (var msg in imported) Messages.Add(msg);
                                AddSystemMessage($"履歴をインポートしました（置き換え）");
                            }
                            else if (result == MessageBoxResult.No)
                            {
                                foreach (var msg in imported) Messages.Add(msg);
                                AddSystemMessage($"履歴をインポートしました（追加）");
                            }
                        });
                    }
                }
            }
            catch (Exception ex) { AddSystemMessage($"インポート失敗: {ex.Message}"); }
        }

        private void AddRemoteMessage(ChatMessage msg)
        {
            if (msg.UserName == UserName) return; // 自分のメッセージは無視
            Application.Current.Dispatcher.Invoke(() => Messages.Add(msg));
        }

        private void AddLocalMessage(ChatMessage msg)
        {
            Application.Current.Dispatcher.Invoke(() => Messages.Add(msg));
        }

        private void AddSystemMessage(string text)
        {
            var msg = new ChatMessage { Type = ChatMessageType.System, Text = text, Timestamp = DateTime.Now };
            Application.Current.Dispatcher.Invoke(() => Messages.Add(msg));
        }

        public void Dispose() => _supabase?.Dispose();

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string? name = null) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}