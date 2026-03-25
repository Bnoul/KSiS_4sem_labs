using System;
using System.Collections.Generic;
using System.Net.Sockets;
using System.Text;

namespace laba_3.Net
{
    class Client_backend
    {
        private TcpClient? _client;
        private bool _running;

        public Action<string>? Log;
        public Action? OnDisconnect;

        public async Task ConnectAsync(string ip, int port)
        {
            if (_running)
            {
                Log?.Invoke("Уже подключено");
                return;
            }

            try
            {
                _client = new TcpClient();
                await _client.ConnectAsync(ip, port);
            }
            catch (Exception ex)
            {
                Log?.Invoke($"Ошибка подключения: {ex.Message}");
                return;
            }

            _running = true;
            Log?.Invoke($"Подключено к {ip}:{port}");

            _ = ReceiveLoop();
        }

        public void Disconnect()
        {
            _running = false;

            try { _client?.Close(); } catch { }

            Log?.Invoke("Отключено");
            OnDisconnect?.Invoke();
        }

        private async Task ReceiveLoop()
        {
            var buffer = new byte[4096];

            try
            {
                while (_running)
                {
                    int read = await _client!.GetStream().ReadAsync(buffer);
                    if (read == 0)
                        break;

                    string msg = Encoding.UTF8.GetString(buffer, 0, read);
                    Log?.Invoke($"[СЕРВЕР] {msg}");
                }
            }
            catch { }

            _running = false;
            OnDisconnect?.Invoke();
        }

        public async Task SendAsync(string msg)
        {
            if (!_running || _client == null)
            {
                Log?.Invoke("Нет подключения");
                return;
            }

            try
            {
                byte[] data = Encoding.UTF8.GetBytes(msg);
                await _client.GetStream().WriteAsync(data);
            }
            catch (Exception ex)
            {
                Log?.Invoke($"Ошибка отправки: {ex.Message}");
                Disconnect();
            }
        }
    }
}
