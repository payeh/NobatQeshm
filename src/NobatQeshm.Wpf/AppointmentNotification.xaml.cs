using System.Windows;
using System.Windows.Threading;

namespace NobatQeshm.Wpf;

public partial class AppointmentNotification : Window
{
    private readonly Action _open;
    private readonly DispatcherTimer _timer = new();

    public AppointmentNotification(string title, string message, string openLabel, Action open, string? serviceName)
    {
        InitializeComponent();
        NotificationTitle.Text = title;
        if (!string.IsNullOrWhiteSpace(serviceName))
        {
            NotificationService.Text = serviceName;
            NotificationService.Visibility = Visibility.Visible;
        }
        NotificationMessage.Text = message;
        OpenButton.Content = openLabel;
        _open = open;
        _timer.Interval = TimeSpan.FromSeconds(serviceName is null ? 9 : 18);
        _timer.Tick += (_, _) => Close();
        Closed += (_, _) => _timer.Stop();
    }

    internal void MeasureForWorkArea(Size available)
    {
        Width = Math.Min(390, available.Width);
        var surface = (FrameworkElement)Content;
        surface.Measure(new Size(Width, available.Height));
        Height = Math.Ceiling(surface.DesiredSize.Height);
    }

    private void Notification_Loaded(object sender, RoutedEventArgs e) => _timer.Start();

    private void Dismiss_Click(object sender, RoutedEventArgs e) => Close();

    private void Open_Click(object sender, RoutedEventArgs e)
    {
        Close();
        _open();
    }
}
