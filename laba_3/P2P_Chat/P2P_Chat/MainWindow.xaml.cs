using System.Net;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;
using P2P_Chat.GUI_stuff;

namespace P2P_Chat
{
    public partial class MainWindow : Window
    {
        private View_model? _vm;

        public MainWindow()
        {
            InitializeComponent();

            // Простейший способ задать имя и IP — через Environment.GetCommandLineArgs()
            var args = Environment.GetCommandLineArgs();
            string name = "User";
            string ipStr = "127.0.0.1";

            if (args.Length >= 2) name = args[1];
            if (args.Length >= 3) ipStr = args[2];

            if (!IPAddress.TryParse(ipStr, out var ip))
            {
                MessageBox.Show("Неверный IP, используется 127.0.0.1");
                ip = IPAddress.Parse("127.0.0.1");
            }

            _vm = new View_model(name, ip);
            DataContext = _vm;
        }

        protected override void OnClosed(EventArgs e)
        {
            base.OnClosed(e);
            _vm?.Stop();
        }
    }
}