using System;
using System.IO;
using System.Windows;
using System.Windows.Threading;
using AIOrchestrator.Models;
using AIOrchestrator.Views;

namespace AIOrchestrator
{
    public partial class App : Application
    {
        /// <summary>Tài khoản đang đăng nhập; null khi chưa xác thực.</summary>
        public static UserAccount? CurrentUser { get; private set; }

        protected override void OnStartup(StartupEventArgs e)
        {
            DispatcherUnhandledException += App_DispatcherUnhandledException;
            AppDomain.CurrentDomain.UnhandledException += CurrentDomain_UnhandledException;
            base.OnStartup(e);

            ShutdownMode = ShutdownMode.OnExplicitShutdown;
            var login = new LoginWindow();
            if (login.ShowDialog() != true || login.AuthenticatedUser == null)
            {
                Shutdown();
                return;
            }

            var main = new MainWindow(login.AuthenticatedUser);
            MainWindow = main;
            ShutdownMode = ShutdownMode.OnMainWindowClose;
            main.Show();
        }

        private void App_DispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
        {
            File.WriteAllText("app_error.log", $"Dispatcher Exception: {e.Exception}");
            MessageBox.Show($"Lỗi ứng dụng: {e.Exception.Message}\n{e.Exception.StackTrace}", "Lỗi khởi động", MessageBoxButton.OK, MessageBoxImage.Error);
        }

        private void CurrentDomain_UnhandledException(object sender, UnhandledExceptionEventArgs e)
        {
            File.WriteAllText("app_error.log", $"Domain Exception: {e.ExceptionObject}");
        }
    }
}
