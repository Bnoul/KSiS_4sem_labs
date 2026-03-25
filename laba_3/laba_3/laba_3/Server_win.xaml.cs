using System;
using System.Collections.Generic;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using laba_3.Net;

namespace laba_3
{
    /// <summary>
    /// Логика взаимодействия для Server_win.xaml
    /// </summary>
    public partial class Server_win : Window
    {
            private readonly Server_backend _server;

        public Server_win()
        {
            InitializeComponent();
            _server = new Server_backend { Log = AddLog };
        }
        private async void Start_Click(object sender, RoutedEventArgs e)
        {
            string ip = IpBox.Text.Trim();
            if (!int.TryParse(PortBox.Text, out int port))
            {
                MessageBox.Show("Порт должен быть числом");
                return;
            }
            await _server.StartAsync(ip, port);
        }

        private void Stop_Click(object sender, RoutedEventArgs e)
        {
            _server.Stop();
        }

        private void AddLog(string text)
        {
            Dispatcher.Invoke(() =>
            {
                LogBox.AppendText(text + "\n");
                LogBox.ScrollToEnd();
            });
        }

        private void Window_Closed(object sender, EventArgs e)
        {
            _server.Stop();
        }
    }
}
