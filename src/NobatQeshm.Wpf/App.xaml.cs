using System.Windows;

namespace NobatQeshm.Wpf;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
            MessageBox.Show(args.ExceptionObject?.ToString() ?? "خطای ناشناخته", "خطای برنامه", MessageBoxButton.OK, MessageBoxImage.Error);
        base.OnStartup(e);
    }
}

