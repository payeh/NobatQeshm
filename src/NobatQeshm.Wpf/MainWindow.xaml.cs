using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json.Nodes;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace NobatQeshm.Wpf;

public partial class MainWindow : Window
{
    private readonly QeshmClient _client;
    private readonly SessionStore _sessionStore = new();
    private readonly SavedPlateStore _savedPlateStore = new();
    private readonly ObservableCollection<SavedProfile> _savedProfiles = [];
    private readonly ObservableCollection<SavedPlate> _savedPlates = [];
    private readonly ObservableCollection<PlateRow> _plates = [];
    private readonly ObservableCollection<PriceRow> _prices = [];
    private readonly ObservableCollection<TrafficRow> _traffic = [];
    private readonly ObservableCollection<ReservationRow> _reservations = [];
    private readonly ObservableCollection<AvailableSessionRow> _available = [];
    private readonly ObservableCollection<MonitorItem> _monitors = [];
    private readonly ObservableCollection<ServiceOption> _monitorServices = [];
    private readonly DispatcherTimer _monitorTimer = new() { Interval = TimeSpan.FromSeconds(1) };
    private readonly LogWindow _logWindow = new();
    private JsonObject _activeSession = [];
    private string _activeSavedCode = "";
    private string _captchaId = "";
    private bool _captchaBusy;
    private JsonObject? _bookingSearch;
    private JsonObject _lastReceipt = [];
    private ReceiptAdvertising _advertising = AdvertisingStore.Load();
    private int _busyCount;
    private bool _closing;
    private bool _activationLocked;
    private int _platePage = 1, _platePages = 1, _trafficPage = 1, _trafficPages = 1;
    private bool _plateCanAdd;

    public MainWindow()
    {
        InitializeComponent();
        RestoreWindowSize(AppPaths.WindowSize);
        _client = new(logger: Log);
        SavedAccountsCombo.ItemsSource = _savedProfiles;
        MonitorAccountCombo.ItemsSource = _savedProfiles;
        SavedPlateBookingCombo.ItemsSource = _savedPlates;
        SavedPlateMonitorCombo.ItemsSource = _savedPlates;
        PlatesGrid.ItemsSource = _plates; PricesGrid.ItemsSource = _prices; TrafficGrid.ItemsSource = _traffic;
        ReservationCards.ItemsSource = _reservations; AvailableSessionsGrid.ItemsSource = _available;
        MonitorGrid.ItemsSource = _monitors; MonitorServicesItems.ItemsSource = _monitorServices;
        MonitorServicesItems.ItemTemplate = BuildMonitorServiceTemplate();
        InitializeMonitorSounds();
        _logWindow.Closing += (_, e) => { if (!_closing) { e.Cancel = true; _logWindow.Hide(); } };
        _monitorTimer.Tick += MonitorTimer_Tick;
        Loaded += MainWindow_Loaded;
        ShowSystemInfo();
        RenderReceipt();
    }

    private static DataTemplate BuildMonitorServiceTemplate()
    {
        var template = new DataTemplate(typeof(ServiceOption));
        var check = new FrameworkElementFactory(typeof(CheckBox));
        check.SetBinding(CheckBox.ContentProperty, new System.Windows.Data.Binding("Name"));
        check.SetBinding(CheckBox.IsCheckedProperty, new System.Windows.Data.Binding("Selected") { Mode = System.Windows.Data.BindingMode.TwoWay });
        check.SetValue(FrameworkElement.MarginProperty, new Thickness(10, 4, 10, 4));
        template.VisualTree = check;
        return template;
    }

    private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        Loaded -= MainWindow_Loaded;
        RefreshSavedAccounts(); RefreshSavedPlates(); _monitorTimer.Start();
        await RefreshCaptchaAsync();
        var last = _sessionStore.LastCode();
        if (last.Length > 0)
        {
            SavedAccountsCombo.SelectedItem = _savedProfiles.FirstOrDefault(x => x.NationalCode == last);
            await RestoreSessionAsync(last, silent: true);
        }
    }

    private void RefreshSavedAccounts(string selectCode = "")
    {
        var selected = selectCode.Length > 0 ? selectCode : (SavedAccountsCombo.SelectedItem as SavedProfile)?.NationalCode;
        _savedProfiles.Clear(); foreach (var item in _sessionStore.Profiles()) _savedProfiles.Add(item);
        if (!string.IsNullOrWhiteSpace(selected)) SavedAccountsCombo.SelectedItem = _savedProfiles.FirstOrDefault(x => x.NationalCode == selected);
        if (SavedAccountsCombo.SelectedItem is null && _savedProfiles.Count > 0) SavedAccountsCombo.SelectedIndex = 0;
        MonitorAccountStatusText.Text = _savedProfiles.Count == 0 ? "برای ذخیره نشست، ابتدا وارد شوید." : "نشست موردنظر برای پایش را انتخاب کنید.";
    }

    private void RefreshSavedPlates()
    {
        _savedPlates.Clear();
        try { foreach (var plate in _savedPlateStore.Load()) _savedPlates.Add(plate); }
        catch (Exception ex) { SetStatus($"خواندن پلاک‌های ذخیره‌شده ممکن نبود: {ex.Message}"); }
    }

    private async Task<T?> RunAsync<T>(string title, Func<Task<T>> work, bool showErrors = true)
    {
        if (_activationLocked) return default;
        Busy(true, title);
        try { var result = await work(); SetStatus($"{title} انجام شد."); return result; }
        catch (OperationCanceledException) { SetStatus($"{title} لغو شد."); }
        catch (Exception ex)
        {
            Log($"خطا در {title}: {ex}"); SetStatus($"خطا: {ex.Message}");
            if (showErrors && IsVisible && !_closing) MessageBox.Show(this, ex.Message, title, MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally { Busy(false, ""); }
        return default;
    }
    private async Task RunAsync(string title, Func<Task> work, bool showErrors = true) => await RunAsync(title, async () => { await work(); return true; }, showErrors);
    private void Busy(bool value, string title)
    {
        _busyCount = Math.Max(0, _busyCount + (value ? 1 : -1));
        BusyProgress.Visibility = _busyCount > 0 ? Visibility.Visible : Visibility.Collapsed;
        if (value) SetStatus(title + " ...");
    }
    private void SetStatus(string value) => StatusText.Text = value;
    private void Log(string value)
    {
        Dispatcher.Invoke(() =>
        {
            _logWindow.TextBox.AppendText($"[{DateTime.Now:HH:mm:ss}] {value}{Environment.NewLine}");
            _logWindow.TextBox.ScrollToEnd();
        });
    }

    private void ToggleLog_Click(object sender, RoutedEventArgs e)
    {
        if (_logWindow.IsVisible)
        {
            _logWindow.Hide();
            return;
        }

        _logWindow.Owner ??= this;
        _logWindow.Show();
        _logWindow.Activate();
    }
    private void ManageSavedPlates_Click(object sender, RoutedEventArgs e) => new SavedPlatesWindow(_savedPlateStore, VerifySavedPlateAsync) { Owner = this, Changed = RefreshSavedPlates }.ShowDialog();
    private async Task<SavedPlate> VerifySavedPlateAsync(SavedPlate plate, SavedPlate? original)
    {
        if (original is not null && original.TransferCode == plate.TransferCode && original.Number == plate.Number && original.Series == plate.Series) return plate;
        if (!QeshmClient.SessionIsValid(_activeSession)) throw new QeshmApiException("برای بررسی پلاک، ابتدا از تب ورود وارد شوید.");
        var services = ServiceCombo.ItemsSource as IEnumerable<ServiceOption> ?? await _client.GetReservationServiceTypesAsync();
        var service = services.FirstOrDefault(x => x.Name.Contains("نقل و انتقال")) ?? services.FirstOrDefault() ?? throw new QeshmApiException("خدمت فعالی برای بررسی پلاک پیدا نشد.");
        return await _client.VerifySavedPlateAsync(plate, service.Id);
    }

    private void SavedAccountsCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (SavedAccountsCombo.SelectedItem is not SavedProfile profile) return;
        NationalCodeText.Text = profile.NationalCode; if (profile.Mobile.Length > 0) MobileText.Text = profile.Mobile;
        SavedAccountStatusText.Text = profile.Expired || JsonHelpers.IsExpired(profile.Expires) ? "منقضی — ورود مجدد لازم است" : "ذخیره‌شده";
    }
    private async void RestoreSession_Click(object sender, RoutedEventArgs e) { if (SavedAccountsCombo.SelectedItem is SavedProfile p) await RestoreSessionAsync(p.NationalCode); }
    private async Task RestoreSessionAsync(string code, bool silent = false)
    {
        var profile = _sessionStore.Get(code);
        if (profile is null) return;
        var session = await RunAsync("بازیابی و بررسی نشست ذخیره‌شده", async () =>
        {
            if (profile.Expired || JsonHelpers.IsExpired(profile.Expires)) throw new QeshmApiException("نشست ذخیره‌شده منقضی شده است.");
            var cookies = _sessionStore.LoadCookies(code); if (cookies.Count == 0) throw new QeshmApiException("کوکی ذخیره‌شده قابل بازیابی نیست.");
            _client.ImportCookies(cookies); var value = await _client.GetSessionAsync();
            if (!QeshmClient.SessionIsValid(value)) throw new QeshmApiException("نشست ذخیره‌شده معتبر نیست.");
            return value;
        }, !silent);
        if (session is null) { _sessionStore.MarkExpired(code); RefreshSavedAccounts(code); return; }
        _activeSession = session; _activeSavedCode = code; NationalCodeText.Text = code; MobileText.Text = profile.Mobile;
        UpdateSessionUi(profile.Name); PersistSession(); await AutoLoadUserDataAsync();
    }
    private void DeleteSession_Click(object sender, RoutedEventArgs e)
    {
        if (SavedAccountsCombo.SelectedItem is not SavedProfile p || MessageBox.Show(this, $"نشست ذخیره‌شده {p.Name} حذف شود؟", "حذف حساب", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
        _sessionStore.Delete(p.NationalCode); if (_activeSavedCode == p.NationalCode) { _activeSavedCode = ""; _activeSession = []; _client.ClearAuthCookies(); UpdateBookingAccountMessage(); } RefreshSavedAccounts();
    }

    private async void RefreshCaptcha_Click(object sender, RoutedEventArgs e) => await RefreshCaptchaAsync();
    private async Task RefreshCaptchaAsync()
    {
        if (_captchaBusy) return;
        _captchaBusy = true;
        RefreshCaptchaButton.IsEnabled = false;
        RequestOtpButton.IsEnabled = false;
        _captchaId = "";
        CaptchaText.Clear();
        CaptchaImage.Source = null;
        CaptchaStatusText.Text = "در حال دریافت تصویر…";
        try
        {
            var result = await RunAsync("دریافت تصویر کد امنیتی", async () =>
            {
                var data = await _client.GetCaptchaAsync();
                CaptchaImage.Source = CaptchaImageRenderer.Render(data.Text("svg"));
                return data;
            }, showErrors: false);
            if (result is null)
            {
                CaptchaStatusText.Text = "تصویر دریافت نشد. جزئیات خطا در نوار وضعیت است.";
                return;
            }

            _captchaId = result.Text("captchaId");
            var detected = result.Text("detectedCode");
            if (AutoCaptchaCheck.IsChecked == true && detected.Length == 5) CaptchaText.Text = detected;
            CaptchaStatusText.Text = CaptchaText.Text.Length == 0
                ? "پنج رقم تصویر را وارد کنید." : "کد وارد شد. می‌توانید آن را با تصویر تطبیق دهید.";
        }
        finally
        {
            _captchaBusy = false;
            RefreshCaptchaButton.IsEnabled = true;
            RequestOtpButton.IsEnabled = _captchaId.Length > 0;
        }
    }
    private async void RequestOtp_Click(object sender, RoutedEventArgs e)
    {
        if (_captchaBusy || _captchaId.Length == 0) return;
        var code = JsonHelpers.NormalizeDigits(NationalCodeText.Text);
        var mobile = JsonHelpers.NormalizeDigits(MobileText.Text);
        if (code.Length == 0 || mobile.Length == 0) { Warn("کد ملی و شماره موبایل را وارد کنید."); return; }
        var captcha = JsonHelpers.NormalizeDigits(CaptchaText.Text);
        if (captcha.Length != 5 || !captcha.All(char.IsAsciiDigit)) { Warn("پنج رقم تصویر کد امنیتی را وارد کنید."); return; }

        var captchaId = _captchaId;
        _captchaId = "";
        _captchaBusy = true;
        RefreshCaptchaButton.IsEnabled = false;
        RequestOtpButton.IsEnabled = false;
        try
        {
            var result = await RunAsync("ارسال کد فعال‌سازی", () => _client.RequestOtpAsync(code, mobile, captchaId, captcha));
            if (result is null) return;
            LoginStatusTitle.Text = "پاسخ سامانه";
            LoginStatusText.Text = result.Text("msg", "message");
            OtpText.Focus();
        }
        finally
        {
            _captchaBusy = false;
            RefreshCaptchaButton.IsEnabled = true;
            CaptchaStatusText.Text = "برای درخواست دوبارهٔ پیامک، «تصویر جدید» را بزنید.";
        }
    }
    private async Task VerifyLoginAsync()
    {
        var code = JsonHelpers.NormalizeDigits(NationalCodeText.Text); var mobile = JsonHelpers.NormalizeDigits(MobileText.Text); var otp = OtpText.Password.Trim();
        if (code.Length == 0 || mobile.Length == 0 || otp.Length == 0) { Warn("کد ملی، موبایل و کد فعال‌سازی را وارد کنید."); return; }
        var session = await RunAsync("تأیید کد فعال‌سازی و تشکیل نشست", () => _client.VerifyOtpAsync(code, mobile, otp));
        if (session is null) return;
        _activeSession = session; _activeSavedCode = code; PersistSession(); UpdateSessionUi(); LoginStatusTitle.Text = "ورود موفق"; LoginStatusTitle.Foreground = Brushes.SeaGreen; LoginStatusText.Text = "نشست به‌صورت امن ذخیره شد. اطلاعات در حال بارگذاری است.";
        await AutoLoadUserDataAsync();
    }
    private void PersistSession(JsonObject? person = null)
    {
        if (!QeshmClient.SessionIsValid(_activeSession)) return;
        var user = QeshmClient.SessionUser(_activeSession);
        var code = JsonHelpers.NormalizeDigits(user.TextOr(_activeSavedCode, "codeMelli", "MelliCode"));
        if (code.Length == 0) return;
        var existing = _sessionStore.Get(code);
        var name = FirstPersonName($"{person?.Text("FirstName")} {person?.Text("LastName")}",
            person?.Text("name", "fullName"), existing?.Name, user.Text("name", "fullName"));
        if (name.Length == 0) name = "کاربر";
        var mobile = _client.LastLoginInputs.GetValueOrDefault("mob") ?? MobileText.Text.Trim(); if (mobile.Length == 0) mobile = existing?.Mobile ?? "";
        _sessionStore.Save(name, code, mobile, _activeSession.Text("expires"), _client.ExportCookies()); _activeSavedCode = code; RefreshSavedAccounts(code);
    }
    private void UpdateSessionUi(string displayName = "")
    {
        var user = QeshmClient.SessionUser(_activeSession); var code = user.Text("codeMelli"); var saved = _sessionStore.Get(code); var name = displayName.Length > 0 ? displayName : saved?.Name ?? "";
        SessionHeaderText.Text = $"وارد شده‌اید{(name.Length > 0 ? $" | {name}" : "")}{(code.Length > 0 ? $" | {code}" : "")}";
        LoginStatusTitle.Text = "نشست معتبر است"; LoginStatusTitle.Foreground = Brushes.SeaGreen; LoginStatusText.Text = "ورود فعال است و نیازی به دریافت مجدد OTP نیست.";
        UpdateBookingAccountMessage();
    }
    private async Task AutoLoadUserDataAsync()
    {
        await LoadServicesAsync();
        await Task.WhenAll(LoadProfileAsync(), LoadReservationsAsync(), LoadPlatesAsync(), LoadPricesAsync(), LoadTrafficAsync());
    }

    private async void LoadProfile_Click(object sender, RoutedEventArgs e) => await LoadProfileAsync();
    private async Task LoadProfileAsync()
    {
        var session = _activeSession;
        var data = await RunAsync("دریافت مشخصات کاربر", () => _client.GetProfileAsync()); if (data is null) return;
        if (!ReferenceEquals(session, _activeSession)) return;
        var p = QeshmClient.ExtractPerson(data); if (p.Count == 0) { Warn("شیء مشخصات شخص در پاسخ پیدا نشد."); return; }
        var sessionCode = JsonHelpers.NormalizeDigits(QeshmClient.SessionUser(session).TextOr(_activeSavedCode, "codeMelli", "MelliCode"));
        var personCode = JsonHelpers.NormalizeDigits(p.Text("MelliCode", "codeMelli"));
        if (sessionCode.Length > 0 && personCode.Length > 0 && sessionCode != personCode)
        {
            SetStatus("مشخصات دریافتی با حساب فعال مطابقت ندارد.");
            return;
        }
        string V(params string[] keys) => p.TextOr("—", keys);
        string D(params string[] keys) { var raw = V(keys); var digits = new string(raw.Where(char.IsDigit).ToArray()); return digits.Length == 8 ? $"{digits[..4]}/{digits[4..6]}/{digits[6..]}" : raw; }
        var fullName = FirstPersonName($"{p.Text("FirstName")} {p.Text("LastName")}", p.Text("name", "fullName"));
        CopyProfileNameButton.IsEnabled = fullName.Length > 0;
        if (fullName.Length == 0) fullName = "کاربر";
        ProfileNameText.Text = fullName;
        ProfileStatusText.Text = $"نمایش مشخصات: {fullName}"; ProfileStatusText.Foreground = Brushes.SeaGreen; ProfileStatusText.FontWeight = FontWeights.Bold;
        PersonalFields.ItemsSource = Fields(("نوع عضویت", V("GroupTitle")), ("ملیت", V("NationalityTitle")), ("نام", V("FirstName")), ("نام خانوادگی", V("LastName")), ("نام لاتین", V("LatinFirstName")), ("نام خانوادگی لاتین", V("LatinLastName")), ("نام پدر", V("Father")), ("کد ملی", V("MelliCode")), ("تاریخ اعتبار کد ملی", D("MelliExpiredDate2", "MelliExpiredDate")), ("شماره پاسپورت", V("PassportNum")), ("تاریخ اعتبار پاسپورت", D("PassportExpiredDate2", "PassportExpiredDate")), ("تاریخ تولد", D("BirthDate2", "BirthDate")), ("محل تولد", V("BirthPlaceTitle")), ("صادره", V("IdPlaceTitle")), ("تحصیلات", V("EducationTitle")), ("دین و مذهب", V("ReligionTitle")));
        ResidenceFields.ItemsSource = Fields(("کد پستی", V("PostalCode")), ("تلفن", V("Phone")), ("تلفن همراه", V("CellPhone")), ("فکس", V("Fax")), ("آدرس محل سکونت", V("StayAdr")));
        JobFields.ItemsSource = Fields(("شغل", V("Job")), ("کد پستی", V("JobPostalCode")), ("تلفن", V("JobPhone")), ("فکس", V("JobFax")), ("آدرس محل کار", V("JobAdr")));
        ExtraFields.ItemsSource = Fields(("وضعیت تأهل", p.Bool("IsSingle") ? "مجرد" : "متأهل"), ("جنسیت", p.Bool("IsMale") ? "مرد" : "زن"), ("مقیم", p.Bool("IsResident") ? "بله" : "خیر"), ("ایمیل", V("Email")), ("شماره بیمه", V("InsurNum")), ("اشتراک برق", V("EshterkBargh")));
        ProfileImage.Source = null;
        CopyProfilePhotoButton.IsEnabled = false;
        PersistSession(p); UpdateSessionUi(fullName);
        try
        {
            var photo = p.Text("userPic");
            if (photo.Length == 0) return;
            var bytes = photo.StartsWith("data:image")
                ? Convert.FromBase64String(photo[(photo.IndexOf(',') + 1)..])
                : photo.Length > 300 && !photo.StartsWith("http")
                    ? Convert.FromBase64String(photo)
                    : await _client.DownloadAsync(photo);
            if (ReferenceEquals(session, _activeSession)) ProfilePhoto(bytes);
        }
        catch (Exception ex) { Log("نمایش عکس پروفایل ناموفق بود: " + ex.Message); }
    }
    private static List<ProfileField> Fields(params (string Label, string Value)[] fields) => fields.Select(x => new ProfileField { Label = x.Label, Value = x.Value }).ToList();
    private void ProfilePhoto(byte[] bytes)
    {
        using var stream = new MemoryStream(bytes); var image = new BitmapImage(); image.BeginInit(); image.CacheOption = BitmapCacheOption.OnLoad; image.StreamSource = stream; image.DecodePixelWidth = 130; image.EndInit(); image.Freeze();
        ProfileImage.Source = image;
        CopyProfilePhotoButton.IsEnabled = true;
    }

    private void CopyProfileValue_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            switch ((sender as Button)?.CommandParameter)
            {
                case string text when !string.IsNullOrWhiteSpace(text) && text != "—":
                    Clipboard.SetText(text);
                    SetStatus("مقدار در کلیپ‌بورد کپی شد.");
                    break;
                case BitmapSource image:
                    Clipboard.SetImage(image);
                    SetStatus("عکس کاربر در کلیپ‌بورد کپی شد.");
                    break;
            }
        }
        catch (ExternalException)
        {
            SetStatus("کلیپ‌بورد در دسترس نیست. دوباره تلاش کنید.");
        }
    }
    private void ShowSystemInfo()
    {
        var mem = GC.GetGCMemoryInfo().TotalAvailableMemoryBytes;
        var appVersion = typeof(MainWindow).Assembly.GetName().Version?.ToString(3) ?? "—";
        SystemInfoGrid.ItemsSource = Fields(("سیستم‌عامل", RuntimeInformation.OSDescription), ("پردازنده", Environment.GetEnvironmentVariable("PROCESSOR_IDENTIFIER") ?? "—"), ("پردازنده‌های منطقی", Environment.ProcessorCount.ToString()), ("معماری", RuntimeInformation.OSArchitecture.ToString()), ("حافظه رم قابل استفاده", mem > 0 ? $"{mem / 1024d / 1024 / 1024:0.0} GiB" : "—"), ("نسخه .NET", Environment.Version.ToString()), ("نسخه برنامه", appVersion));
    }
    private void Warn(string text) => MessageBox.Show(this, text, "توجه", MessageBoxButton.OK, MessageBoxImage.Warning);

    internal void SetActivationLocked(bool locked)
    {
        _activationLocked = locked;
        IsEnabled = !locked;
        if (locked)
        {
            _monitorTimer.Stop();
            _notifications.Clear();
            foreach (var notification in _visibleNotifications.ToArray()) notification.Close();
        }
        else if (!_closing)
        {
            _monitorTimer.Start();
        }
    }
}
