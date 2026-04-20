using laba_3.Net;
using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;

namespace laba_3
{
    /// <summary>
    /// Логика взаимодействия для Client_win.xaml
    /// </summary>
    public partial class Client_win : Window
    {
        private readonly Client_backend _client;
        public Client_win()
        {
            InitializeComponent();

            _client = new Client_backend
            {
                Log = AddLog,
                OnDisconnect = OnDisconnected
            };

            LoadLocalIPs();
        }

        private void LoadLocalIPs()
        {
            var host = Dns.GetHostEntry(Dns.GetHostName());

            var ips = host.AddressList
                .Where(ip => ip.AddressFamily == AddressFamily.InterNetwork)
                .Select(ip => ip.ToString())
                .ToList();

            if (ips.Count == 0)
                ips.Add("0.0.0.0");

            LocalIpBox.ItemsSource = ips;
            LocalIpBox.SelectedIndex = 0;
        }

        private async void Connect_Click(object sender, RoutedEventArgs e)
        {
            string serverIp = IpBox.Text.Trim();
            string localIp = LocalIpBox.SelectedItem.ToString()!;

            if (!int.TryParse(PortBox.Text, out int port))
            {
                MessageBox.Show("Порт должен быть числом");
                return;
            }

            if (!int.TryParse(client_port.Text, out int port_client))
            {
                MessageBox.Show("Порт должен быть числом");
                return;
            }

            await _client.ConnectAsync(serverIp, port, port_client, localIp);
        }

        private void Disconnect_Click(object sender, RoutedEventArgs e)
        {
            _client.Disconnect();
        }

        private async void Send_Click(object sender, RoutedEventArgs e)
        {
            string msg = MsgBox.Text.Trim();
            if (msg.Length == 0) return;

            await _client.SendAsync(msg);
            AddLog("[Я] " + msg);
            MsgBox.Clear();
        }

        private void AddLog(string text)
        {
            Dispatcher.Invoke(() =>
            {
                LogBox.AppendText(text + "\n");
                LogBox.ScrollToEnd();
            });
        }

        private void OnDisconnected()
        {
            Dispatcher.Invoke(() =>
            {
                ConnectBtn.IsEnabled = true;
                DisconnectBtn.IsEnabled = false;
                SendBtn.IsEnabled = false;
            });
        }

        private void Window_Closed(object sender, EventArgs e)
        {
            _client.Disconnect();
        }

        private void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            _client.Emerg_disconnect();
        }
    }
}
