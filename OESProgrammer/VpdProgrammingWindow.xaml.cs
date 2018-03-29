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
        #region Variables

        private UdpClient _sender;
        private UdpClient _receiver;
        private IPEndPoint _sendEndPoint;
        private IPEndPoint _receiveEndPoint;
        private const int TimeOut = 100;
        private const int LocalPort = 40100;
        private const int RemotePort = 40101;
        private const uint AdrRam = 0x80008000;
        private const uint AdrRom = 0x1400000;
        private const string FwName1 = "_7315_01_22_100_DD6_DD9_V1";
        private const string FwName2 = "_7315_01_22_100_DD6_DD9_V2";
        private const string FwName3 = "_7315_01_22_200_DD6_DD9_V3";
        private const string FwName4 = "_7315_01_22_200_DD6_DD9_V4";
        private readonly byte[] _doNotClose = { 10, 0, 0, 0, 0, 0, 0, 0 };
        private static readonly DispatcherTimer DoNotCloseConnectionTimer = new DispatcherTimer {Interval = TimeSpan.FromSeconds(1)};
        private static string _remoteIp;

        #endregion

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
        /// <summary>
        /// Включение кнопок Считать\Прошить
        /// </summary>
        private void SetButtonsEnable()
        {
            Dispatcher.Invoke(() =>
            {
                BtnGetFirmwareVersion.IsEnabled = true;
                BtnProgrammVpd.IsEnabled = true; 
            });
        }
        /// <summary>
        /// Выключение кнопок Считать\Прошить
        /// </summary>
        private void SetButtonsDisable()
        {
            Dispatcher.Invoke(() =>
            {
                BtnGetFirmwareVersion.IsEnabled = false;
                BtnProgrammVpd.IsEnabled = false;
            });
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

        #endregion

        private void BtnGetFirmwareVersion_Click(object sender, RoutedEventArgs e)
        {
            GetFirmwareVersion();
        }
        private async void GetFirmwareVersion()
        {
            await Task.Run(() =>
            {                
                // Отключение кнопок Считать\Прошить 
                SetButtonsDisable();

                // Скачивание прошивки из ВПУ
                var fw = GetFirmwareFromOed(AdrRom);
                if (fw == null) return;
                // Проверка контрольной суммый структуры с параметрами пользователя
                var cstrcs = CountStructControlsSum(fw);
                var rstrcs = ReadStructCheckSum(fw);
                if (cstrcs != rstrcs)
                {
                    Dispatcher.Invoke(() => 
                    {
                        MessageBox.Show(this, "Ошибка скачивания. Параметры повреждены.", "Ошибка",
                            MessageBoxButton.OK, MessageBoxImage.Error);
                    });
                    return;
                }

                // Проверка контрольной суммы всего файла
                var zerofwcs = CountFirmwareControlSum(fw, fw.Length);
                if (zerofwcs != 0)
                {
                    Dispatcher.Invoke(() =>
                    {
                        MessageBox.Show(this, "Ошибка скачивания. Файл прошивки поврежден.", "Ошибка",
                            MessageBoxButton.OK, MessageBoxImage.Error);
                    });
                    return;
                }

                // Декодирвание параметров из файла прошивки (хранятся в классе FwConfig)
                DecodeFirmwareSettings(fw);
                // Применение декодированных параметров к TextBox 
                SetFirmwareSettings();
            });
            // Включение кнопок Считать\Прошить
            SetButtonsEnable();
        }
        /// <summary>
        /// Считывание текущей версии прошивки из ВПУ
        /// </summary>
        /// <returns>Файл прошивки</returns>
        private byte[] GetFirmwareFromOed(uint adrMem)
        {
            #region Varibles

            // Команда подготовки прошивки
            var comLoadFwtoMem = new byte[16];
            comLoadFwtoMem[0] = 0x0c;
            comLoadFwtoMem[2] = 0x0b;
            // Адресс начала считывания
            var adr = BitConverter.GetBytes(adrMem);
            comLoadFwtoMem[8] = adr[0];
            comLoadFwtoMem[9] = adr[1];
            comLoadFwtoMem[10] = adr[2];
            comLoadFwtoMem[11] = adr[3];

            // Команда дай мне 2048 байт
            var comGetMe2048 = new byte[8];
            comGetMe2048[0] = 0x0c;
            comGetMe2048[2] = 0x03;

            const int fwsize = 262144;
            int counter = 0;
            var firmware = new byte[fwsize];
            Dispatcher.Invoke(() => { PbOperationStatus.Maximum = fwsize; });

            #endregion

            DoNotCloseConnectionTimer.Stop();
            try
            {
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

                        if (resivedData[0] != 0x0c || resivedData[2] != 0x03 || resivedData[3] == 0xff)
                        {
                            Dispatcher.Invoke(() =>
                            {
                                MessageBox.Show(this, "STM вернул ошибку ОЭД.", "Ошибка",
                                    MessageBoxButton.OK, MessageBoxImage.Error);
                            });
                            return null;
                        }
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
            }
            catch (SocketException)
            {
                Dispatcher.Invoke(() =>
                {
                    MessageBox.Show(this, "STM не ответил на команду.", "Ошибка", MessageBoxButton.OK,
                        MessageBoxImage.Error);
                });
                return null;
            }
            finally
            {
                DoNotCloseConnectionTimer.Start();
                // После скачивания зануляем статусбар
                Dispatcher.Invoke(() => { PbOperationStatus.Value = 0; });
            }
            return firmware;
        }
        /// <summary>
        /// Декодирование пользовательских параметров из файла прошивки
        /// </summary>
        /// <param name="firmware">Файл прошивки</param>
        private static void DecodeFirmwareSettings(byte[] firmware)
        {
            // Номер прибора
            FwConfig.Device = (ushort)ToBigEndian(firmware, 262082, 2);

            // Координата Х канал 1 (*16 [почему - никто не помнит, для все координат])
            FwConfig.CoordXChannel1 = (byte)(ToBigEndian(firmware, 262084, 2) / 16);

            // Координата Y канал 1 (*16 [почему - никто не помнит, для все координат])
            FwConfig.CoordYChannel1 = (byte)(ToBigEndian(firmware, 262086, 2) / 16);

            // Координата Х канал 2 (*16 [почему - никто не помнит, для все координат])
            FwConfig.CoordXChannel2 = (byte)(ToBigEndian(firmware, 262088, 2) / 16);

            // Координата Y канал 2 (*16 [почему - никто не помнит, для все координат])
            FwConfig.CoordYChannel2 = (byte)(ToBigEndian(firmware, 262090, 2) / 16);

            // Фокус 1 канала (*10 [почему - никто не помнит, для всех фокусов])
            FwConfig.FokusChannel1 = (double)ToBigEndian(firmware, 262092, 2) / 10;

            // Фокус 2 канала (*10 [почему - никто не помнит, для всех])
            FwConfig.FokusChannel2 = (double)ToBigEndian(firmware, 262094, 2) / 10;

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
            var fwName = new[] { FwName1, FwName2, FwName3, FwName4 };

            Array.Copy(fw, fwloaded, fwloaded.Length);

            for (int i = 0; i < 4; i++)
            {
                byte[] firmware = (byte[])Properties.Resources.ResourceManager.GetObject(fwName[i]);

                if(firmware == null) continue;
                Array.Copy(firmware, fwsource, fwsource.Length);

                if (fwsource.SequenceEqual(fwloaded))
                    return i;
            }
            return -1;
        }
        /// <summary>
        /// Установка полей пользовательского интерфейс в соответсвии с данными прошивки
        /// </summary>
        private void SetFirmwareSettings()
        {
            Dispatcher.Invoke(() =>
            {
                TbDeviceNumber.Text = FwConfig.Device.ToString();
                TbCoordXChannel1.Text = FwConfig.CoordXChannel1.ToString();
                TbCoordYChannel1.Text = FwConfig.CoordYChannel1.ToString();
                TbCoordXChannel2.Text = FwConfig.CoordXChannel2.ToString();
                TbCoordYChannel2.Text = FwConfig.CoordYChannel2.ToString();
                TbFocusChannel1.Text = FwConfig.FokusChannel1.ToString(CultureInfo.InvariantCulture);
                TbFocusChannel2.Text = FwConfig.FokusChannel2.ToString(CultureInfo.InvariantCulture);
                CbVersions.SelectedIndex = FwConfig.FirmwareVersion; 
            });
        }
        private void BtnProgrammVpd_Click(object sender, RoutedEventArgs e)
        {
            ProgrammVpd();
        }

        private async void ProgrammVpd()
        {
            if (!GetFirmwareSettings())
                return;
            // Отключаем кнопки прошить\считать для избежания ошибок
            SetButtonsDisable();

            await Task.Run(() =>
            {
                var firmware = PrepareFirmware();
                if (firmware == null) throw new Exception("В ходе подготовки прошивки произошла ошибка.");

                var cs = CountFirmwareControlSum(firmware, firmware.Length - 2);
                WriteFirmwareCheckSum(firmware, firmware.Length - 2, cs);

                var zerocs = CountFirmwareControlSum(firmware, firmware.Length);
                if (zerocs != 0) throw new Exception("Посчитанная контрольная сумма не равно 0.");
                WriteFirmwareCheckSum(firmware, firmware.Length, zerocs);

                // Сохраняем прошику перед записьшу в ВПУ
                SaveFinishedFirmware(firmware);

                if (!SendFirmWare(firmware))
                    return;

                // Первичная верификация
                var dwldfwRam = GetFirmwareFromOed(AdrRam);
                if (dwldfwRam == null) return;
                if (FirmwareVerification(firmware, dwldfwRam))
                {
                    // Отправить команду прошить ВПУ
                    WriteFirmwareToRom();

                    // Вторичная верификация
                    var dwldfwRom = GetFirmwareFromOed(AdrRom);
                    if (dwldfwRom == null) return;
                    if (FirmwareVerification(firmware, dwldfwRom))
                        Dispatcher.Invoke(() =>
                        {
                            MessageBox.Show(this, "ВПУ успешно прошито.", "Информация",
                                MessageBoxButton.OK, MessageBoxImage.Information);
                        });
                    else
                    {
                        Dispatcher.Invoke(() =>
                        {
                            MessageBox.Show(this, "Ошибка в ходе вторичной верификации. Не перезагружайте ВПУ!", "ВНИМАНИЕ",
                                MessageBoxButton.OK, MessageBoxImage.Warning);
                        });
                        return;
                    }
                }
                else
                {
                    MessageBox.Show(this, "Ошибка в ходе первичной верификации.", "ВНИМАНИЕ",
                        MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }
            });
            // Включаем кнопки считать\прошить
            SetButtonsEnable();
        }

        private void WriteFirmwareToRom()
        {
            var writefwtoRom = new byte[8];
            writefwtoRom[0] = 0x0c;
            writefwtoRom[2] = 0x0a;

            DoNotCloseConnectionTimer.Stop();

            try
            {
                _sender.Send(writefwtoRom, writefwtoRom.Length, _sendEndPoint);

                var responce = _receiver.Receive(ref _receiveEndPoint);
                if (writefwtoRom[0] != 0x0c || writefwtoRom[2] != 0x0a || writefwtoRom[3] == 0xff)
                {
                    Dispatcher.Invoke(() =>
                    {
                        MessageBox.Show(this, "STM вернул ошибку ОЭД", "Ошибка",
                            MessageBoxButton.OK, MessageBoxImage.Error);
                    });
                }
            }
            catch (SocketException)
            {
                Dispatcher.Invoke(() =>
                {
                    MessageBox.Show(this, "STM не ответила на команду. Timeout.", "Ошибка",
                        MessageBoxButton.OK, MessageBoxImage.Error);
                });
            }
            finally
            {
                DoNotCloseConnectionTimer.Start();
            }
        }

        private bool FirmwareVerification(byte[] sourcefirmware, byte[] loadedfirmware)
        {
            for (int i = 0; i < sourcefirmware.Length; i++)
            {
                if (sourcefirmware[i] != loadedfirmware[i])
                {
                    Dispatcher.Invoke(() =>
                    {
                        MessageBox.Show(this, "Контрольная сумма зашитой в ВПУ прошивки не совпаадет.", "Ошибка",
                            MessageBoxButton.OK, MessageBoxImage.Error);
                    });
                    return false;
                }
            }
            return true;
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
                MessageBox.Show(this, "Номер прибора может быть от 42 до 65535", "Ошибка", MessageBoxButton.OK,
                    MessageBoxImage.Error);
                return false;
            }
            FwConfig.Device = deviceNumber;

            byte coordXChannel1;
            var cxc1IsValid = byte.TryParse(TbCoordXChannel1.Text, out coordXChannel1);
            if (!cxc1IsValid)
            {
                MessageBox.Show(this, "Координата Х по каналу 1 может принимать значения от 0 до 255", "Ошибка",
                    MessageBoxButton.OK, MessageBoxImage.Error);
                return false;
            }
            FwConfig.CoordXChannel1 = coordXChannel1;

            byte coordYChannel1;
            var cyc1IsValid = byte.TryParse(TbCoordYChannel1.Text, out coordYChannel1);
            if (!cyc1IsValid)
            {
                MessageBox.Show(this, "Координата Y по каналу 1 может принимать значения от 0 до 255", "Ошибка",
                    MessageBoxButton.OK, MessageBoxImage.Error);
                return false;
            }
            FwConfig.CoordYChannel1 = coordYChannel1;

            byte coordXChannel2;
            var cxc2IsValid = byte.TryParse(TbCoordXChannel2.Text, out coordXChannel2);
            if (!cxc2IsValid)
            {
                MessageBox.Show(this, "Координата Х по каналу 2 может принимать значения от 0 до 255", "Ошибка",
                    MessageBoxButton.OK, MessageBoxImage.Error);
                return false;
            }
            FwConfig.CoordXChannel2 = coordXChannel2;

            byte coordYChannel2;
            var cyc2IsValid = byte.TryParse(TbCoordYChannel2.Text, out coordYChannel2);
            if (!cyc2IsValid)
            {
                MessageBox.Show(this, "Координата Y по каналу 2 может принимать значения от 0 до 255", "Ошибка",
                    MessageBoxButton.OK, MessageBoxImage.Error);
                return false;
            }
            FwConfig.CoordYChannel2 = coordYChannel2;

            double focusChannel1;
            double.TryParse(TbFocusChannel1.Text.Replace(".", ","), out focusChannel1);
            if (focusChannel1 < 49 || focusChannel1 > 55)
            {
                MessageBox.Show(this, "Фокус канала 1 может принимать значения от 49 до 55, с шагом 0,1", "Ошибка",
                    MessageBoxButton.OK, MessageBoxImage.Error);
                return false;
            }
            FwConfig.FokusChannel1 = focusChannel1;

            double focusChannel2;
            double.TryParse(TbFocusChannel2.Text.Replace(".", ","), out focusChannel2);
            if (focusChannel2 < 320 || focusChannel2 > 340)
            {
                MessageBox.Show(this, "Фокус канала 2 может принимать значения от 320 до 340, с шагом 0,1",
                    "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
                return false;
            }
            FwConfig.FokusChannel2 = focusChannel2;

            // Проверяем выбрана ли прошивка
            if (CbVersions.SelectedIndex == -1)
            {
                MessageBox.Show(this, "Не выбрана версия прошивки.", "Ошибка", MessageBoxButton.OK,
                    MessageBoxImage.Error);
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
            return ToBigEndian(fw, 262098, 2);
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
            //// Зануление, для правильного расчета контрольной суммы
            firmware[length - 1] = firmware[length - 2] = 0;

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

        private bool SendFirmWare(byte[] firmware)
        {
            try
            {
                #region Variables

                var comFwUpdate = new byte[72];
                comFwUpdate[0] = 0x0c;
                comFwUpdate[2] = 0x08;

                comFwUpdate[8] = 12;
                // Адресс записи
                comFwUpdate[9] = 0;
                comFwUpdate[10] = 0;
                comFwUpdate[11] = 0;
                comFwUpdate[12] = 0;
                // Размер блока данных
                comFwUpdate[13] = 58;

                int counter = 0;
                uint adress = AdrRam;

                // 14-71 данные прошивки
                // Устанавливаем размер прогресс бара в соответсвии с размером прошивки
                Dispatcher.Invoke(()=> { PbOperationStatus.Maximum = firmware.Length; });
                DoNotCloseConnectionTimer.Stop();
                #endregion

                while (counter < firmware.Length)
                {
                    // Адресс записи 
                    var adr = BitConverter.GetBytes(adress);
                    comFwUpdate[9] = adr[0];
                    comFwUpdate[10] = adr[1];
                    comFwUpdate[11] = adr[2];
                    comFwUpdate[12] = adr[3];

                    // Заносим 58 байт прошивки в комманду
                    int j = 14;
                    if (firmware.Length - counter >= 58)
                    {
                        for (int i = counter; i < counter + 58; i++)
                        {
                            comFwUpdate[j] = firmware[i];
                            j++;
                        }
                    }
                    else
                    {
                        for (int i = counter; i < counter + (firmware.Length - counter); i++)
                        {
                            comFwUpdate[j] = firmware[i];
                            j++;
                        }
                        for (int k = j; k < comFwUpdate.Length; k++)
                            comFwUpdate[k] = 0;
                    }

                    _sender.Send(comFwUpdate, comFwUpdate.Length, _sendEndPoint);

                    adress += 58;
                    counter += 58;

                    // Изменяем значение прогресс баар
                    var counter1 = counter;
                    Dispatcher.Invoke(() => { PbOperationStatus.Value = counter1; });

                    var responce = _receiver.Receive(ref _receiveEndPoint);
                    if (responce[0] != 0x0c || responce[2] != 0x08 || responce[4] == 0xff)
                    {
                        Dispatcher.Invoke(() =>
                        {
                            MessageBox.Show(this, "Ошибка загрузки файла прошивки в ОЭД. Операция отменена.", "ОШИБКА",
                                MessageBoxButton.OK, MessageBoxImage.Error);
                        });
                        return false;
                    } 
                }
                // После окончания загрузки зануляем прогресс бар
                Dispatcher.Invoke(() => { PbOperationStatus.Value = 0; });
            }
            catch (SocketException)
            {
                Dispatcher.Invoke(() =>
                {
                    MessageBox.Show(this, "Ошибка загрузки файла прошивки в ОЭД. Stm не отвечает.", "Ошибка",
                        MessageBoxButton.OK, MessageBoxImage.Error);
                });
                return false;
            }finally
            {
                DoNotCloseConnectionTimer.Start();
            }
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
