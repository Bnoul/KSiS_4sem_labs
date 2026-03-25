using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Text;

namespace laba_3.Net
{
    class Server_backend
    {
        private TcpListener? _listener;
        private readonly List<TcpClient> _clients = new();
        private bool _running;

        public Action<string>? Log;

        public async Task StartAsync(string ip, int port)
        {
            if (_running)
            {
                Log?.Invoke("Сервер уже запущен");
                return;
            }

            try
            {
                _listener = new TcpListener(IPAddress.Parse(ip), port);
                _listener.Start();
            }
            catch (Exception ex)
            {
                Log?.Invoke($"Ошибка запуска сервера: {ex.Message}");
                return;
            }

            _running = true;
            Log?.Invoke($"Сервер запущен на {ip}:{port}");

            _ = AcceptLoop();
        }

        public void Stop()
        {
            _running = false;

            try { _listener?.Stop(); } catch { }

            lock (_clients)
            {
                foreach (var c in _clients)
                    try { c.Close(); } catch { }
                _clients.Clear();
            }

            Log?.Invoke("Сервер остановлен");
        }

        private async Task AcceptLoop()
        {
            while (_running)
            {
                TcpClient? client = null;

                try
                {
                    client = await _listener!.AcceptTcpClientAsync();
                }
                catch
                {
                    break;
                }

                lock (_clients)
                    _clients.Add(client);

                Log?.Invoke($"Клиент подключился: {client.Client.RemoteEndPoint}");

                _ = HandleClient(client);
            }
        }

        private async Task HandleClient(TcpClient client)
        {
            var stream = client.GetStream();
            var buffer = new byte[4096];

            try
            {
                while (_running)
                {
                    int read = await stream.ReadAsync(buffer);
                    if (read == 0)
                        break;

                    string msg = Encoding.UTF8.GetString(buffer, 0, read);
                    Log?.Invoke($"От {client.Client.RemoteEndPoint}: {msg}");

                    Broadcast(msg, client);
                }
            }
            catch { }

            lock (_clients)
                _clients.Remove(client);

            Log?.Invoke($"Клиент отключился: {client.Client.RemoteEndPoint}");
            client.Close();
        }

        private void Broadcast(string msg, TcpClient sender)
        {
            byte[] data = Encoding.UTF8.GetBytes(msg);

            lock (_clients)
            {
                foreach (var c in _clients.ToList())
                {
                    if (c == sender) continue;

                    try
                    {
                        c.GetStream().WriteAsync(data);
                    }
                    catch
                    {
                        try { c.Close(); } catch { }
                        _clients.Remove(c);
                    }
                }
            }
        }
    }
}
