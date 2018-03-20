using System;
using System.Net;
using System.Net.Sockets;
using System.Windows;
using System.Windows.Threading;

namespace OESProgrammer
{
    /// <summary>
    /// Логика взаимодействия для CapProgrammingWindow.xaml
    /// </summary>
    public partial class VpdProgrammingWindow
    {
        private readonly UdpClient _sender;
        private readonly UdpClient _resiver;
        private readonly IPEndPoint _endPoint;
        private const int TimeOut = 100;
        private const int LocalPort = 40100;
        private readonly byte[] _doNotClose = { 10, 0, 0, 0, 0, 0, 0, 0 };
        private const int RemotePort = 40101;
        private static readonly DispatcherTimer DoNotCloseConnectionTimer = new DispatcherTimer {Interval = TimeSpan.FromSeconds(1)};
        private static string _remoteIp;
        public VpdProgrammingWindow(string remoteIp)
        {
            // IP адресc STM
            _remoteIp = remoteIp;
            // Инициализация компонентов формы
            InitializeComponent();

            _sender = new UdpClient();
            _resiver = new UdpClient(LocalPort) { Client = { ReceiveTimeout = TimeOut, DontFragment = false } };
            _endPoint = new IPEndPoint(IPAddress.Parse(_remoteIp), RemotePort);

            // Подпись на событие, поддержания связи с STM
            DoNotCloseConnectionTimer.Tick += DoNotCloseConnectionTimer_Tick;
            // Запуск таймера
            DoNotCloseConnectionTimer.Start();
        }
        private void DoNotCloseConnectionTimer_Tick(object sender, EventArgs e)
        {
            try
            {
                _sender.Send(_doNotClose, _doNotClose.Length, _endPoint);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "Невозможно отправить команду перехвата управления в STM: " + ex.Message, "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
                e.Cancel = true;
                Hide();
        }
    }
}
