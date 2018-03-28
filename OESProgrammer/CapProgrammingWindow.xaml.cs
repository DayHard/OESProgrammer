using System;
using System.ComponentModel;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;

namespace OESProgrammer
{
    /// <summary>
    /// Логика взаимодействия для CapProgrammingWindow.xaml
    /// </summary>
    public partial class CapProgrammingWindow
    {
        #region Variables

        private readonly byte[] _doNotClose = { 10, 0, 0, 0, 0, 0, 0, 0 };
        private static readonly DispatcherTimer DoNotCloseConnectionTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        private UdpClient _sender;
        private UdpClient _receiver;
        private IPEndPoint _sendEndPoint;
        private IPEndPoint _receiveEndPoint;
        private const int TimeOut = 100;
        private const int LocalPort = 40100;
        private const int RemotePort = 40101;
        private static string _remoteIp;

        #endregion

        #region SupportMethods

        public CapProgrammingWindow(string remoteIp)
        {
            // IP адресc STM
            _remoteIp = remoteIp;
            // Инициализация компонентов формы
            InitializeComponent();
            // Подпись на событие изменение видимости формы
            IsVisibleChanged += CapProgrammingWindow_IsVisibleChanged;
        }
        // Обработчик события изменения видимости формы
        // Выделение ресурсов перед отображением окна
        private void CapProgrammingWindow_IsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            _sender = new UdpClient{Client = {SendTimeout = 100}};
            _receiver = new UdpClient(LocalPort) { Client = { ReceiveTimeout = TimeOut, DontFragment = false } };
            _sendEndPoint = new IPEndPoint(IPAddress.Parse(_remoteIp), RemotePort);

            // Подпись на событие, поддержания связи с STM
            DoNotCloseConnectionTimer.Tick += DoNotCloseConnectionTimer_Tick;
            // Запуск таймера
            DoNotCloseConnectionTimer.Start();
            // Отпись от события изменения видимости формы
            IsVisibleChanged -= CapProgrammingWindow_IsVisibleChanged;
        }
        // Освобождение ресурсов перед сворачиваем окна
        private void Window_Closing(object sender, CancelEventArgs e)
        {
            DoNotCloseConnectionTimer.Stop();
            _sender.Close();
            _receiver.Close();

            e.Cancel = true;
            Hide();
            // Подпись на событие изменение видимости формы
            IsVisibleChanged += CapProgrammingWindow_IsVisibleChanged;
        }
        // Команда по таймеру не закрывать соединение  
        private void DoNotCloseConnectionTimer_Tick(object sender, EventArgs e)
        {
            try
            {
                _sender.Send(_doNotClose, _doNotClose.Length, _sendEndPoint);
            }
            catch (SocketException)
            {
                MessageBox.Show(this, "Невозможно отправить команду перехвата управления в STM. Удаленное устройство недоступно. ", "Ошибка", MessageBoxButton.OK, 
                    MessageBoxImage.Error);
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
        /// Установка значения статуса операции Успех
        /// </summary>
        private void SetStatusSuccess()
        {
            Dispatcher.Invoke(()=>
            {
                LabStatusOperation.Content = "[" + DateTime.Now + "]" + " Успех.";
            });
        }
        /// <summary>
        /// Установка значения статуса операции Ошибка
        /// </summary>
        private void SetStatusFail()
        {
            Dispatcher.Invoke(() =>
            {
                LabStatusOperation.Content ="[" + DateTime.Now + "]" + " Ошибка.";
            });
        }
        #endregion

        private void BtnCheckCap_Click(object sender, RoutedEventArgs e)
        {
            CheckCapStatus();
        }
        // Запрос состоянии крышек ОЭД
        private async void CheckCapStatus()
        {
            var getCapStatus = new byte[8];
            getCapStatus[0] = 12;
            getCapStatus[2] = 7;

            await Task.Run(() =>
            {
                try
                {               
                    // Запрос на получения статуса крышеки
                    _sender.Send(getCapStatus, getCapStatus.Length, _sendEndPoint);
                    // Ожидание ответа статуса крышек 
                    var recData = _receiver.Receive(ref _receiveEndPoint);
                    var data = new byte[recData.Length - 8];

                    Array.Copy(recData, 8, data, 0, data.Length);

                    if (data.Length != 64 || recData[2] == 0xff)
                    {
                        Dispatcher.Invoke(() =>
                        {
                            MessageBox.Show(this, "Произошла ошибка при получении статуса крышек из ОЭД.", "Ошибка",
                                MessageBoxButton.OK,
                                MessageBoxImage.Error);
                            SetStatusFail();
                        });
                        return;
                    }
                    Dispatcher.Invoke(() => 
                    {
                        TbCapSensorOpen.Text = ToLittleEndian(data, 1, 2).ToString();
                        TbCapSensorClose.Text = ToLittleEndian(data, 3, 2).ToString();
                    });
                }
                catch (SocketException)
                {
                    Dispatcher.Invoke(() =>
                    {
                        MessageBox.Show(this, "Stm не ответила на запрос. ", "Ошибка", MessageBoxButton.OK,
                            MessageBoxImage.Error);
                        SetStatusFail();
                    });
                    return;
                }
                SetStatusSuccess();
            });

        }
        private void BtnProgrammCap_Click(object sender, RoutedEventArgs e)
        {
           ProgrammCap();
        }
        // Команда перепрошивки крышек ОЭД
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
            if (openValue < 0 || openValue > 4096)
            {
                MessageBox.Show(this, "Значение датчика положения крышки (Открыто) недопустимо. Введите значение от 0 до 4096.", "Ошибка",
                    MessageBoxButton.OK, MessageBoxImage.Error);
                SetStatusFail();
                return;
            }
            // Проверяем введенное значение крышка закрыта на валидность
            if (closeValue < 0 || closeValue > 4096)
            {
                MessageBox.Show(this, "Значение датчика положения крышки (Закрыто) недопустимо. Введите значение от 0 до 4096.", "Ошибка",
                    MessageBoxButton.OK, MessageBoxImage.Error);
                SetStatusFail();
                return;
            }

            var openValueBytes = BitConverter.GetBytes(openValue);
            comProgCap[9] = openValueBytes[0];
            comProgCap[10] = openValueBytes[1];

            var closeValueBytes = BitConverter.GetBytes(closeValue);
            comProgCap[11] = closeValueBytes[0];
            comProgCap[12] = closeValueBytes[1];

            bool isNotSuccess = false;
            await Task.Run(() =>
            {
                try
                {
                    _sender.Send(comProgCap, comProgCap.Length, _sendEndPoint);

                    var rec = _receiver.Receive(ref _receiveEndPoint);

                    if (rec[0] != 0xc || rec[2] != 0x06)
                    {
                        Dispatcher.Invoke(() =>
                        {
                            MessageBox.Show(this, "Произошла ошибка, в процессе прошивки крышки", "Ошибка",
                                MessageBoxButton.OK, MessageBoxImage.Error);
                            SetStatusFail();
                            isNotSuccess = true;
                        });
                    }
                }
                catch (SocketException)
                {
                    Dispatcher.Invoke(() =>
                    {
                        MessageBox.Show(this, "Stm не отвечает.", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
                        SetStatusFail();
                        isNotSuccess = true;
                    });
                }
            });

            if (!isNotSuccess)
            // Проверяем статус прошивки
            CheckCapStatus();
        }
    }
}
