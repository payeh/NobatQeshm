using System.Collections.ObjectModel;
using System.Net;
using System.Windows;

namespace NobatQeshm.Wpf;

public partial class ServiceAvailabilityWindow : Window
{
    private readonly QeshmClient _client;
    private readonly string _transferCode;
    private readonly string _plateNumber;
    private readonly string _series;
    private readonly ObservableCollection<ServiceCapacity> _rows = [];
    private readonly CancellationTokenSource _lifetime = new();
    private bool _isClosed;

    internal ServiceAvailabilityWindow(QeshmClient client, string transferCode, string plateNumber, string series)
    {
        _client = client;
        _transferCode = transferCode;
        _plateNumber = plateNumber;
        _series = series;
        InitializeComponent();
        ServiceGrid.ItemsSource = _rows;
        VehicleText.Text = $"پلاک {plateNumber}{(series.Length == 0 ? "" : $" / سری {series}")} | کد شماره‌گذاری {transferCode}";
    }

    private async void Window_Loaded(object sender, RoutedEventArgs e)
    {
        Loaded -= Window_Loaded;
        await RefreshAsync();
    }

    private async void Refresh_Click(object sender, RoutedEventArgs e) => await RefreshAsync();

    protected override void OnClosed(EventArgs e)
    {
        _isClosed = true;
        _lifetime.Cancel();
        _lifetime.Dispose();
        base.OnClosed(e);
    }

    private async Task RefreshAsync()
    {
        RefreshButton.IsEnabled = false;
        _rows.Clear();
        var token = _lifetime.Token;
        try
        {
            StatusText.Text = "در حال دریافت فهرست خدمات…";
            var services = await _client.GetReservationServiceTypesAsync(token);
            token.ThrowIfCancellationRequested();
            foreach (var service in services)
                _rows.Add(new(service.Name, "—", "در صف بررسی"));

            var total = 0;
            var checkedServices = 0;
            var failed = 0;
            for (var index = 0; index < services.Count; index++)
            {
                token.ThrowIfCancellationRequested();
                var service = services[index];
                StatusText.Text = $"در حال بررسی {index + 1} از {services.Count} خدمت";
                _rows[index] = new(service.Name, "—", "در حال بررسی");
                try
                {
                    var response = await _client.ReservationAvailabilityAsync(service.Id, _transferCode, _plateNumber, _series, token);
                    token.ThrowIfCancellationRequested();
                    var sessions = response.Array("sessions");
                    var status = response.Int("status");
                    if ((status != 0 && status != 200) || (status == 0 && sessions is null))
                        throw new QeshmApiException(response.TextOr("پاسخ سامانه برای این خدمت معتبر نیست.", "msg", "message"),
                            businessStatus: status);
                    var count = sessions?.Count ?? 0;
                    total += count;
                    checkedServices++;
                    _rows[index] = new(service.Name, count.ToString(), count > 0 ? "نوبت آزاد دارد" : "نوبت آزاد ندارد");
                }
                catch (QeshmApiException error) when (!token.IsCancellationRequested)
                {
                    _rows[index] = new(service.Name, "—", error.Message);
                    failed++;
                    if (error.HttpStatus is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden or HttpStatusCode.TooManyRequests
                        || error.BusinessStatus is 401 or 403 or 429)
                    {
                        for (var remaining = index + 1; remaining < services.Count; remaining++)
                            _rows[remaining] = new(services[remaining].Name, "—", "بررسی نشد");
                        StatusText.Text = error.Message;
                        return;
                    }
                }
            }
            StatusText.Text = $"{checkedServices} خدمت بررسی شد | مجموع بازه‌های آزاد: {total}"
                + (failed == 0 ? "" : $" | {failed} خطا");
        }
        catch (Exception) when (token.IsCancellationRequested) { }
        catch (QeshmApiException error)
        {
            StatusText.Text = error.Message;
        }
        finally { if (!_isClosed) RefreshButton.IsEnabled = true; }
    }

    private sealed record ServiceCapacity(string ServiceName, string Capacity, string Status);
}
