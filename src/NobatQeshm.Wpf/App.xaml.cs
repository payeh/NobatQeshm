using System.Threading;
using System.Windows;

namespace NobatQeshm.Wpf;

public partial class App : Application
{
    private Mutex? _instanceMutex;
    private bool _ownsMutex;

    protected override void OnStartup(StartupEventArgs e)
    {
        _instanceMutex = new Mutex(false, @"Local\NobatQeshm.Wpf.29D9F549-1E49-49AC-91E0-A81905F9227F");
        try { _ownsMutex = _instanceMutex.WaitOne(0); }
        catch (AbandonedMutexException) { _ownsMutex = true; }
        if (!_ownsMutex)
        {
            MessageBox.Show("برنامه در حال اجراست. از آیکون نوبت قشم در سینی ویندوز آن را باز کنید.", "سامانه نوبت قشم",
                MessageBoxButton.OK, MessageBoxImage.Information, MessageBoxResult.OK,
                MessageBoxOptions.RightAlign | MessageBoxOptions.RtlReading);
            Shutdown();
            return;
        }

        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
            MessageBox.Show(args.ExceptionObject?.ToString() ?? "خطای ناشناخته", "خطای برنامه", MessageBoxButton.OK, MessageBoxImage.Error);
        base.OnStartup(e);
        var window = new MainWindow();
        MainWindow = window;
        SessionEnding += (_, _) => window.AllowSessionShutdown();
        window.Show();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        if (_ownsMutex) _instanceMutex?.ReleaseMutex();
        _instanceMutex?.Dispose();
        base.OnExit(e);
    }
}

