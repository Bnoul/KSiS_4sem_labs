using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Text;

namespace laba_3.Net
{
    class Server_backend
    {
        private Socket? _listener;
        private readonly List<Socket> _clients = new();
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
                IPAddress address;

                if (string.IsNullOrWhiteSpace(ip) || ip == "0.0.0.0")
                    address = IPAddress.Any;
                else if (!IPAddress.TryParse(ip, out address))
                {
                    Log?.Invoke("Некорректный IP адрес");
                    return;
                }

                _listener = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);

                _listener.Bind(new IPEndPoint(address, port));
                _listener.Listen(100);
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

            try { _listener?.Close(); } catch { }

            lock (_clients)
            {
                foreach (var c in _clients)
                {
                    try
                    {
                        c.Shutdown(SocketShutdown.Both);
                        c.Close();
                    }
                    catch { }
                }
                _clients.Clear();
            }

            Log?.Invoke("Сервер остановлен");
        }

        private async Task AcceptLoop()
        {
            while (_running)
            {
                Socket? client = null;

                try
                {
                    client = await _listener!.AcceptAsync();
                }
                catch
                {
                    break;
                }

                lock (_clients)
                    _clients.Add(client);

                Log?.Invoke($"Клиент подключился: {client.RemoteEndPoint}");

                _ = HandleClient(client);
            }
        }

        private async Task HandleClient(Socket client)
        {
            var buffer = new byte[4096];

            try
            {
                while (_running)
                {
                    int read = await client.ReceiveAsync(buffer, SocketFlags.None);
                    if (read == 0)
                        break;

                    string msg = Encoding.UTF8.GetString(buffer, 0, read);
                    Log?.Invoke($"От {client.RemoteEndPoint}: {msg}");

                    Broadcast($"{((IPEndPoint)client.RemoteEndPoint).Address.ToString()}: {msg}", client);
                }
            }
            catch
            {
            }

            lock (_clients)
                _clients.Remove(client);

            Log?.Invoke($"Клиент отключился: {client.RemoteEndPoint}");

            try
            {
                client.Shutdown(SocketShutdown.Both);
                client.Close();
            }
            catch { }
        }

        private void Broadcast(string msg, Socket sender)
        {
            byte[] data = Encoding.UTF8.GetBytes(msg);

            lock (_clients)
            {
                foreach (var c in _clients.ToList())
                {
                    if (c == sender) continue;

                    try
                    {
                        c.SendAsync(data, SocketFlags.None);
                    }
                    catch
                    {
                        try
                        {
                            c.Shutdown(SocketShutdown.Both);
                            c.Close();
                        }
                        catch { }

                        _clients.Remove(c);
                    }
                }
            }
        }
    }
}
