using System;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using System.Net.Sockets;
using System.Threading;
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
        private UdpClient _receiver;
        private IPEndPoint _sendEndPoint;
        private IPEndPoint _receiveEndPoint;
        private const int TimeOut = 100;
        private const int LocalPort = 40100;
        private const int RemotePort = 40101;
        private const string FwName1 = "_7315_01_22_100_DD6_DD9_V1";
        private const string FwName2 = "_7315_01_22_100_DD6_DD9_V2";
        private const string FwName3 = "_7315_01_22_200_DD6_DD9_V3";
        private const string FwName4 = "_7315_01_22_200_DD6_DD9_V4";
        private readonly byte[] _doNotClose = { 10, 0, 0, 0, 0, 0, 0, 0 };
        private static readonly DispatcherTimer DoNotCloseConnectionTimer = new DispatcherTimer {Interval = TimeSpan.FromSeconds(1)};
        private static string _remoteIp;

        #region SupportMethods

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
            _receiver = new UdpClient(LocalPort) { Client = { ReceiveTimeout = TimeOut, DontFragment = false } };
            _sendEndPoint = new IPEndPoint(IPAddress.Parse(_remoteIp), RemotePort);

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
            _receiver.Close();
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
                _sender.Send(_doNotClose, _doNotClose.Length, _sendEndPoint);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "Невозможно отправить команду перехвата управления в STM: " + ex.Message, "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
        private static int ToLittleEndian(byte[] data, int poss, int size)
        {
            var arr = new byte[size];
            for (int i = 0; i < arr.Length; i++)
            {
                arr[i] = data[poss + i];
            }
            return arr.Select((t, i) => t << i * 8).Sum();
        }

        #endregion

        private void BtnGetFirmwareVersion_Click(object sender, RoutedEventArgs e)
        {
            GetFirmwareVersion();
        }

        private async void GetFirmwareVersion()
        {
            await Task.Run(() =>
            {
                var fw = GetFirmwareFromOed();

                var cstrcs = CountStructControlsSum(fw);
                var rstrcs = ReadStructCheckSum(fw);
                if (cstrcs != rstrcs) throw new Exception("Ошибка проверки контрольный сум структуры.");

                var cfwcs = CountFirmwareControlSum(fw, fw.Length);
                if (cfwcs != 0) throw new Exception("Контрольная сумма файла прошивки не совпадает.");

                DecodeFirmwareSettings(fw);
                SetFirmwareSettings();
            });
        }

        /// <summary>
        /// Считывание текущей версии прошивки из ВПУ
        /// </summary>
        /// <returns>Файл прошивки</returns>
        private byte[] GetFirmwareFromOed()
        {
            #region Varibles

            // Команда подготовки прошивки
            var comLoadFwtoMem = new byte[8];
            comLoadFwtoMem[0] = 0x0c;
            comLoadFwtoMem[2] = 0x0b;
            // Команда дай мне 2048 байт
            var comGetMe2048 = new byte[8];
            comGetMe2048[0] = 0x0c;
            comGetMe2048[2] = 0x03;

            const int fwsize = 262144;
            int counter = 0;
            var firmware = new byte[fwsize];
            PbOperationStatus.Maximum = fwsize;

            #endregion

            // Запрос на подготовку прошивки
            _sender.Send(comLoadFwtoMem, comLoadFwtoMem.Length, _sendEndPoint);
            Thread.Sleep(1000);

            while (counter < fwsize)
            {                         
                // Запрос на подготовку прошивки
                _sender.Send(comGetMe2048, comGetMe2048.Length, _sendEndPoint);
                for (int i = 0; i < 2; i++)
                {                       
                    // Запрос на получение блока данных (2048)
                    var resivedData = _receiver.Receive(ref _receiveEndPoint);

                    if (firmware.Length - counter >= 1024)
                    {
                        Array.Copy(resivedData, 8, firmware, counter, resivedData.Length - 8);
                        counter += resivedData.Length - 8;
                    }
                    else
                    {
                        Array.Copy(resivedData, 8, firmware, counter, firmware.Length - counter);
                        counter += firmware.Length - counter;
                    }
                    // Изменяем значение прогресс бара
                    var counter1 = counter;
                    Dispatcher.Invoke(() =>
                    {
                        PbOperationStatus.Value = counter1;
                    });
                }
            }
            // После скачивания зануляем статусбар
            PbOperationStatus.Value = 0;
            return firmware;
        }
        /// <summary>
        /// Декодирование пользовательских параметров из файла прошивки
        /// </summary>
        /// <param name="firmware">Файл прошивки</param>
        private static void DecodeFirmwareSettings(byte[] firmware)
        {
            // Номер прибора
            FwConfig.Device = (ushort)ToLittleEndian(firmware, 262082, 2);

            // Координата Х канал 1 (*16 [почему - никто не помнит, для все координат])
            FwConfig.CoordXChannel1 = (byte)(ToLittleEndian(firmware, 262084, 2) / 16);

            // Координата Х канал 1 (*16 [почему - никто не помнит, для все координат])
            FwConfig.CoordYChannel1 = (byte)(ToLittleEndian(firmware, 262086, 2) / 16);

            // Координата Х канал 2 (*16 [почему - никто не помнит, для все координат])
            FwConfig.CoordXChannel2 = (byte)(ToLittleEndian(firmware, 262088, 2) / 16);

            // Координата Y канал 2 (*16 [почему - никто не помнит, для все координат])
            FwConfig.CoordYChannel1 = (byte)(ToLittleEndian(firmware, 262090, 2) / 16);

            // Фокус 1 канала (*10 [почему - никто не помнит, для всех фокусов])
            FwConfig.FokusChannel1 = (double)ToLittleEndian(firmware, 262092, 2) / 10;

            // Фокус 2 канала (*10 [почему - никто не помнит, для всех])
            FwConfig.FokusChannel2 = (double)ToLittleEndian(firmware, 262094, 2) / 10;

            //Версия прошивки
            FwConfig.FirmwareVersion = SetFirmwareVersion(firmware);

        }
        /// <summary>
        /// Сравнение версий прошивок на соответствие исходным
        /// </summary>
        /// <param name="fw">Скачанный файл прошивки</param>
        /// <returns>Индекс соответствующей прошивки</returns>
        private static int SetFirmwareVersion(byte[] fw)
        {
            const int length = 262079;

            var fwloaded = new byte[length];
            var fwsource = new byte[length];
            Array.Copy(fw, fwloaded, fwloaded.Length);

            // Проверяем соответствие 1 прошивки
            byte[] firmware1 = (byte[])Properties.Resources.ResourceManager.GetObject(FwName1);
            // ReSharper disable once AssignNullToNotNullAttribute
            Array.Copy(firmware1,fwsource, fwsource.Length);
            if (Equals(fwloaded, fwsource))
                return 0;

            // Проверяем соответствие 2 прошивки
            byte[] firmware2 = (byte[])Properties.Resources.ResourceManager.GetObject(FwName2);
            // ReSharper disable once AssignNullToNotNullAttribute
            Array.Copy(firmware2, fwsource, fwsource.Length);
            if (Equals(fwloaded, fwsource))
                return 1;

            // Проверяем соответствие 3 прошивки
            byte[] firmware3 = (byte[])Properties.Resources.ResourceManager.GetObject(FwName3);
            // ReSharper disable once AssignNullToNotNullAttribute
            Array.Copy(firmware3, fwsource, fwsource.Length);
            if (Equals(fwloaded, fwsource))
                return 2;

            // Проверяем соответствие 4 прошивки
            byte[] firmware4 = (byte[])Properties.Resources.ResourceManager.GetObject(FwName4);
            // ReSharper disable once AssignNullToNotNullAttribute
            Array.Copy(firmware4, fwsource, fwsource.Length);
            if (Equals(fwloaded, fwsource))
                return 3;

            return -1;
        }
        /// <summary>
        /// Установка полей пользовательского интерфейс в соответсвии с данными прошивки
        /// </summary>
        private void SetFirmwareSettings()
        {
            TbDeviceNumber.Text = FwConfig.Device.ToString();
            TbCoordXChannel1.Text = FwConfig.CoordXChannel1.ToString();
            TbCoordYChannel1.Text = FwConfig.CoordYChannel1.ToString();
            TbCoordXChannel2.Text = FwConfig.CoordXChannel2.ToString();
            TbCoordYChannel2.Text = FwConfig.CoordYChannel2.ToString();
            TbFocusChannel1.Text = FwConfig.FokusChannel1.ToString(CultureInfo.InvariantCulture);
            TbFocusChannel2.Text = FwConfig.FokusChannel2.ToString(CultureInfo.InvariantCulture);
            CbVersions.SelectedIndex = FwConfig.FirmwareVersion;
        }
        private void BtnProgrammVpd_Click(object sender, RoutedEventArgs e)
        {
            if (!GetFirmwareSettings())
                return;

            var firmware = PrepareFirmware();
            if (firmware == null) throw new Exception("В ходе подготовки прошивки произошла ошибка.");

            var cs = CountFirmwareControlSum(firmware, firmware.Length - 2);
            WriteFirmwareCheckSum(firmware, firmware.Length - 2, cs);

            var zerocs = CountFirmwareControlSum(firmware, firmware.Length);
            if (zerocs != 0) throw new Exception("Посчитанная контрольная сумма не равно 0.");
            WriteFirmwareCheckSum(firmware, firmware.Length, zerocs);

            // Сохраняем прошику перед записьшу в ВПУ
            SaveFinishedFirmware(firmware);

            //if (!SendFirmWare(firmware)) return;

        }
        /// <summary>
        /// Считывание параметров, введенных пользователем, а также проверка на валидность.
        /// </summary>
        /// <returns>Успешно ли выполнение</returns>
        private bool GetFirmwareSettings()
        {
            ushort deviceNumber;
            ushort.TryParse(TbDeviceNumber.Text, out deviceNumber);
            if (deviceNumber < 42 || deviceNumber > 65535)
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
            if (focusChannel1 < 49 || focusChannel1 > 55)
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
        /// <summary>
        /// Занесение пользовательских параметров в файл прошивки
        /// </summary>
        /// <returns>Файл прошивки с параметрами</returns>
        private static byte[] PrepareFirmware()
        {
            string fwName = string.Empty;
            switch (FwConfig.FirmwareVersion)
            {
                case 0:
                    fwName = FwName1;
                    break;
                case 1:
                    fwName = FwName2;
                    break;
                case 2:
                    fwName = FwName3;
                    break;
                case 3:
                    fwName = FwName4;
                    break;
            }

            byte[] firmware = (byte[])Properties.Resources.ResourceManager.GetObject(fwName);
            if (firmware == null) return null;

            // Флаг начала структуры
            firmware[262080] = 0xAA;
            firmware[262081] = 0x55;

            // Номер прибора
            var dev = BitConverter.GetBytes(FwConfig.Device);
            firmware[262082] = dev[1];
            firmware[262083] = dev[0];

            // Координата Х канал 1 (*16 [почему - никто не помнит, для все координат])
            var x1 = BitConverter.GetBytes(FwConfig.CoordXChannel1 * 16);
            firmware[262084] = x1[1];
            firmware[262085] = x1[0];

            // Координата Х канал 1 (*16 [почему - никто не помнит, для все координат])
            var y1 = BitConverter.GetBytes(FwConfig.CoordYChannel1 * 16);
            firmware[262086] = y1[1];
            firmware[262087] = y1[0];

            // Координата Х канал 2 (*16 [почему - никто не помнит, для все координат])
            var x2 = BitConverter.GetBytes(FwConfig.CoordXChannel2 * 16);
            firmware[262088] = x2[1];
            firmware[262089] = x2[0];

            // Координата Y канал 2 (*16 [почему - никто не помнит, для все координат])
            var y2 = BitConverter.GetBytes(FwConfig.CoordYChannel2 * 16);
            firmware[262090] = y2[1];
            firmware[262091] = y2[0];

            // Фокус 1 канала (*10 [почему - никто не помнит, для всех фокусов])
            var f1 = BitConverter.GetBytes((ushort)(FwConfig.FokusChannel1 * 10));
            firmware[262092] = f1[1];
            firmware[262093] = f1[0];

            // Фокус 2 канала (*10 [почему - никто не помнит, для всех])
            var f2 = BitConverter.GetBytes((ushort)(FwConfig.FokusChannel2 * 10));
            firmware[262094] = f2[1];
            firmware[262095] = f2[0];

            // Зануляем байты согласно структуре (резервные байты)
            firmware[262096] = firmware[262097] = firmware[262104] = firmware[262105] = 0;

            var checksumstruct = CountStructControlsSum(firmware);
            WriteStructControlSum(firmware, checksumstruct);

            return firmware;
        }
        /// <summary>
        /// Расчет контрольной суммы структуры с пользовательскими параметрами
        /// </summary>
        /// <param name="firmware">Файл прошивки</param>
        /// <returns>Контрольную сумму</returns>
        private static int CountStructControlsSum(byte[] firmware)
        {
            int checksumstruct = 0;
            for (int i = 262080; i < 262097; i += 2)
            {
                checksumstruct ^= firmware[i] << 8 | firmware[i + 1];
            }
            return checksumstruct;
        }
        /// <summary>
        /// Запись контрольной суммы структуры с пользовательскими параметрами в файл прошивки
        /// </summary>
        /// <param name="firmware">Файл прошивки</param>
        /// <param name="checksumstruct">Контрольная сумма структуры</param>
        private static void WriteStructControlSum(byte[] firmware, int checksumstruct)
        {
            var cs = BitConverter.GetBytes((ushort)checksumstruct);
            firmware[262098] = cs[1];
            firmware[262099] = cs[0];
        }
        /// <summary>
        /// Считывание контрольной суммы структуры пользовательскиз параметров из файла прошивки
        /// </summary>
        /// <param name="fw">Файл прошивки</param>
        /// <returns>Контрольную сумму</returns>
        private static int ReadStructCheckSum(byte[] fw)
        {
            return ToLittleEndian(fw, 262098, 2);
        }
        /// <summary>
        /// Расчет контрольной суммы файла прошивки
        /// </summary>
        /// <param name="firmware">Файл прошивки</param>
        /// <param name="length">Длинна масива</param>
        /// <returns>Контрольную сумму</returns>
        private static uint CountFirmwareControlSum(byte[] firmware, int length)
        {
            // Портировано из исходников прошлой программы.
            uint crc = 0;
            const uint polynom = 0x80050000;
            firmware[length - 1] = 0;
            firmware[length - 2] = 0;

            crc += (uint)firmware[0] << 24;
            crc += (uint)firmware[1] << 16;

            for (int i = 2; i < length; i += 2)
            {
                crc += (uint)(firmware[i] << 8);
                crc += firmware[i + 1];
                for (int j = 0; j < 16; j++)
                {
                    if ((crc & 0x80000000) == 0x80000000)
                    {
                        crc <<= 1;
                        crc ^= polynom;
                    }
                    else crc <<= 1;
                }
            }
            return crc >> 16;
        }
        /// <summary>
        /// Запись контрольной суммы файла в прошивку.
        /// </summary>
        /// <param name="firmware">Файл прошивки</param>
        /// <param name="length">Длинна прошивки</param>
        /// <param name="checksum">Значение контрольной суммы</param>
        private static void WriteFirmwareCheckSum(byte[] firmware, int length, uint checksum)
        {
            var csbytes = BitConverter.GetBytes(checksum);
            firmware[length - 2] = csbytes[1];
            firmware[length - 1] = csbytes[0];
        }
        /// <summary>
        /// Сохранения файла прошивки на жестком диске
        /// </summary>
        /// <param name="firmware">Массив прошивки</param>
        private static void SaveFinishedFirmware(byte[] firmware)
        {
            var version = FwConfig.FirmwareVersion + 1;
            using (var bin = new BinaryWriter(File.OpenWrite("Номер прибора " + FwConfig.Device + " Версия прошивки " + version + ".bex")))
            {
                bin.Write(firmware);
            }
        }

        private static bool SendFirmWare(byte[] firmware)
        {
            try
            {
                //var commandStm = new byte[72];
                //commandStm[0] = 0x0c;
                //commandStm[2] = 0x08;

                //var commandOed = new byte[6];
                //var dataOed = new byte[6];

                //int counter = 0;
                //int adress = 0;

                //while (counter < firmware.Length)
                //{

                //    commandOed[0] = 
                //    int j = 0;
                //    for (int i = counter; i < counter + 58; i++)
                //    {
                //        dataOed[j] = firmware[i];           
                //        j++;
                //    }
                //    counter += j;
                //}

                //_sender.Send(comProgCap, comProgCap.Length, _endPoint);

                //var rec = _resiver.Receive(ref _endPoint);

                //if (rec[0] != 0xc || rec[2] != 0x06)
                //{
                //    Dispatcher.Invoke(() =>
                //    {
                //        MessageBox.Show(this, "Произошла ошибка, в процессе прошивки крышки", "Ошибка",
                //            MessageBoxButton.OK, MessageBoxImage.Error);
                //        SetStatusFail();
                //        isNotSuccess = true;
                //    });
                //}
            }
            catch (SocketException)
            {
                //Dispatcher.Invoke(() =>
                //{
                //    MessageBox.Show(this, "Stm не отвечает.", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
                //    SetStatusFail();
                //    isNotSuccess = true;
                //});
            }
            return false;
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
