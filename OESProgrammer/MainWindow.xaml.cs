using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Windows;
using System.Xml;
using System.Xml.Linq;

namespace OESProgrammer
{
    /// <summary>
    /// Логика взаимодействия для MainWindow.xaml
    /// </summary>
    public partial class MainWindow
    {
        private VpdProgrammingWindow _vpdWindow;
        private CapProgrammingWindow _capWindow;
        private static string _remoteIp;
        public MainWindow()
        {     
            // Загружаем файл локализации и IP-адреса STM
            LoadConfiguration();
            // Инициализация компонентов формы
            InitializeComponent();
        }

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            LbVersion.Content = "Версия ПО: " + Assembly.GetExecutingAssembly().GetName().Version;
        }
        private void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            // Обновление файла конфигурации
            UpdateConfiguration();
        }
        #region LanguageConfiguration

        private void BtnLangRus_Click(object sender, RoutedEventArgs e)
        {
            Thread.CurrentThread.CurrentUICulture = new CultureInfo("ru-RU");
            InitializeComponent();
        }

        private void BtnLangFr_Click(object sender, RoutedEventArgs e)
        {
            Thread.CurrentThread.CurrentUICulture = new CultureInfo("fr-FR");
            InitializeComponent();
        }

        private void BtnLangEng_Click(object sender, RoutedEventArgs e)
        {
            Thread.CurrentThread.CurrentUICulture = new CultureInfo("en-US");
            InitializeComponent();
        }
        private static void LoadConfiguration()
        {
            if (File.Exists("setting.xml"))
            {
                XDocument xDoc = XDocument.Load("setting.xml");
                XElement xLang = xDoc.Descendants("Language").First();
                ChooseLanguage(xLang.Value);
                // Установка ClientIP
                XElement xClientIp = xDoc.Descendants("ClientIP").First();
                _remoteIp = xClientIp.Value;
                if (_remoteIp == string.Empty) _remoteIp = "192.168.0.100";
            }
            else
            {
                ChooseLanguage(CultureInfo.CurrentCulture.Name);
                _remoteIp = "192.168.0.100";
            }
        }
        //Выбор текущего языка
        private static void ChooseLanguage(string lang)
        {
            switch (lang)
            {
                case "ru-RU":
                    Thread.CurrentThread.CurrentUICulture = new CultureInfo("ru-RU");
                    break;
                case "ru-BY":
                    Thread.CurrentThread.CurrentUICulture = new CultureInfo("ru-RU");
                    break;
                case "en-US":
                    Thread.CurrentThread.CurrentUICulture = new CultureInfo("en-US");
                    break;
                case "fr-FR":
                    Thread.CurrentThread.CurrentUICulture = new CultureInfo("fr-FR");
                    break;
                default:
                    Thread.CurrentThread.CurrentUICulture = new CultureInfo("ru-BY");
                    break;
            }
        }
        // Обновление файла конфигурации
        private static void UpdateConfiguration()
        {
            XmlTextWriter writer = new XmlTextWriter("setting.xml", Encoding.UTF8);
            writer.WriteStartDocument();
            writer.WriteStartElement("xml");
            writer.WriteEndElement();
            writer.Close();

            XmlDocument doc = new XmlDocument();
            doc.Load("setting.xml");
            XmlNode element = doc.CreateElement("OESDataDownloader");
            doc.DocumentElement?.AppendChild(element);

            XmlNode languageEl = doc.CreateElement("Language"); // даём имя
            languageEl.InnerText = Thread.CurrentThread.CurrentUICulture.ToString(); // и значение
            element.AppendChild(languageEl); // и указываем кому принадлежит

            XmlNode clientIpEl = doc.CreateElement("ClientIP"); // даём имя
            clientIpEl.InnerText = _remoteIp; // и значение
            element.AppendChild(clientIpEl); // и указываем кому принадлежит

            doc.Save("setting.xml");
        }

        #endregion
        // Открыть форму программирования ВПУ
        private void BtnProgrammingVpd_Click(object sender, RoutedEventArgs e)
        {
            if (_vpdWindow == null)
            {
                _vpdWindow = new VpdProgrammingWindow(_remoteIp){Owner = this};
                _vpdWindow.ShowDialog();
            }
            else _vpdWindow.ShowDialog();
        }
        // Открыть форму программирования крышки
        private void BtnProgrammingCap_Click(object sender, RoutedEventArgs e)
        {
            if (_capWindow == null)
            {
                _capWindow = new CapProgrammingWindow(_remoteIp) {Owner = this};
                _capWindow.Show();
            }
            else _capWindow.ShowDialog();
        }
    }
}
