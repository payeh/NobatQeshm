using System.Threading;
using System.Windows;
using System.Windows.Threading;

namespace NobatQeshm.Wpf;

public partial class App : Application
{
    private Mutex? _instanceMutex;
    private bool _ownsMutex;
    private readonly DispatcherTimer _activationTimer = new() { Interval = TimeSpan.FromSeconds(15) };

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

        ShutdownMode = ShutdownMode.OnExplicitShutdown;
        var activation = new ActivationService();
        var expiresAt = activation.ActiveUntilUtc();
        if (expiresAt is null && new ActivationWindow(activation).ShowDialog() == true)
            expiresAt = activation.ActiveUntilUtc();
        if (expiresAt is null)
        {
            Shutdown();
            return;
        }

        var window = new MainWindow();
        MainWindow = window;
        SessionEnding += (_, _) => window.AllowSessionShutdown();
        ShutdownMode = ShutdownMode.OnMainWindowClose;
        window.Show();
        _activationTimer.Tick += (_, _) =>
        {
            if (expiresAt > DateTimeOffset.UtcNow) return;
            _activationTimer.Stop();
            window.SetActivationLocked(true);
            var dialog = new ActivationWindow(activation);
            if (window.IsVisible)
            {
                dialog.Owner = window;
                dialog.WindowStartupLocation = WindowStartupLocation.CenterOwner;
            }
            if (dialog.ShowDialog() == true) expiresAt = activation.ActiveUntilUtc();
            if (expiresAt is not { } renewed || renewed <= DateTimeOffset.UtcNow)
            {
                window.AllowSessionShutdown();
                Shutdown();
                return;
            }
            window.SetActivationLocked(false);
            _activationTimer.Start();
        };
        _activationTimer.Start();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _activationTimer.Stop();
        if (_ownsMutex) _instanceMutex?.ReleaseMutex();
        _instanceMutex?.Dispose();
        base.OnExit(e);
    }
}

