using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace YMM4ChatPlugin
{
    public class SupabaseService : IDisposable
    {
        private readonly HttpClient _httpClient;
        private ClientWebSocket? _webSocket;
        private CancellationTokenSource? _wsCts;
        private string? _currentRoomId;
        private bool _isConnected;
        private string? _currentUserName;

        public event Action<ChatMessage>? OnMessageReceived;
        public event Action<string>? OnConnected;
        public event Action<string>? OnError;

        private const string SupabaseUrl = "https://dcfpbrgatcobrkhncbkh.supabase.co";
        private const string SupabaseKey = "eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9.eyJpc3MiOiJzdXBhYmFzZSIsInJlZiI6ImRjZnBicmdhdGNvYnJraG5jYmtoIiwicm9sZSI6ImFub24iLCJpYXQiOjE3ODA3MTg4NTMsImV4cCI6MjA5NjI5NDg1M30.Zj0sI7x3wlMJk8CeP3Za7sP5sgtsp7CpuA65xSuo4dQ";

        public bool IsConnected => _isConnected;

        public SupabaseService()
        {
            _httpClient = new HttpClient();
            _httpClient.BaseAddress = new Uri(SupabaseUrl);
            _httpClient.DefaultRequestHeaders.Add("apikey", SupabaseKey);
            _httpClient.DefaultRequestHeaders.Add("Authorization", $"Bearer {SupabaseKey}");
            _httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        }

        public void SetCurrentUserName(string userName) => _currentUserName = userName;

        public async Task<bool> InitializeAsync()
        {
            try
            {
                var response = await _httpClient.GetAsync("/rest/v1/rooms?limit=1");
                if (response.IsSuccessStatusCode)
                {
                    _isConnected = true;
                    OnConnected?.Invoke("Supabase接続成功");
                    return true;
                }
                else
                {
                    var error = await response.Content.ReadAsStringAsync();
                    OnError?.Invoke($"接続確認失敗: {error}");
                    return false;
                }
            }
            catch (Exception ex)
            {
                OnError?.Invoke($"初期化エラー: {ex.Message}");
                return false;
            }
        }

        public async Task<string> CreateRoomAsync()
        {
            try
            {
                string roomId = GenerateShortId();
                var room = new { id = roomId, last_active = DateTime.UtcNow };
                var content = new StringContent(JsonSerializer.Serialize(room), Encoding.UTF8, "application/json");
                var response = await _httpClient.PostAsync("/rest/v1/rooms", content);
                if (response.IsSuccessStatusCode)
                {
                    _currentRoomId = roomId;
                    _ = Task.Run(() => ConnectRealtimeAsync(roomId)); // WebSocket接続開始
                    return roomId;
                }
                else
                {
                    var error = await response.Content.ReadAsStringAsync();
                    OnError?.Invoke($"ルーム作成失敗: {error}");
                    return "";
                }
            }
            catch (Exception ex)
            {
                OnError?.Invoke($"ルーム作成失敗: {ex.Message}");
                return "";
            }
        }

        public async Task<bool> JoinRoomAsync(string roomId)
        {
            try
            {
                var response = await _httpClient.GetAsync($"/rest/v1/rooms?id=eq.{roomId}&select=id");
                if (!response.IsSuccessStatusCode) return false;
                var content = await response.Content.ReadAsStringAsync();
                var rooms = JsonSerializer.Deserialize<List<dynamic>>(content);
                if (rooms == null || rooms.Count == 0) return false;

                _currentRoomId = roomId;
                _ = Task.Run(() => ConnectRealtimeAsync(roomId));
                return true;
            }
            catch (Exception ex)
            {
                OnError?.Invoke($"ルーム参加失敗: {ex.Message}");
                return false;
            }
        }

        private async Task ConnectRealtimeAsync(string roomId)
        {
            _wsCts?.Cancel();
            _webSocket?.Dispose();
            _webSocket = new ClientWebSocket();
            _wsCts = new CancellationTokenSource();

            // Supabase Realtime WebSocket URL
            var wsUrl = $"{SupabaseUrl.Replace("https", "wss")}/realtime/v1/websocket?apikey={SupabaseKey}&vsn=1.0.0";
            await _webSocket.ConnectAsync(new Uri(wsUrl), _wsCts.Token);

            // チャンネル参加メッセージ
            var joinMsg = new
            {
                topic = $"realtime:public:messages",
                @event = "phx_join",
                payload = new { headers = new { } },
                @ref = "1"
            };
            var joinJson = JsonSerializer.Serialize(joinMsg);
            await _webSocket.SendAsync(new ArraySegment<byte>(Encoding.UTF8.GetBytes(joinJson)), WebSocketMessageType.Text, true, _wsCts.Token);

            // 受信ループ
            var buffer = new byte[4096];
            while (_webSocket.State == WebSocketState.Open && !_wsCts.Token.IsCancellationRequested)
            {
                var result = await _webSocket.ReceiveAsync(new ArraySegment<byte>(buffer), _wsCts.Token);
                if (result.MessageType == WebSocketMessageType.Text)
                {
                    var msgJson = Encoding.UTF8.GetString(buffer, 0, result.Count);
                    // 簡単なパース: INSERT イベントのみ処理
                    if (msgJson.Contains("\"event\":\"INSERT\"") && msgJson.Contains("\"table\":\"messages\""))
                    {
                        try
                        {
                            using var doc = JsonDocument.Parse(msgJson);
                            var payload = doc.RootElement.GetProperty("payload");
                            var record = payload.GetProperty("record");
                            var chatMsg = new ChatMessage
                            {
                                Id = record.GetProperty("id").ToString(),
                                UserName = record.GetProperty("user_name").ToString(),
                                Text = record.GetProperty("text").ToString(),
                                Type = record.GetProperty("type").ToString() == "JoinLeave" ? ChatMessageType.JoinLeave :
                                       record.GetProperty("type").ToString() == "System" ? ChatMessageType.System : ChatMessageType.Normal,
                                Timestamp = record.GetProperty("created_at").GetDateTime(),
                                ReplyToId = record.TryGetProperty("reply_to_id", out var rid) ? rid.ToString() : null,
                                ReplyPreview = record.TryGetProperty("reply_preview", out var rp) ? rp.ToString() : null
                            };
                            OnMessageReceived?.Invoke(chatMsg);
                        }
                        catch (Exception ex) { OnError?.Invoke($"メッセージ解析エラー: {ex.Message}"); }
                    }
                }
            }
        }

        public async Task SendMessageAsync(ChatMessage message)
        {
            if (string.IsNullOrEmpty(_currentRoomId)) return;
            try
            {
                var record = new
                {
                    room_id = _currentRoomId,
                    user_name = message.UserName,
                    text = message.Text,
                    type = message.Type.ToString(),
                    created_at = DateTime.UtcNow,
                    reply_to_id = message.ReplyToId,
                    reply_preview = message.ReplyPreview
                };
                var content = new StringContent(JsonSerializer.Serialize(record), Encoding.UTF8, "application/json");
                await _httpClient.PostAsync("/rest/v1/messages", content);
            }
            catch (Exception ex)
            {
                OnError?.Invoke($"メッセージ送信失敗: {ex.Message}");
            }
        }

        public async Task<List<ChatMessage>> LoadHistoryAsync(int limit = 100)
        {
            if (string.IsNullOrEmpty(_currentRoomId)) return new List<ChatMessage>();
            try
            {
                var url = $"/rest/v1/messages?room_id=eq.{_currentRoomId}&order=created_at.asc&limit={limit}";
                var response = await _httpClient.GetAsync(url);
                if (!response.IsSuccessStatusCode) return new List<ChatMessage>();
                var json = await response.Content.ReadAsStringAsync();
                var records = JsonSerializer.Deserialize<List<JsonElement>>(json);
                var messages = new List<ChatMessage>();
                if (records != null)
                {
                    foreach (var item in records)
                    {
                        messages.Add(new ChatMessage
                        {
                            Id = item.GetProperty("id").ToString(),
                            UserName = item.GetProperty("user_name").ToString(),
                            Text = item.GetProperty("text").ToString(),
                            Type = item.GetProperty("type").ToString() == "JoinLeave" ? ChatMessageType.JoinLeave :
                                   item.GetProperty("type").ToString() == "System" ? ChatMessageType.System : ChatMessageType.Normal,
                            Timestamp = item.GetProperty("created_at").GetDateTime(),
                            ReplyToId = item.TryGetProperty("reply_to_id", out var rid) ? rid.ToString() : null,
                            ReplyPreview = item.TryGetProperty("reply_preview", out var rp) ? rp.ToString() : null
                        });
                    }
                }
                return messages;
            }
            catch (Exception ex)
            {
                OnError?.Invoke($"履歴読み込み失敗: {ex.Message}");
                return new List<ChatMessage>();
            }
        }

        private static string GenerateShortId()
        {
            var chars = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789";
            var random = new Random();
            var result = new char[6];
            for (int i = 0; i < 6; i++) result[i] = chars[random.Next(chars.Length)];
            return new string(result);
        }

        public void Dispose()
        {
            _wsCts?.Cancel();
            _webSocket?.Dispose();
            _httpClient?.Dispose();
        }
    }
}