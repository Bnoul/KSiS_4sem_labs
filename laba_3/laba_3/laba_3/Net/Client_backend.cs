using System;
using System.Net;
using System.Net.Sockets;
using System.Text;

namespace laba_3.Net
{
    class Client_backend
    {
        private Socket? _socket;
        private bool _running;

        public Action<string>? Log;
        public Action? OnDisconnect;

        public async Task ConnectAsync(string ip, int port, string localIp)
        {
            if (_running)
            {
                Log?.Invoke("Уже подключено");
                return;
            }

            try
            {
                _socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);

                var localEndPoint = new IPEndPoint(IPAddress.Parse(localIp), 0);
                _socket.Bind(localEndPoint);

                var remoteEndPoint = new IPEndPoint(IPAddress.Parse(ip), port);
                await _socket.ConnectAsync(remoteEndPoint);
            }
            catch (Exception ex)
            {
                Log?.Invoke($"Ошибка подключения: {ex.Message}");
                return;
            }

            _running = true;
            Log?.Invoke($"Подключено к {ip}:{port} (локальный IP: {localIp})");

            _ = ReceiveLoop();
        }

        public void Disconnect()
        {
            _running = false;

            try
            {
                _socket?.Shutdown(SocketShutdown.Both);
                _socket?.Close();
            }
            catch { }

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
                    int read = await _socket!.ReceiveAsync(buffer, SocketFlags.None);
                    if (read == 0)
                        break;

                    string msg = Encoding.UTF8.GetString(buffer, 0, read);
                    Log?.Invoke($"{msg}");
                }
            }
            catch
            {
            }

            _running = false;
            OnDisconnect?.Invoke();
        }

        public async Task SendAsync(string msg)
        {
            if (!_running || _socket == null)
            {
                Log?.Invoke("Нет подключения");
                return;
            }

            try
            {
                byte[] data = Encoding.UTF8.GetBytes(msg);
                await _socket.SendAsync(data, SocketFlags.None);
            }
            catch (Exception ex)
            {
                Log?.Invoke($"Ошибка отправки: {ex.Message}");
                Disconnect();
            }
        }
    }
}