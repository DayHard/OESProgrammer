﻿using System.Threading;
using System.Windows;

namespace OESProgrammer
{
    /// <summary>
    /// Логика взаимодействия для App.xaml
    /// </summary>
    public partial class App
    {
        private static Mutex _instanceCheckMutex;
        private void Application_Startup(object sender, StartupEventArgs e)
        {
            if (InstanceCheck()) return;
            MessageBox.Show("Программа уже запущена!", "Ошибка", MessageBoxButton.OK);
            System.Environment.Exit(1);
        }
        private static bool InstanceCheck()
        {
            // "OESProgrammer" - уникальное имя мьютекс в перелах приложения
            _instanceCheckMutex = new Mutex(true, "OESProgrammer", out bool isNew);
            return isNew;
        }
    }
}
