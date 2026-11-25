using System.Media;
using System.Text.Json.Nodes;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;

namespace NobatQeshm.Wpf;

public partial class MainWindow
{
    private List<CookieInfo> _monitorCookies = [];
    private int _monitorRevision;

    private void SavedPlateMonitorCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (SavedPlateMonitorCombo.SelectedItem is not SavedPlate p) return; MonitorTransferText.Text = p.TransferCode; MonitorPlateText.Text = p.Number; MonitorSeriesText.Text = p.Series;
    }
    private async void MonitorAccountCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (MonitorAccountCombo.SelectedItem is not SavedProfile profile) return;
        _monitorRevision++; var revision = _monitorRevision; _monitorCookies = []; _monitorServices.Clear(); AddMonitorButton.IsEnabled = false; RefreshMonitorServicesButton.IsEnabled = false;
        MonitorAccountStatusText.Text = "در حال بررسی نشست پایش ...";
        var cookies = await RunAsync("بررسی نشست پایش", async () =>
        {
            if (profile.Expired || JsonHelpers.IsExpired(profile.Expires)) throw new QeshmApiException("نشست انتخاب‌شده منقضی شده است.");
            var saved = _sessionStore.LoadCookies(profile.NationalCode); if (saved.Count == 0) throw new QeshmApiException("نشست انتخاب‌شده قابل بازیابی نیست.");
            using var client = new QeshmClient(logger: Log); client.ImportCookies(saved); var session = await client.GetSessionAsync();
            var user = QeshmClient.SessionUser(session); if (!QeshmClient.SessionIsValid(session) || user.Text("codeMelli") != profile.NationalCode) throw new QeshmApiException("نشست انتخاب‌شده معتبر نیست.");
            return client.ExportCookies();
        });
        if (cookies is null || revision != _monitorRevision) { MonitorAccountStatusText.Text = "نشست پایش معتبر نیست."; return; }
        _monitorCookies = cookies; RefreshMonitorServicesButton.IsEnabled = true; MonitorAccountStatusText.Text = "نشست پایش فعال است."; await LoadMonitorServicesAsync(revision);
        foreach (var item in _monitors.Where(x => x.Enabled)) { item.NextDue = DateTime.Now; item.Refresh(); }
    }
    private async void RefreshMonitorServices_Click(object sender, RoutedEventArgs e) => await LoadMonitorServicesAsync(_monitorRevision);
    private async Task LoadMonitorServicesAsync(int revision)
    {
        if (_monitorCookies.Count == 0) return; MonitorAccountStatusText.Text = "در حال دریافت خدمات پایش ..."; var snapshot = _monitorCookies.ToList();
        var rows = await RunAsync("دریافت خدمات پایش", async () => { using var client = new QeshmClient(logger: Log); client.ImportCookies(snapshot); return await client.GetReservationServiceTypesAsync(); });
        if (rows is null || revision != _monitorRevision) return; var selected = _monitorServices.Where(x => x.Selected).Select(x => x.Id).ToHashSet(); _monitorServices.Clear(); foreach (var item in rows) { item.Selected = selected.Contains(item.Id); _monitorServices.Add(item); }
        AddMonitorButton.IsEnabled = rows.Count > 0; MonitorAccountStatusText.Text = $"نشست پایش فعال است. {rows.Count} خدمت در دسترس است.";
    }
    private async void AddMonitor_Click(object sender, RoutedEventArgs e)
    {
        var transfer = JsonHelpers.NormalizeDigits(MonitorTransferText.Text); var plate = JsonHelpers.NormalizeDigits(MonitorPlateText.Text); var series = JsonHelpers.NormalizeDigits(MonitorSeriesText.Text);
        if (!transfer.All(char.IsDigit) || transfer.Length == 0 || !plate.All(char.IsDigit) || plate.Length == 0 || (series.Length > 0 && !series.All(char.IsDigit))) { Warn("کد شماره‌گذاری و شماره پلاک باید عددی و الزامی باشند؛ سری اختیاری و عددی است."); return; }
        if (!int.TryParse(JsonHelpers.NormalizeDigits(MonitorIntervalText.Text), out var interval) || interval is < 1 or > 1440) { Warn("فاصله بررسی باید بین ۱ تا ۱۴۴۰ دقیقه باشد."); return; }
        if (!int.TryParse(JsonHelpers.NormalizeDigits(MonitorAlarmCountText.Text), out var alarmCount) || alarmCount is < 1 or > 20) { Warn("تعداد پخش هشدار را بین ۱ و ۲۰ وارد کنید."); return; }
        var services = _monitorServices.Where(x => x.Selected).ToList(); if (services.Count == 0) { Warn("حداقل یک خدمت را برای پایش انتخاب کنید."); return; }
        var sound = (MonitorSoundCombo.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "اعلان ویندوز"; var created = new List<MonitorItem>();
        foreach (var service in services)
        {
            if (_monitors.Any(x => x.TransferCode == transfer && x.PlateNumber == plate && x.PlateSeries == series && x.ServiceId == service.Id)) continue;
            var item = new MonitorItem { TransferCode = transfer, PlateNumber = plate, PlateSeries = series, ServiceId = service.Id, ServiceName = service.Name, IntervalMinutes = interval, AlarmEnabled = MonitorAlarmCheck.IsChecked == true, AlarmCount = alarmCount, BalloonEnabled = MonitorBalloonCheck.IsChecked == true, AlarmSound = sound, NextDue = DateTime.Now };
            _monitors.Add(item); created.Add(item);
        }
        if (created.Count == 0) { MessageBox.Show(this, "همه خدمت‌های انتخاب‌شده قبلاً برای این پلاک اضافه شده‌اند.", "پایش"); return; }
        UpdateMonitorSummary(); foreach (var item in created) await CheckMonitorItemAsync(item);
    }
    private async void CheckMonitorNow_Click(object sender, RoutedEventArgs e) { if (MonitorGrid.SelectedItem is MonitorItem item && !item.Checking) { item.Enabled = true; await CheckMonitorItemAsync(item); } else Warn("یک مورد را از جدول انتخاب کنید."); }
    private void ToggleMonitor_Click(object sender, RoutedEventArgs e) { if (MonitorGrid.SelectedItem is not MonitorItem item) { Warn("یک مورد را انتخاب کنید."); return; } item.Enabled = !item.Enabled; if (item.Enabled) { item.NextDue = DateTime.Now; item.LastResult = "در صف بررسی"; } item.Refresh(); UpdateMonitorSummary(); }
    private void DeleteMonitor_Click(object sender, RoutedEventArgs e) { if (MonitorGrid.SelectedItem is not MonitorItem item) { Warn("یک مورد را انتخاب کنید."); return; } _monitors.Remove(item); UpdateMonitorSummary(); }
    private void PreviewSound_Click(object sender, RoutedEventArgs e) => PlayAlarm((MonitorSoundCombo.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "اعلان ویندوز", 1);
    private async void MonitorTimer_Tick(object? sender, EventArgs e)
    {
        if (_closing || _monitorCookies.Count == 0) return; var now = DateTime.Now;
        foreach (var item in _monitors.Where(x => x.Enabled && !x.Checking && x.NextDue is not null && x.NextDue <= now).ToList()) await CheckMonitorItemAsync(item);
    }
    private async Task CheckMonitorItemAsync(MonitorItem item)
    {
        if (_monitorCookies.Count == 0 || item.Checking || !item.Enabled || _closing) return; item.Checking = true; item.Refresh(); var revision = _monitorRevision; var snapshot = _monitorCookies.ToList();
        try
        {
            using var client = new QeshmClient(logger: Log); client.ImportCookies(snapshot); var response = await client.ReservationAvailabilityAsync(item.ServiceId, item.TransferCode, item.PlateNumber, item.PlateSeries);
            if (revision != _monitorRevision || !_monitors.Contains(item)) return; if (response.Int("status") == 401) throw new QeshmApiException("نشست پایش معتبر نیست.");
            var count = response.Array("sessions")?.Count ?? 0; item.TotalSlots = count; item.LastResult = count > 0 ? $"نوبت موجود است؛ {count} بازه" : "نوبت موجود نیست";
            if (count > 0)
            {
                if (item.AlarmEnabled) PlayAlarm(item.AlarmSound, item.AlarmCount);
                if (item.BalloonEnabled) ShowBalloon("نوبت آزاد پیدا شد", $"{item.ServiceName}: {count} بازه | پلاک {item.PlateNumber} / {(item.PlateSeries.Length == 0 ? "—" : item.PlateSeries)}");
            }
        }
        catch (Exception ex) { item.TotalSlots = 0; item.LastResult = "خطا: " + ex.Message; Log("خطا در پایش نوبت: " + ex.Message); }
        finally { item.Checking = false; item.LastCheck = DateTime.Now; item.NextDue = DateTime.Now.AddMinutes(item.IntervalMinutes); item.Refresh(); UpdateMonitorSummary(); }
    }
    private static void PlayAlarm(string name, int repeat)
    {
        if (name == "بدون صدا") return; var sound = name switch { "هشدار ویندوز" => SystemSounds.Exclamation, "صدای خطا" => SystemSounds.Hand, _ => SystemSounds.Asterisk };
        _ = Task.Run(async () => { for (var i = 0; i < Math.Max(1, repeat); i++) { sound.Play(); if (i + 1 < repeat) await Task.Delay(900); } });
    }
    private static void ShowBalloon(string title, string message)
    {
        Application.Current.Dispatcher.Invoke(() =>
        {
            var panel = new StackPanel { Margin = new Thickness(16) };
            panel.Children.Add(new TextBlock { Text = title, FontWeight = FontWeights.Bold, FontSize = 16, Foreground = Brushes.White });
            panel.Children.Add(new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 7, 0, 0), Foreground = new SolidColorBrush(Color.FromRgb(210, 226, 238)) });
            var notice = new Window { Width = 390, Height = 135, WindowStyle = WindowStyle.None, ResizeMode = ResizeMode.NoResize, ShowInTaskbar = false, Topmost = true, Background = new SolidColorBrush(Color.FromRgb(21, 56, 82)), Content = panel, FlowDirection = FlowDirection.RightToLeft };
            notice.Left = SystemParameters.WorkArea.Right - notice.Width - 16; notice.Top = SystemParameters.WorkArea.Bottom - notice.Height - 16; notice.Show();
            var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(10) }; timer.Tick += (_, _) => { timer.Stop(); notice.Close(); }; timer.Start();
        });
    }
    private void UpdateMonitorSummary()
    {
        var active = _monitors.Count(x => x.Enabled && _monitorCookies.Count > 0); var available = _monitors.Count(x => x.TotalSlots > 0); MonitorSummaryText.Text = _monitors.Count == 0 ? "هنوز موردی به پایش اضافه نشده است." : $"{_monitors.Count} مورد | {active} فعال | {available} مورد دارای نوبت";
    }
}
