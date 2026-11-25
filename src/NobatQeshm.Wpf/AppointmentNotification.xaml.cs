using System.Windows;
using System.Windows.Threading;

namespace NobatQeshm.Wpf;

public partial class AppointmentNotification : Window
{
    private readonly Action _open;
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(9) };

    public AppointmentNotification(string title, string message, Action open)
    {
        InitializeComponent();
        NotificationTitle.Text = title;
        NotificationMessage.Text = message;
        _open = open;
        var workArea = SystemParameters.WorkArea;
        Left = workArea.Right - Width - 12;
        Top = workArea.Bottom - 215;
        _timer.Tick += (_, _) => Close();
        Closed += (_, _) => _timer.Stop();
    }

    private void Notification_Loaded(object sender, RoutedEventArgs e)
    {
        Top = Math.Max(SystemParameters.WorkArea.Top, SystemParameters.WorkArea.Bottom - ActualHeight - 12);
        _timer.Start();
    }

    private void Dismiss_Click(object sender, RoutedEventArgs e) => Close();

    private void Open_Click(object sender, RoutedEventArgs e)
    {
        Close();
        _open();
    }
}
