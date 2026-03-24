using P2P_Chat.Models;
using P2P_Chat.Net;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace P2P_Chat.Core
{
    public class Chat
    {
        private readonly string _name;
        private readonly IPAddress _localIp;
        private readonly int _udpPort = 50000;
        private readonly int _tcpPort = 50001;

        private readonly UdpClient _udpClient;
        private readonly TcpListener _tcpListener;

        private readonly ConcurrentDictionary<IPAddress, TcpClient> _connections = new();
        private readonly ConcurrentDictionary<IPAddress, byte> _pendingConnections = new();


        private readonly List<Chat_event> _history = new();
        private readonly string _historyFilePath;

        private CancellationTokenSource? _cts;

        public event Action<Chat_event>? OnEvent;
        public event Action<string, IPAddress, string>? OnIncomingMessage;

        public Chat(string name, IPAddress localIp)
        {
            _name = name;
            _localIp = localIp.MapToIPv4();

            _udpClient = new UdpClient(new IPEndPoint(localIp, _udpPort))
            {
                EnableBroadcast = true
            };

            _tcpListener = new TcpListener(new IPEndPoint(localIp, _tcpPort));

            _historyFilePath = Path.Combine(
                AppDomain.CurrentDomain.BaseDirectory,
                $"history_{_name}_{_localIp}.log");

            LoadHistory();
        }

        public IReadOnlyList<Chat_event> History => _history.AsReadOnly();


        private void AddEvent(string text)
        {
            var ev = new Chat_event
            {
                Timestamp = DateTime.Now,
                EventText = text
            };

            _history.Add(ev);
            OnEvent?.Invoke(ev);
            AppendHistoryToFile(ev);
        }

        private void LoadHistory()
        {
            if (!File.Exists(_historyFilePath))
                return;

            try
            {
                using var fs = new FileStream(
                    _historyFilePath,
                    FileMode.Open,
                    FileAccess.Read,
                    FileShare.ReadWrite);

                using var sr = new StreamReader(fs, Encoding.UTF8);
                string? line;
                while ((line = sr.ReadLine()) != null)
                {
                    var parts = line.Split('|', 2);
                    if (parts.Length == 2 &&
                        DateTime.TryParse(parts[0], out var ts))
                    {
                        _history.Add(new Chat_event
                        {
                            Timestamp = ts,
                            EventText = parts[1]
                        });
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[HISTORY] Load error: {ex}");
            }
        }

        private void AppendHistoryToFile(Chat_event ev)
        {
            try
            {
                using var fs = new FileStream(
                    _historyFilePath,
                    FileMode.Append,
                    FileAccess.Write,
                    FileShare.ReadWrite);

                using var sw = new StreamWriter(fs, Encoding.UTF8);
                sw.WriteLine($"{ev.Timestamp:o}|{ev.EventText}");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[HISTORY] Write error: {ex}");
            }
        }


        public void Start()
        {
            _cts = new CancellationTokenSource();
            _tcpListener.Start();

            _ = Task.Run(() => ListenUdpAsync(_cts.Token));
            _ = Task.Run(() => AcceptTcpAsync(_cts.Token));

            BroadcastHello();
            AddEvent("Чат запущен");
        }

        public void Stop()
        {
            try
            {
                _cts?.Cancel();
                _tcpListener.Stop();
                _udpClient.Close();

                foreach (var kv in _connections)
                {
                    try { kv.Value.Close(); } catch { }
                }

                AddEvent("Чат остановлен");
            }
            catch { }
        }

        private void BroadcastHello()
        {
            string msg = $"HELLO|{_name}|{_localIp}";
            byte[] data = Encoding.UTF8.GetBytes(msg);
            var ep = new IPEndPoint(IPAddress.Broadcast, _udpPort);
            _udpClient.Send(data, data.Length, ep);
        }

        private async Task ListenUdpAsync(CancellationToken token)
        {
            while (!token.IsCancellationRequested)
            {
                try
                {
                    var result = await _udpClient.ReceiveAsync(token);
                    string text = Encoding.UTF8.GetString(result.Buffer);

                    if (!text.StartsWith("HELLO|"))
                        continue;

                    var parts = text.Split('|');
                    if (parts.Length < 3)
                        continue;

                    string remoteName = parts[1];
                    if (!IPAddress.TryParse(parts[2], out var remoteIp))
                        continue;

                    if (remoteIp.Equals(_localIp))
                        continue;

                    if (CompareIp(_localIp, remoteIp) > 0)
                    {
                        Debug.WriteLine($"[{_name}] Not initiating TCP to {remoteIp} (our IP is higher)");
                        continue;
                    }
                    int local = _localIp.GetAddressBytes()[3];
                    int remote = remoteIp.GetAddressBytes()[3];

                    int delayMs = Math.Max(1, (remote - local) * 10);

                    await Task.Delay(delayMs, token);

                    await EnsureTcpConnectionAsync(remoteIp, remoteName);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[{_name}] ListenUdpAsync error: {ex}");
                }
            }
        }

        private async Task EnsureTcpConnectionAsync(IPAddress remoteIp, string remoteName)
        {
            if (CompareIp(_localIp, remoteIp) >= 0)
            {
                Debug.WriteLine($"[{_name}] Skipping outbound connect to {remoteIp} (our IP is not lower)");
                return;
            }

            if (_connections.ContainsKey(remoteIp))
                return;

            if (!_pendingConnections.TryAdd(remoteIp, 0))
            {
                Debug.WriteLine($"[{_name}] Outbound connect to {remoteIp} already pending");
                return;
            }

            try
            {
                var client = new TcpClient();
                await client.ConnectAsync(remoteIp, _tcpPort);

                if (_connections.ContainsKey(remoteIp))
                {
                    Debug.WriteLine($"[{_name}] Outbound duplicate to {remoteIp}, closing NEW one");
                    client.Dispose();
                    return;
                }

                if (_connections.TryAdd(remoteIp, client))
                {
                    var stream = client.GetStream();
                    Protocol.SendMessage(stream, Message_tpye.Name, _name);
                    Protocol.SendMessage(stream, Message_tpye.User_connected, $"{_name}|{_localIp}");
                    Protocol.SendMessage(stream, Message_tpye.History_req, "");

                    _ = Task.Run(() => HandleClientAsync(client, remoteIp));
                    AddEvent($"Установлено TCP-соединение с {remoteName} ({remoteIp})");
                }
                else
                {
                    client.Dispose();
                }
            }
            catch
            {
                AddEvent($"Не удалось подключиться к {remoteName} ({remoteIp})");
            }
            finally
            {
                _pendingConnections.TryRemove(remoteIp, out _);
            }
        }


        private async Task AcceptTcpAsync(CancellationToken token)
        {
            while (!token.IsCancellationRequested)
            {
                try
                {
                    var client = await _tcpListener.AcceptTcpClientAsync(token);
                    var remoteIp = ((IPEndPoint)client.Client.RemoteEndPoint!).Address;
                    remoteIp = remoteIp.MapToIPv4();

                    if (CompareIp(_localIp, remoteIp) < 0)
                    {
                        Debug.WriteLine($"[{_name}] Rejecting inbound from {remoteIp} (we should initiate)");
                        client.Dispose();
                        continue;
                    }

                    if (_connections.ContainsKey(remoteIp))
                    {
                        Debug.WriteLine($"[{_name}] Duplicate inbound from {remoteIp}, closing NEW one");
                        client.Dispose();
                        continue;
                    }

                    if (_connections.TryAdd(remoteIp, client))
                    {
                        _ = Task.Run(() => HandleClientAsync(client, remoteIp));
                        AddEvent($"Принято TCP-соединение от {remoteIp}");
                    }
                    else
                    {
                        client.Dispose();
                    }
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[{_name}] AcceptTcpAsync error: {ex}");
                }
            }
        }


        private async Task HandleClientAsync(TcpClient client, IPAddress remoteIp)
        {
            string remoteName = remoteIp.ToString();

            try
            {
                var stream = client.GetStream();

                while (client.Connected)
                {
                    var msg = Protocol.ReadMessage(stream);
                    if (msg == null)
                        break;

                    switch (msg.Value.Type)
                    {
                        case Message_tpye.Name:
                            remoteName = msg.Value.Payload;
                            break;

                        case Message_tpye.Text:
                            AddEvent($"Сообщение от {remoteName} ({remoteIp}): {msg.Value.Payload}");
                            OnIncomingMessage?.Invoke(remoteName, remoteIp, msg.Value.Payload);
                            break;

                        case Message_tpye.User_connected:
                            AddEvent($"Уведомление: подключен {msg.Value.Payload}");
                            break;

                        case Message_tpye.User_disconnected:
                            AddEvent($"Уведомление: отключен {msg.Value.Payload}");
                            break;

                        case Message_tpye.History_req:
                            {
                                var sb = new StringBuilder();
                                foreach (var ev in _history.OrderBy(e => e.Timestamp))
                                {
                                    sb.AppendLine($"{ev.Timestamp:o}|{ev.EventText}");
                                }

                                Protocol.SendMessage(stream, Message_tpye.History_res, sb.ToString());
                                break;
                            }

                        case Message_tpye.History_res:
                            {
                                try
                                {
                                    var received = new List<Chat_event>();

                                    using var sr = new StringReader(msg.Value.Payload);
                                    string? line;
                                    while ((line = sr.ReadLine()) != null)
                                    {
                                        var parts = line.Split('|', 2);
                                        if (parts.Length == 2 &&
                                            DateTime.TryParse(parts[0], out var ts))
                                        {
                                            received.Add(new Chat_event
                                            {
                                                Timestamp = ts,
                                                EventText = parts[1]
                                            });
                                        }
                                    }

                                    var merged = _history
                                        .Concat(received)
                                        .GroupBy(ev => $"{ev.Timestamp:o}|{ev.EventText}")
                                        .Select(g => g.First())
                                        .OrderBy(ev => ev.Timestamp)
                                        .ToList();

                                    _history.Clear();
                                    _history.AddRange(merged);

                                    foreach (var ev in received)
                                        OnEvent?.Invoke(ev);

                                    AddEvent($"История получена от {remoteName} ({remoteIp})");
                                }
                                catch (Exception ex)
                                {
                                    Debug.WriteLine($"[HISTORY] History_res error: {ex}");
                                }

                                break;
                            }
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[{_name}] HandleClientAsync error with {remoteIp}: {ex}");
            }
            finally
            {
                client.Close();
                _connections.TryRemove(remoteIp, out _);
                AddEvent($"Соединение с {remoteName} ({remoteIp}) закрыто");
            }
        }


        public void SendTextMessage(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return;

            AddEvent($"Я: {text}");
            Debug.WriteLine($"[{_name}] SendTextMessage: connections={_connections.Count}");

            foreach (var kv in _connections)
            {
                var client = kv.Value;
                if (!client.Connected)
                    continue;

                try
                {
                    var stream = client.GetStream();
                    Protocol.SendMessage(stream, Message_tpye.Text, text);
                    Debug.WriteLine($"[{_name}] → sent to {kv.Key}");
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[{_name}] SendTextMessage error to {kv.Key}: {ex}");
                }
            }
        }


        private static int CompareIp(IPAddress a, IPAddress b)
        {
            var ab = a.GetAddressBytes();
            var bb = b.GetAddressBytes();

            int len = Math.Min(ab.Length, bb.Length);
            for (int i = 0; i < len; i++)
            {
                int diff = ab[i].CompareTo(bb[i]);
                if (diff != 0)
                    return diff;
            }

            return ab.Length.CompareTo(bb.Length);
        }
    }
}
