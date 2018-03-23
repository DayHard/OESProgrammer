using System;
using System.ComponentModel;
using System.Net;
using System.Net.Sockets;
using System.Windows;
using System.Windows.Threading;

namespace OESProgrammer
{
    /// <summary>
    /// Логика взаимодействия для VpdProgrammingWindow.xaml
    /// </summary>
    public partial class VpdProgrammingWindow
    {
        private UdpClient _sender;
        private UdpClient _resiver;
        private IPEndPoint _endPoint;
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
            // Подпись на событие изменение видимости формы
            IsVisibleChanged += VpdProgrammingWindow_IsVisibleChanged;

        }
        private void VpdProgrammingWindow_IsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            _sender = new UdpClient();
            _resiver = new UdpClient(LocalPort) { Client = { ReceiveTimeout = TimeOut, DontFragment = false } };
            _endPoint = new IPEndPoint(IPAddress.Parse(_remoteIp), RemotePort);

            // Подпись на событие, поддержания связи с STM
            DoNotCloseConnectionTimer.Tick += DoNotCloseConnectionTimer_Tick;
            // Запуск таймера
            DoNotCloseConnectionTimer.Start();

            IsVisibleChanged -= VpdProgrammingWindow_IsVisibleChanged;
        }
        private void Window_Closing(object sender, CancelEventArgs e)
        {
            // Закрываем текущие соединения
            DoNotCloseConnectionTimer.Stop();
            _sender.Close();
            _resiver.Close();
            // Отменяем закрытие формы
            e.Cancel = true;
            Hide();
            // Отпись от события изменение видимости формы
            IsVisibleChanged += VpdProgrammingWindow_IsVisibleChanged;
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

        private void BtnProgrammVpd_Click(object sender, RoutedEventArgs e)
        {
            if (!BoxesIsValid())
                return;

            if(!PrepareFirmware())
                return;

        }

        private static bool PrepareFirmware()
        {
            string fwName = string.Empty;
            switch (FwConfig.FirmwareVersion)
            {
                case 0:
                    fwName = "_7315_01_22_100_DD6_DD9_V1";
                    break;
                case 1:
                    fwName = "_7315_01_22_100_DD6_DD9_V2";
                    break;
                case 2:
                    fwName = "_7315_01_22_200_DD6_DD9_V3";
                    break;
                case 3:
                    fwName = "_7315_01_22_200_DD6_DD9_V4";
                    break;
            }

            object obj = Properties.Resources.ResourceManager.GetObject(fwName);

            byte[] firmware = (byte[])obj;

            if (firmware == null) return false;

            // Флаг начала структуры
            firmware[262080] = 0xAA;
            firmware[262081] = 0x55;

            // Номер прибора
            var dev = BitConverter.GetBytes(FwConfig.Device);
            firmware[262082] = dev[1];
            firmware[262083] = dev[0];

            // Координата Х канал 1 (*16 [почему никто не помнит, для все координат])
            var x1 = BitConverter.GetBytes(FwConfig.CoordXChannel1 * 16);
            firmware[262084] = x1[1];
            firmware[262085] = x1[0];

            // Координата Х канал 1 (*16 [почему никто не помнит, для все координат])
            var y1 = BitConverter.GetBytes(FwConfig.CoordYChannel1 * 16);
            firmware[262086] = y1[1];
            firmware[262087] = y1[0];

            // Координата Х канал 2 (*16 [почему никто не помнит, для все координат])
            var x2 = BitConverter.GetBytes(FwConfig.CoordXChannel2 * 16);
            firmware[262088] = x2[1];
            firmware[262089] = x2[0];

            // Координата Y канал 2 (*16 [почему никто не помнит, для все координат])
            var y2 = BitConverter.GetBytes(FwConfig.CoordYChannel2 * 16);
            firmware[262090] = y2[1];
            firmware[262091] = y2[0];

            // Фокус 1 канала (*10 [почему никто не помнит, для всех фокусов])
            var f1 = BitConverter.GetBytes((ushort)(FwConfig.FokusChannel1 * 10));
            firmware[262092] = f1[1];
            firmware[262093] = f1[0];

            // Фокус 2 канала (*10 [почему никто не помнит, для всех])
            var f2 = BitConverter.GetBytes((ushort)(FwConfig.FokusChannel2 * 10));
            firmware[262094] = f2[1];
            firmware[262095] = f2[0];

            // Зануляем байты согласно структуре
            firmware[262096] = firmware[262097] = firmware[262104] = firmware[262105] = 0;

            CountControlsSum(firmware);

            return true;
        }

        private static void CountControlsSum(byte[] firmware)
        {
            int checksumstruct = 0;
            for (int i = 262080; i < 262097; i += 2)
            {
                checksumstruct ^= firmware[i] << 8 | firmware[i + 1];
            }
            var cs = BitConverter.GetBytes((ushort)checksumstruct);
            firmware[262098] = cs[1];
            firmware[262099] = cs[0];

            int checksum = 0;
            for (int i = 0; i < firmware.Length - 4; i += 2)
            {
                checksum ^= firmware[i] << 8 | firmware[i + 1];
            }

            var cs2 = BitConverter.GetBytes((ushort)checksum);
            firmware[262140] = cs2[1];
            firmware[262141] = cs2[0];

            int globcs = 0;
            for (int i = 0; i < firmware.Length - 2; i++)
            {
                globcs ^= firmware[i] << 8 | firmware[i + 1];
            }
        }

        // Загружаем данные, введенные пользователем и проверяем их на валидность
        private bool BoxesIsValid()
        {
            ushort  deviceNumber;
            ushort.TryParse(TbDeviceNumber.Text, out deviceNumber);
            if (deviceNumber < 41 || deviceNumber > 65535)
            {
                MessageBox.Show(this, "Номер прибора может быть от 42 до 65535", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
                return false;
            }
            FwConfig.Device = deviceNumber;

            byte coordXChannel1;
            var cxc1IsValid = byte.TryParse(TbCoordXChannel1.Text, out coordXChannel1);
            if (!cxc1IsValid)
            {
                MessageBox.Show(this, "Координата Х по каналу 1 может принимать значения от 0 до 255", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
                return false;
            }
            FwConfig.CoordXChannel1 = coordXChannel1;

            byte coordYChannel1;
            var cyc1IsValid = byte.TryParse(TbCoordYChannel1.Text, out coordYChannel1);
            if (!cyc1IsValid)
            {
                MessageBox.Show(this, "Координата Y по каналу 1 может принимать значения от 0 до 255", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
                return false;
            }
            FwConfig.CoordYChannel1 = coordYChannel1;

            byte coordXChannel2;
            var cxc2IsValid = byte.TryParse(TbCoordXChannel2.Text, out coordXChannel2);
            if (!cxc2IsValid)
            {
                MessageBox.Show(this, "Координата Х по каналу 2 может принимать значения от 0 до 255", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
                return false;
            }
            FwConfig.CoordXChannel2 = coordXChannel2;

            byte coordYChannel2;
            var cyc2IsValid = byte.TryParse(TbCoordYChannel2.Text, out coordYChannel2);
            if (!cyc2IsValid)
            {
                MessageBox.Show(this, "Координата Y по каналу 2 может принимать значения от 0 до 255", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
                return false;
            }
            FwConfig.CoordYChannel2 = coordYChannel2;

            double focusChannel1;
            double.TryParse(TbFocusChannel1.Text, out focusChannel1);
            if (focusChannel1 < 49 || focusChannel1 >55)
            {
                MessageBox.Show(this, "Фокус канала 1 может принимать значения от 49 до 55, с шагов 0,1", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
                return false;
            }
            FwConfig.FokusChannel1 = focusChannel1;

            double focusChannel2;
            double.TryParse(TbFocusChannel2.Text, out focusChannel2);
            if (focusChannel2 < 320 || focusChannel2 > 340)
            {
                MessageBox.Show(this, "Фокус канала 2 может принимать значения от 320 до 340, с шагов 0,1", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
                return false;
            }
            FwConfig.FokusChannel2 = focusChannel2;

            // Проверяем выбрана ли прошивка
            if (CbVersions.SelectedIndex == -1)
            {
                MessageBox.Show(this, "Не выбрана версия прошивки.", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
                return false;
            }
            FwConfig.FirmwareVersion = CbVersions.SelectedIndex;

            // Запрос у пользователя, на согласие прошить ВПУ
            if (MessageBox.Show(this, "Вы уверены что хотите перепрошить ВПУ?", "Внимание", MessageBoxButton.YesNo,
                    MessageBoxImage.Question) == MessageBoxResult.No)
                return false;        

            return true;
        }
    }
    // Хранятся значения полей, введенных пользователем
    public static class FwConfig
    {
        public static ushort Device;
        public static byte CoordXChannel1;
        public static byte CoordYChannel1;
        public static byte CoordXChannel2;
        public static byte CoordYChannel2;
        public static double FokusChannel1;
        public static double FokusChannel2;
        public static int FirmwareVersion;
    }
}
