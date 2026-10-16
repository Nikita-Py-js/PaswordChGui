using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;

namespace PaswordChGui
{
    public partial class App : Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            // Ловим необработанные исключения, чтобы не вылетало с диалогом.
            DispatcherUnhandledException += (s, args) =>
            {
                MessageBox.Show(
                    "Произошла ошибка:\n\n" + args.Exception.Message,
                    "PaswordCh GUI",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                args.Handled = true;
            };

            TaskScheduler.UnobservedTaskException += (s, args) =>
            {
                args.SetObserved();
            };
        }
    }
}