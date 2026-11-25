using System.ComponentModel;
using System.Windows;
using Forms = System.Windows.Forms;

namespace NobatQeshm.Wpf;

public partial class MainWindow
{
    private Forms.NotifyIcon? _trayIcon;
    private WindowState _lastVisibleState = WindowState.Normal;
    private bool _allowWindowClose;
    private readonly Queue<(string Title, string Message, string OpenLabel, Action Open)> _notifications = new();
    private readonly List<AppointmentNotification> _visibleNotifications = [];

    private void Window_SourceInitialized(object? sender, EventArgs e)
    {
        var menu = new Forms.ContextMenuStrip { RightToLeft = Forms.RightToLeft.Yes };
        menu.Items.Add("باز کردن سامانه نوبت قشم", null, (_, _) => Dispatcher.BeginInvoke(RestoreFromTray));
        menu.Items.Add("پایش نوبت", null, (_, _) => Dispatcher.BeginInvoke(OpenMonitoring));
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add("خروج از برنامه", null, (_, _) => Dispatcher.BeginInvoke(ExitFromTray));

        var resource = Application.GetResourceStream(new Uri("pack://application:,,,/NobatQeshm.Wpf;component/Assets/icon.ico"));
        using var stream = resource.Stream;
        using var icon = new System.Drawing.Icon(stream);
        _trayIcon = new Forms.NotifyIcon
        {
            Icon = (System.Drawing.Icon)icon.Clone(),
            Text = "سامانه نوبت قشم",
            ContextMenuStrip = menu,
            Visible = true
        };
        _trayIcon.MouseClick += (_, eventArgs) =>
        {
            if (eventArgs.Button == Forms.MouseButtons.Left) Dispatcher.BeginInvoke(RestoreFromTray);
        };
    }

    private void Window_StateChanged(object? sender, EventArgs e)
    {
        if (WindowState == WindowState.Minimized)
            HideToTray();
        else
            _lastVisibleState = WindowState;
    }

    private void HideToTray()
    {
        if (_closing || _trayIcon is null || !IsVisible) return;
        _logWindow.Hide();
        Hide();
        ShowNotification("من اینجام، نگران نباش", "پایش نوبت در سینی ویندوز ادامه دارد.", "باز کردن برنامه", RestoreFromTray);
    }

    private void RestoreFromTray()
    {
        if (_closing || _activationLocked) return;
        if (!IsVisible) Show();
        if (WindowState == WindowState.Minimized) WindowState = _lastVisibleState;
        Activate();
    }

    private void OpenMonitoring()
    {
        if (_closing || _activationLocked) return;
        RestoreFromTray();
        MainTabs.SelectedItem = MonitorTab;
    }

    internal void AllowSessionShutdown() => _allowWindowClose = true;

    private void ExitFromTray()
    {
        _allowWindowClose = true;
        Close();
    }

    private void Window_Closing(object? sender, CancelEventArgs e)
    {
        if (!_allowWindowClose && _trayIcon is not null)
        {
            e.Cancel = true;
            HideToTray();
            return;
        }

        _closing = true;
        _monitorTimer.Stop();
        _alarmCancellation.Cancel();
        _notifications.Clear();
        foreach (var notification in _visibleNotifications.ToArray()) notification.Close();
        _logWindow.Close();
        _receiptPreview?.Dispose();
        _client.Dispose();
        if (_trayIcon is null) return;
        _trayIcon.Visible = false;
        _trayIcon.ContextMenuStrip?.Dispose();
        var icon = _trayIcon.Icon;
        _trayIcon.Dispose();
        icon?.Dispose();
        _trayIcon = null;
    }

    private void ShowMonitorNotification(string title, string message) =>
        ShowNotification(title, message, "باز کردن پایش", OpenMonitoring);

    private void ShowNotification(string title, string message, string openLabel, Action open)
    {
        if (_closing || _activationLocked) return;
        _notifications.Enqueue((title, message, openLabel, open));
        ArrangeMonitorNotifications();
    }

    private void ArrangeMonitorNotifications()
    {
        if (_closing || _activationLocked) return;
        const double margin = 12;
        const double gap = 7;
        var workArea = SystemParameters.WorkArea;
        var bottom = workArea.Bottom - margin;
        foreach (var popup in _visibleNotifications)
        {
            popup.Left = workArea.Right - margin - popup.Width;
            popup.Top = bottom - popup.Height;
            bottom = popup.Top - gap;
        }

        while (_notifications.TryPeek(out var next) && bottom > workArea.Top + margin)
        {
            var popup = new AppointmentNotification(next.Title, next.Message, next.OpenLabel, next.Open);
            popup.MeasureForWorkArea(new Size(workArea.Width - margin * 2, workArea.Height - margin * 2));
            var top = bottom - popup.Height;
            if (top < workArea.Top + margin)
            {
                popup.Close();
                break;
            }

            _notifications.Dequeue();
            popup.Left = workArea.Right - margin - popup.Width;
            popup.Top = top;
            bottom = top - gap;
            _visibleNotifications.Add(popup);
            popup.Closed += (_, _) =>
            {
                _visibleNotifications.Remove(popup);
                if (!_closing) Dispatcher.BeginInvoke(ArrangeMonitorNotifications);
            };
            popup.Show();
        }
    }
}
