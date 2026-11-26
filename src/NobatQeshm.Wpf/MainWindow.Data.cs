using Microsoft.Win32;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Text.Json.Nodes;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace NobatQeshm.Wpf;

public partial class MainWindow
{
    private bool _confirmingReservation;
    private JsonObject? _bookingSession;
    private string _availabilitySummary = "";

    private async void VerifyOtp_Click(object sender, RoutedEventArgs e) => await VerifyLoginAsync();

    private async void LoadServices_Click(object sender, RoutedEventArgs e) => await LoadServicesAsync();
    private async Task LoadServicesAsync()
    {
        ServicesStatusText.Text = "در حال دریافت نوع خدمت‌ها از سایت ...";
        var rows = await RunAsync("دریافت نوع خدمت‌ها", () => _client.GetReservationServiceTypesAsync());
        if (rows is null) { ServicesStatusText.Text = "دریافت خدمات ناموفق بود"; return; }
        ServiceCombo.ItemsSource = rows; if (rows.Count > 0) ServiceCombo.SelectedIndex = 0; ServicesStatusText.Text = $"{rows.Count} نوع خدمت فعال دریافت شد";
    }
    private void SavedPlateBookingCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (SavedPlateBookingCombo.SelectedItem is not SavedPlate p) return; TransferCodeText.Text = p.TransferCode; ReservationPlateText.Text = p.Number; ReservationSeriesText.Text = p.Series;
    }
    private void BookingInput_Changed(object sender, EventArgs e)
    {
        if (_bookingSearch is not null)
            ResetBookingSearch("برای اطلاعات جدید، نوبت‌های آزاد را بررسی کنید.");
    }

    private void ResetBookingSearch(string message)
    {
        _bookingSearch = null;
        _bookingSession = null;
        _availabilitySummary = "";
        _available.Clear();
        VehicleInfoText.Text = "";
        AvailabilityStatusText.Text = message;
        AvailabilityStatusText.Foreground = (Brush)FindResource("MutedBrush");
        AvailabilityStatusBorder.Background = Brushes.WhiteSmoke;
        UpdateConfirmReservationButton();
    }

    private void UpdateBookingAccountMessage()
    {
        if (_bookingSession is null) return;
        if (!ReferenceEquals(_bookingSession, _activeSession))
        {
            ResetBookingSearch("حساب کاربری تغییر کرد. نوبت‌های آزاد را دوباره بررسی کنید.");
            return;
        }
        if (_availabilitySummary.Length == 0) return;

        var user = QeshmClient.SessionUser(_bookingSession);
        var code = JsonHelpers.NormalizeDigits(user.TextOr(_activeSavedCode, "codeMelli", "MelliCode"));
        var name = FirstPersonName(_sessionStore.Get(code)?.Name, user.Text("name", "fullName"));
        var account = name.Length > 0 ? $"«{name}»" : "واردشده";
        AvailabilityStatusText.Text = _available.Count == 0
            ? _availabilitySummary
            : $"{_availabilitySummary} این نوبت با حساب کاربری {account} گرفته می‌شود.";
    }

    private async void CheckAvailability_Click(object sender, RoutedEventArgs e)
    {
        if (ServiceCombo.SelectedItem is not ServiceOption service) { Warn("ابتدا نوع خدمت‌ها را به‌روزرسانی کنید."); return; }
        if (!QeshmClient.SessionIsValid(_activeSession)) { Warn("برای بررسی نوبت‌های آزاد وارد حساب کاربری شوید."); return; }
        var transfer = JsonHelpers.NormalizeDigits(TransferCodeText.Text); var plate = JsonHelpers.NormalizeDigits(ReservationPlateText.Text); var series = JsonHelpers.NormalizeDigits(ReservationSeriesText.Text);
        if (transfer.Length == 0 || plate.Length == 0) { Warn("کد شماره‌گذاری و شماره پلاک الزامی است."); return; }
        var search = new JsonObject { ["sessionTypeId"] = service.Id, ["transferCode"] = transfer, ["plkNumber"] = plate, ["plkSeriesNumber"] = series };
        ResetBookingSearch("در حال بررسی نوبت‌های آزاد…");
        _bookingSearch = search;
        _bookingSession = _activeSession;
        var data = await RunAsync("بررسی اطلاعات خودرو و نوبت‌های آزاد", () => _client.ReservationAvailabilityAsync(service.Id, transfer, plate, series));
        if (!ReferenceEquals(_bookingSearch, search) || !ReferenceEquals(_bookingSession, _activeSession)) return;
        if (data is null || data.Int("status") == 401)
        {
            ResetBookingSearch("بررسی نوبت‌های آزاد انجام نشد. وضعیت ورود و ارتباط را بررسی کنید.");
            return;
        }
        var vehicle = data.Object("vehicleInfo"); VehicleInfoText.Text = vehicle is null ? "" : $"خودرو: {vehicle.Text("type")} | کد: {vehicle.Text("transferCode")} | پلاک: {vehicle.Text("plkNumber")} / سری {vehicle.Text("plkSeriesNumber")}";
        var sessions = data.Array("sessions") ?? []; var i = 0; foreach (var row in sessions.OfType<JsonObject>()) _available.Add(new() { Index = ++i, Source = row.CloneObject() });
        _availabilitySummary = _available.Count == 0
            ? $"برای «{service.Name}» نوبتی جهت نمایش یافت نشد."
            : $"{_available.Count} نوبت {service.Name} جهت نمایش یافت شد.";
        UpdateBookingAccountMessage();
        AvailabilityStatusText.Foreground = _available.Count == 0 ? Brushes.DarkGoldenrod : Brushes.SeaGreen; AvailabilityStatusBorder.Background = new SolidColorBrush(_available.Count == 0 ? Color.FromRgb(255, 251, 235) : Color.FromRgb(236, 253, 245));
    }
    private void AvailableSessionsGrid_SelectionChanged(object sender, SelectionChangedEventArgs e) => UpdateConfirmReservationButton();

    private void UpdateConfirmReservationButton() =>
        ConfirmReservationButton.IsEnabled = !_confirmingReservation && _bookingSearch is not null
            && ReferenceEquals(_bookingSession, _activeSession)
            && AvailableSessionsGrid.SelectedItem is AvailableSessionRow;

    private async void ConfirmReservation_Click(object sender, RoutedEventArgs e)
    {
        if (_confirmingReservation) return;
        if (AvailableSessionsGrid.SelectedItem is not AvailableSessionRow selected || _bookingSearch is not { } search
            || !ReferenceEquals(_bookingSession, _activeSession))
        {
            Warn("یک نوبت آزاد را انتخاب کنید.");
            return;
        }
        if (!QeshmClient.SessionIsValid(_activeSession))
        {
            Warn("برای ثبت نوبت ابتدا وارد حساب شخص شوید.");
            return;
        }

        var serviceName = (ServiceCombo.SelectedItem as ServiceOption)?.Name ?? "";
        var user = QeshmClient.SessionUser(_activeSession);
        var expectedUserId = user.Text("userId");
        var expectedCode = user.TextOr(_activeSavedCode, "codeMelli", "MelliCode");
        var cookies = _client.ExportCookies();
        _confirmingReservation = true;
        UpdateConfirmReservationButton();
        try
        {
            using var bookingClient = new QeshmClient(_client.BaseUrl, Log);
            bookingClient.ImportCookies(cookies);
            var booking = await RunAsync("ثبت نهایی نوبت", async () =>
            {
                var identity = await ResolveBookingIdentityAsync(bookingClient, expectedUserId, expectedCode);
                var response = await bookingClient.ConfirmReservationAsync(search, selected.Source);
                return new { Response = response, identity.Name, identity.NationalCode };
            });
            if (booking is null) return;
            var data = booking.Response;
            if (data.Int("status") != 200)
            {
                MessageBox.Show(this, data.ToJsonString(JsonHelpers.Options), "پاسخ ثبت نوبت");
                return;
            }

            if (data.Text("sessionTypeFa").Length == 0) data["sessionTypeFa"] = serviceName;
            if (ReferenceEquals(_bookingSearch, search)) _available.Remove(selected);
            _lastReceipt = data;
            string message;
            MessageBoxImage severity;
            try
            {
                var path = ReceiptService.SaveToDesktop(data, booking.Name, booking.NationalCode, _advertising);
                message = $"رسید روی دسکتاپ ذخیره شد.\n{Path.GetFileName(path)}";
                severity = MessageBoxImage.Information;
            }
            catch (Exception ex)
            {
                Log("ذخیرهٔ خودکار رسید: " + ex.Message);
                message = "ذخیرهٔ خودکار روی دسکتاپ انجام نشد. رسید از تب «آخرین رسید» قابل ذخیره است.";
                severity = MessageBoxImage.Warning;
            }

            ReservationTabs.SelectedItem = ReceiptTab;
            RenderReceipt();
            MessageBox.Show(this, $"نوبت ثبت شد. کد نهایی رزرو {data.Text("code", "reservationCode")}\n{message}",
                "ثبت نوبت", MessageBoxButton.OK, severity);
            await LoadReservationsAsync();
        }
        finally
        {
            _confirmingReservation = false;
            UpdateConfirmReservationButton();
        }
    }

    private async Task<(string Name, string NationalCode)> ResolveBookingIdentityAsync(
        QeshmClient bookingClient, string expectedUserId, string expectedCode)
    {
        var user = QeshmClient.SessionUser(await bookingClient.GetSessionAsync());
        if (user.Text("userId") != expectedUserId)
            throw new QeshmApiException("نشست حساب تغییر کرده است. دوباره وارد حساب موردنظر شوید.");

        var sessionCode = JsonHelpers.NormalizeDigits(user.Text("codeMelli", "MelliCode"));
        expectedCode = JsonHelpers.NormalizeDigits(expectedCode);
        if (sessionCode.Length > 0 && expectedCode.Length > 0 && sessionCode != expectedCode)
            throw new QeshmApiException("کد ملی نشست با حساب انتخاب‌شده مطابقت ندارد.");

        var code = sessionCode.Length > 0 ? sessionCode : expectedCode;
        var name = FirstPersonName(_sessionStore.Get(code)?.Name, user.Text("name", "fullName"));
        if (code.Length == 0 || name.Length == 0)
        {
            var person = QeshmClient.ExtractPerson(await bookingClient.GetProfileAsync());
            var personCode = JsonHelpers.NormalizeDigits(person.Text("MelliCode", "codeMelli"));
            if (personCode.Length > 0 && code.Length > 0 && personCode != code)
                throw new QeshmApiException("کد ملی مشخصات کاربر با نشست ورود مطابقت ندارد.");
            if (code.Length == 0) code = personCode;
            name = FirstPersonName($"{person.Text("FirstName")} {person.Text("LastName")}", person.Text("name", "fullName"), name);
        }

        if (name.Length == 0 || code.Length == 0 || !code.All(char.IsAsciiDigit))
            throw new QeshmApiException("برای ثبت نوبت و نام‌گذاری رسید، نام و کد ملی حساب باید در سامانه کامل باشد.");
        return (name, code);
    }

    private static string FirstPersonName(params string?[] candidates) =>
        candidates.Select(value => value?.Trim() ?? "")
            .FirstOrDefault(value => value is not ("کاربر" or "نام نامشخص") && value.Any(char.IsLetter)) ?? "";

    private async void LoadReservations_Click(object sender, RoutedEventArgs e) => await LoadReservationsAsync();
    private async Task LoadReservationsAsync()
    {
        var data = await RunAsync("دریافت لیست نوبت‌ها", () => _client.ListReservationsAsync(), false); if (data is null) return;
        _reservations.Clear(); foreach (var row in (data.Array("list") ?? []).OfType<JsonObject>()) _reservations.Add(new() { Source = row.CloneObject() }); ReservationCountText.Text = $"{_reservations.Count} نوبت";
    }
    private void ReservationCards_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        var columns = e.NewSize.Width >= 640 ? 2 : 1;
        if (!Equals(ReservationCards.Tag, columns)) ReservationCards.Tag = columns;
    }

    private void CopyReservationCode_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not ReservationRow { HasCode: true } row) return;
        Clipboard.SetText(row.Code);
        SetStatus("کد رزرو کپی شد.");
    }

    private async void LoadPlates_Click(object sender, RoutedEventArgs e) => await LoadPlatesAsync();
    private async Task LoadPlatesAsync()
    {
        var data = await RunAsync($"دریافت پلاک‌ها - صفحه {_platePage}", () => _client.ListPlatesAsync(_platePage), false); if (data is null) return; var body = data.Object("data") ?? data;
        _platePages = Math.Max(1, body.Int("totalPages", 1)); _plateCanAdd = body.Bool("canAdd"); PlatePageText.Text = $"صفحه {_platePage} از {_platePages}"; PlateAddInfoText.Text = _plateCanAdd ? "امکان افزودن پلاک فعال است" : "افزودن پلاک غیرفعال است. " + body.Text("canAddMsg"); AddPlateButton.IsEnabled = _plateCanAdd;
        _plates.Clear(); foreach (var row in (body.Array("list") ?? []).OfType<JsonObject>()) _plates.Add(new() { Source = row.CloneObject() });
    }
    private async void PreviousPlatePage_Click(object sender, RoutedEventArgs e) { if (_platePage > 1) { _platePage--; await LoadPlatesAsync(); } }
    private async void NextPlatePage_Click(object sender, RoutedEventArgs e) { if (_platePage < _platePages) { _platePage++; await LoadPlatesAsync(); } }
    private async void AddPlate_Click(object sender, RoutedEventArgs e) { var dialog = new PlateEditorWindow { Owner = this }; if (dialog.ShowDialog() != true) return; var result = await RunAsync("ثبت پلاک", () => _client.CreatePlateAsync(dialog.Part1, dialog.Part2, dialog.Part3, dialog.Part4, dialog.FrontImage, dialog.BackImage)); if (result is not null) { MessageBox.Show(this, result.TextOr("عملیات با موفقیت انجام شد.", "msg", "message")); await LoadPlatesAsync(); } }
    private async void EditPlate_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not PlateRow row || row.StatusId is not (2 or 3)) { Warn("این پلاک در وضعیت فعلی قابل ویرایش نیست."); return; } var dialog = new PlateEditorWindow(new JsonObjectAdapter(row.Source)) { Owner = this }; if (dialog.ShowDialog() != true) return;
        var result = await RunAsync("ویرایش پلاک", () => _client.UpdatePlateAsync(row.Source, dialog.Part1, dialog.Part2, dialog.Part3, dialog.Part4, dialog.FrontImage, dialog.BackImage)); if (result is not null) await LoadPlatesAsync();
    }
    private async void DeletePlate_Click(object sender, RoutedEventArgs e) { if ((sender as FrameworkElement)?.DataContext is not PlateRow row || MessageBox.Show(this, $"پلاک {row.Plate} حذف شود؟", "حذف پلاک", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return; if (await RunAsync("حذف پلاک", () => _client.DeletePlateAsync(row.Source)) is not null) await LoadPlatesAsync(); }
    private async void ViewFrontPlateImage_Click(object sender, RoutedEventArgs e) { if ((sender as FrameworkElement)?.DataContext is PlateRow row) await ViewImageAsync(row.FrontImage, "تصویر روی کارت خودرو"); }
    private async void ViewBackPlateImage_Click(object sender, RoutedEventArgs e) { if ((sender as FrameworkElement)?.DataContext is PlateRow row) await ViewImageAsync(row.BackImage, "تصویر پشت کارت خودرو"); }
    private async Task ViewImageAsync(string path, string title)
    {
        if (path.Length == 0) { Warn("تصویری ثبت نشده است."); return; } var bytes = await RunAsync("دریافت تصویر", () => _client.DownloadAsync(path)); if (bytes is null) return;
        using var stream = new MemoryStream(bytes); var image = new BitmapImage(); image.BeginInit(); image.CacheOption = BitmapCacheOption.OnLoad; image.StreamSource = stream; image.EndInit(); image.Freeze(); new Window { Owner = this, Title = title, Width = 850, Height = 650, WindowStartupLocation = WindowStartupLocation.CenterOwner, Background = Brushes.Black, Content = new Image { Source = image, Stretch = Stretch.Uniform, Margin = new Thickness(10) } }.ShowDialog();
    }

    private async void LoadPrices_Click(object sender, RoutedEventArgs e) => await LoadPricesAsync();
    private async Task LoadPricesAsync() { var data = await RunAsync("دریافت نرخ خدمات پل و لافت", () => _client.GetPricesAsync(), false); if (data is null) return; _prices.Clear(); foreach (var row in (data.Array("data", "list") ?? []).OfType<JsonObject>()) _prices.Add(new() { Id = row.Text("id"), Type = row.Text("type_name"), Price = row.Text("price_total") }); }
    private async void LoadTraffic_Click(object sender, RoutedEventArgs e) => await LoadTrafficAsync();
    private async Task LoadTrafficAsync() { var data = await RunAsync($"دریافت تردد - صفحه {_trafficPage}", () => _client.ListTrafficAsync(_trafficPage), false); if (data is null) return; var body = data.Object("data") ?? data; _trafficPages = Math.Max(1, body.Int("totalPages", 1)); TrafficPageText.Text = $"صفحه {_trafficPage} از {_trafficPages}"; _traffic.Clear(); foreach (var row in (body.Array("list") ?? []).OfType<JsonObject>()) _traffic.Add(new() { Source = row.CloneObject() }); }
    private async void PreviousTrafficPage_Click(object sender, RoutedEventArgs e) { if (_trafficPage > 1) { _trafficPage--; await LoadTrafficAsync(); } }
    private async void NextTrafficPage_Click(object sender, RoutedEventArgs e) { if (_trafficPage < _trafficPages) { _trafficPage++; await LoadTrafficAsync(); } }
    private void ToggleTrafficDetails_Click(object sender, RoutedEventArgs e) => TrafficDetailsBox.Visibility = TrafficDetailsBox.Visibility == Visibility.Visible ? Visibility.Collapsed : Visibility.Visible;
    private void TrafficGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (TrafficGrid.SelectedItem is not TrafficRow row) return; var copy = row.Source.CloneObject(); foreach (var key in new[] { "code_melli", "mob" }) if (copy.Text(key) is { Length: > 0 } value) copy[key] = JsonHelpers.Mask(value); foreach (var key in new[] { "pan", "pan_laft" }) if (copy.ContainsKey(key)) copy[key] = "<masked>"; TrafficDetailsText.Text = copy.ToJsonString(JsonHelpers.Options);
    }

    private void PlateContract_Click(object sender, RoutedEventArgs e) => DiagnosticsText.Text = "قرارداد قطعی مدیریت پلاک\n\nPOST /api/private/pol-laft/plk  — لیست { pageNumber }\nPUT /api/private/pol-laft/plk   — ثبت { id, plk1..plk4, img1, img2 }\nPATCH /api/private/pol-laft/plk — ویرایش\nDELETE /api/private/pol-laft/plk — حذف { id }\n\nاطمینان: ۱۰۰٪ (برگرفته از JS سایت)";
    private void ReservationContract_Click(object sender, RoutedEventArgs e) => DiagnosticsText.Text = "قرارداد قطعی ثبت نوبت\n\nPUT /api/private/pelak/reservation\n{ sessionId, sessionDate, plkNumber, plkSeriesNumber, transferCode, sessionTypeId }\n\nاطمینان: ۱۰۰٪";
    private async void AuthDiagnostics_Click(object sender, RoutedEventArgs e)
    {
        var result = await RunAsync("بررسی وضعیت ورود و CSRF", async () => new JsonObject { ["session"] = await _client.GetSessionAsync(), ["providers"] = await _client.GetProvidersAsync(), ["csrf"] = await _client.GetCsrfAsync() }); if (result is null) return; var session = result["session"] as JsonObject; DiagnosticsText.Text = QeshmClient.SessionIsValid(session) ? "Session فعال است.\nCSRF و Providers از سایت خوانده شدند؛ مقادیر حساس نمایش داده نمی‌شوند." : "Session فعال نیست.";
    }
}
