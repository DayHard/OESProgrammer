using System;
using System.ComponentModel;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Windows;
using System.Windows.Threading;

namespace OESProgrammer
{
    /// <summary>
    /// Логика взаимодействия для CapProgrammingWindow.xaml
    /// </summary>
    public partial class CapProgrammingWindow
    {
        private readonly UdpClient _sender;
        private readonly UdpClient _resiver;
        private readonly IPEndPoint _endPoint;
        private readonly byte[] _doNotClose = { 10, 0, 0, 0, 0, 0, 0, 0 };
        private static readonly DispatcherTimer DoNotCloseConnectionTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        private const int TimeOut = 100;
        private const int LocalPort = 40100;

        private const int RemotePort = 40101;
        private static string _remoteIp;
        public CapProgrammingWindow(string remoteIp)
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
        private void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            e.Cancel = true;
            Hide();
        }
        // Команда по таймеру не закрывать соединение  
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
        /// <summary>
        /// Преобразование к порядку байтов Little Endian
        /// </summary>
        /// <param name="data">Массив</param>
        /// <param name="poss">Положение 1 байта числа</param>
        /// <param name="size">Размер числа в байтах</param>
        /// <returns></returns>
        private static int ToLittleEndian(byte[] data, int poss, int size)
        {
            var arr = new byte[size];
            for (int i = 0; i < arr.Length; i++)
            {
                arr[i] = data[poss + i];
            }
            return arr.Select((t, i) => t << i * 8).Sum();
        }
        /// <summary>
        /// Преобразование к порядку байтов Big Endian
        /// </summary>
        /// <param name="data">Массив</param>
        /// <param name="poss">Положение 1 байта числа</param>
        /// <param name="size">Размер числа в байтах</param>
        /// <returns></returns>
        private static int ToBigEndian(byte[] data, int poss, int size)
        {
            var arr = new byte[size];
            for (int i = 0; i < arr.Length; i++)
            {
                arr[i] = data[poss + i];
            }
            int result = 0;
            for (int i = 0; i < arr.Length; i++)
            {
                result |= arr[i] << 8 * (size - 1 - i);
            }
            return result;
        }
        private void BtnCheckCap_Click(object sender, RoutedEventArgs e)
        {
            CheckCapStatus();
        }

        private async void CheckCapStatus()
        {
            var getCapStatus = new byte[8];
            getCapStatus[0] = 12;
            getCapStatus[2] = 7;

            try
            {
                // Запрос на получения статуса крышеки
                await _sender.SendAsync(getCapStatus, getCapStatus.Length, _endPoint);
                // Ожидание ответа статуса крышек 
                var receivedData = await _resiver.ReceiveAsync();
                var data = new byte[receivedData.Buffer.Length - 8];

                Array.Copy(receivedData.Buffer, 8, data, 0, data.Length);

                if (data.Length != 64 || receivedData.Buffer[2] == 0xff)
                {
                    MessageBox.Show(this, "Произошла ошибка при получении статуса крышек", "Ошибка", MessageBoxButton.OK,
                        MessageBoxImage.Error);
                    return;
                }

                TbCapSensorOpen.Text = ToLittleEndian(data, 1, 2).ToString();
                TbCapSensorClose.Text = ToLittleEndian(data, 3, 2).ToString();
            }
            catch (SocketException)
            {
                MessageBox.Show(this, "STM не вернул запрошенный статус", "Ошибка", MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
            MessageBox.Show(this, "Данные получены", "Информация", MessageBoxButton.OK,
                MessageBoxImage.Information);
        }

        private void BtnProgrammCap_Click(object sender, RoutedEventArgs e)
        {
           ProgrammCap();
        }

        private async void ProgrammCap()
        {
            if (MessageBox.Show(this, "Вы уверены что хотите перепрошить крышку?", "Внимание", MessageBoxButton.YesNo,MessageBoxImage.Question) == MessageBoxResult.No)
              return;

            var comProgCap = new byte[72];
            comProgCap[0] = 12;
            comProgCap[2] = 6;
            comProgCap[8] = 20;

            short openValue;
            short.TryParse(TbCapSensorOpen.Text, out openValue);

            short closeValue;
            short.TryParse(TbCapSensorClose.Text, out closeValue);

            // Проверяем введенное значение крышка открыта на валидность
            if (openValue < 0 || openValue > 4096) return;
            // Проверяем введенное значение крышка закрыта на валидность
            if (closeValue < 0 || closeValue > 4096) return;

            var openValueBytes = BitConverter.GetBytes(openValue);
            comProgCap[9] = openValueBytes[0];
            comProgCap[10] = openValueBytes[1];

            var closeValueBytes = BitConverter.GetBytes(closeValue);
            comProgCap[11] = closeValueBytes[0];
            comProgCap[12] = closeValueBytes[1];

            await _sender.SendAsync(comProgCap, comProgCap.Length, _endPoint);
            // Проверяем статус прошивки
            CheckCapStatus();
        }
    }
}
