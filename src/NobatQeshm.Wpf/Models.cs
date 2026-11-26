using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace NobatQeshm.Wpf;

public sealed record CookieInfo
{
    [JsonPropertyName("name")] public string Name { get; init; } = "";
    [JsonPropertyName("value")] public string Value { get; init; } = "";
    [JsonPropertyName("domain")] public string Domain { get; init; } = "";
    [JsonPropertyName("path")] public string Path { get; init; } = "/";
    [JsonPropertyName("secure")] public bool Secure { get; init; }
    [JsonPropertyName("expires")] public long? Expires { get; init; }
}

public sealed record SavedProfile(
    string Name, string NationalCode, string Mobile, string Expires,
    bool Expired, string SavedAt, JsonObject Cookies)
{
    public string Display => $"{Name} — {NationalCode}{(Expired || JsonHelpers.IsExpired(Expires) ? " • منقضی" : "")}";
}

public sealed record SavedPlate(
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("transfer_code")] string TransferCode,
    [property: JsonPropertyName("number")] string Number,
    [property: JsonPropertyName("series")] string Series,
    [property: JsonPropertyName("saved_date")] string SavedDate = "",
    [property: JsonPropertyName("vehicle_name")] string VehicleName = "")
{
    public string Display => $"{Name}  |  {Number}{(string.IsNullOrWhiteSpace(Series) ? "" : $" / {Series}")}  |  {TransferCode}";
    public string PersianSavedDate => JsonHelpers.ToPersianDate(SavedDate);
}

public sealed record ReceiptAdvertising(
    [property: JsonPropertyName("enabled")] bool Enabled = true,
    [property: JsonPropertyName("text")] string Text = ReceiptAdvertising.DefaultText)
{
    public const string DefaultText = "ارائه کلیه خدمات تعویض پلاک در قشم  :\nکافینت مهتاب و جزیره  :\nنشانی : مجتمع فردوسی پلاک 2175 و 2156\nتلفن تماس : 90 910 910 076";
    public string VisibleText => Enabled ? Text.Trim() : "";
}

public sealed class PlateRow
{
    public JsonObject Source { get; init; } = [];
    public string Id => Source.Text("id");
    public string Plate => JsonHelpers.PlateText(Source);
    public string Date => $"{Source.Text("date_reg")} {Source.Text("time_reg")}".Trim();
    public string Status => Source.Text("status_fa", "status_id");
    public string Reject => Source.Text("msg_reject");
    public string FrontImage => Source.Text("url_img1");
    public string BackImage => Source.Text("url_img2");
    public int StatusId => Source.Int("status_id");
}

public sealed class PriceRow
{
    public string Id { get; init; } = "";
    public string Type { get; init; } = "";
    public string Price { get; init; } = "";

    public string FormattedPrice
    {
        get
        {
            var value = Price.Trim();
            var amountText = value.EndsWith("ریال", StringComparison.Ordinal) || value.EndsWith("ريال", StringComparison.Ordinal)
                ? value[..^4].TrimEnd()
                : value;
            var normalized = JsonHelpers.NormalizeDigits(amountText).Replace('٬', ',').Replace('،', ',').Replace('٫', '.');
            var culture = System.Globalization.CultureInfo.InvariantCulture;
            if (!decimal.TryParse(normalized, System.Globalization.NumberStyles.Number, culture, out var amount)) return value;
            return $"{amount.ToString("#,0.############################", culture).Replace(',', '،')} ریال";
        }
    }
}

public sealed class TrafficRow
{
    public JsonObject Source { get; init; } = [];
    public string Id => Source.Text("id");
    public string Date => $"{Source.Text("date_in")} {Source.Text("time_in")}".Trim();
    public string Plate => JsonHelpers.PlateText(Source);
    public string Quantity => Source.Text("qty");
    public string Type => Source.Text("type_name");
    public string Price => Source.Text("price_total");
    public string Trace => Source.Text("trace_number");
    public string Payment => Source.Text("pay_pohl", "pay_laft");
}

public sealed class ReservationRow
{
    public JsonObject Source { get; init; } = [];
    public string Service => Source.TextOr("نوبت ثبت‌شده", "sessionTypeFa");
    public string Plate => Source.Text("vehiclePlk", "plkNumber");
    public string Series => Source.Text("vehiclePlkSeries", "plkSeriesNumber");
    public string TransferCode => Source.Text("vehicleTransferCode", "transferCode");
    public string Time => $"{Source.Text("startTime")} - {Source.Text("endTime")}";
    public string CreatedAt => Source.Text("createdAt");
    public string Code => Source.Text("reservationCode", "code");
    public bool HasCode => Code.Length > 0;
    public string CodeLabel => $"کد رزرو نوبت: {(HasCode ? Code : "—")}";
}

public sealed class AvailableSessionRow
{
    public int Index { get; init; }
    public JsonObject Source { get; init; } = [];
    public string Id => Source.Text("id", "sessionId", "session_id");
    public string Date => Source.Text("date", "sessionDate", "dateFa");
    public string Start => Source.Text("startTime", "start_time");
    public string End => Source.Text("endTime", "end_time");
    public string Counter => Source.Text("counterNumber");
}

public sealed class ProfileField
{
    public string Label { get; init; } = "";
    public string Value { get; init; } = "—";
    public bool HasValue => !string.IsNullOrWhiteSpace(Value) && Value != "—";
}

public sealed class ServiceOption
{
    public string Id { get; init; } = "";
    public string Name { get; init; } = "";
    public bool Selected { get; set; }
    public override string ToString() => Name;
}

public sealed class MonitorItem : INotifyPropertyChanged
{
    private bool _enabled = true;
    private bool _checking;
    private int _totalSlots;
    private string _lastResult = "در صف بررسی";
    private DateTime? _lastCheck;
    private DateTime? _nextDue;

    public string Id { get; init; } = Guid.NewGuid().ToString("N");
    public string TransferCode { get; init; } = "";
    public string PlateNumber { get; init; } = "";
    public string PlateSeries { get; init; } = "";
    public string ServiceId { get; init; } = "";
    public string ServiceName { get; init; } = "";
    public int IntervalMinutes { get; init; } = 5;
    public bool AlarmEnabled { get; init; } = true;
    public int AlarmCount { get; set; } = 3;
    public bool BalloonEnabled { get; init; } = true;
    public string AlarmSound { get; init; } = "اعلان ویندوز";
    public bool Enabled { get => _enabled; set => Set(ref _enabled, value); }
    public bool Checking { get => _checking; set => Set(ref _checking, value); }
    public int TotalSlots { get => _totalSlots; set => Set(ref _totalSlots, value); }
    public string LastResult { get => _lastResult; set => Set(ref _lastResult, value); }
    public DateTime? LastCheck { get => _lastCheck; set => Set(ref _lastCheck, value); }
    public DateTime? NextDue { get => _nextDue; set => Set(ref _nextDue, value); }
    public string Vehicle => $"کد {TransferCode} | پلاک {PlateNumber} / {(string.IsNullOrWhiteSpace(PlateSeries) ? "—" : PlateSeries)}";
    public string Interval => $"{IntervalMinutes} دقیقه";
    public string LastTime => LastCheck?.ToString("HH:mm:ss") ?? "—";
    public string NextTime => Checking ? "در حال بررسی" : !Enabled ? "متوقف" : NextDue?.ToString("HH:mm:ss") ?? "—";
    public string Alarm => AlarmEnabled ? $"{AlarmCount}× {AlarmSound}{(BalloonEnabled ? " + اعلان" : "")}" : BalloonEnabled ? "اعلان" : "خاموش";
    public string Result => Checking ? "در حال دریافت اطلاعات از سایت ..." : TotalSlots > 0 ? $"{TotalSlots} بازه آزاد" : LastResult;

    public event PropertyChangedEventHandler? PropertyChanged;
    public void Refresh() { foreach (var property in new[] { nameof(Enabled), nameof(Checking), nameof(TotalSlots), nameof(LastResult), nameof(LastTime), nameof(NextTime), nameof(Alarm), nameof(Result) }) PropertyChanged?.Invoke(this, new(property)); }
    private void Set<T>(ref T field, T value, [CallerMemberName] string? name = null) { if (EqualityComparer<T>.Default.Equals(field, value)) return; field = value; PropertyChanged?.Invoke(this, new(name)); Refresh(); }
}

public static class JsonHelpers
{
    public static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    public static string Text(this JsonObject? obj, params string[] keys) => ReadText(obj, keys, "");
    public static string TextOr(this JsonObject? obj, string fallback, params string[] keys) => ReadText(obj, keys, fallback);
    private static string ReadText(JsonObject? obj, IEnumerable<string> keys, string fallback)
    {
        if (obj is null) return fallback;
        foreach (var key in keys)
        {
            var node = obj[key];
            if (node is null) continue;
            var value = node is JsonValue v && v.TryGetValue<string>(out var s) ? s : node.ToJsonString().Trim('"');
            if (!string.IsNullOrWhiteSpace(value) && value != "null") return value;
        }
        return fallback;
    }
    public static int Int(this JsonObject? obj, string key, int fallback = 0) => int.TryParse(obj.Text(key), out var value) ? value : fallback;
    public static bool Bool(this JsonObject? obj, string key, bool fallback = false)
    {
        var text = obj.Text(key);
        return string.IsNullOrEmpty(text) ? fallback : text.Equals("true", StringComparison.OrdinalIgnoreCase) || text == "1";
    }
    public static JsonObject? Object(this JsonObject? obj, params string[] path)
    {
        JsonNode? node = obj;
        foreach (var key in path) node = (node as JsonObject)?[key];
        return node as JsonObject;
    }
    public static JsonArray? Array(this JsonObject? obj, params string[] path)
    {
        JsonNode? node = obj;
        foreach (var key in path) node = (node as JsonObject)?[key];
        return node as JsonArray;
    }
    public static JsonObject CloneObject(this JsonObject obj) => JsonNode.Parse(obj.ToJsonString())!.AsObject();
    public static bool IsExpired(string? value) => DateTimeOffset.TryParse(value, out var date) && date <= DateTimeOffset.UtcNow;
    public static string NormalizeDigits(string? value)
    {
        const string source = "۰۱۲۳۴۵۶۷۸۹٠١٢٣٤٥٦٧٨٩";
        const string target = "01234567890123456789";
        var text = value?.Trim() ?? "";
        for (var i = 0; i < source.Length; i++) text = text.Replace(source[i], target[i]);
        return text;
    }
    public static string Mask(string value)
    {
        if (value.Length <= 4) return new('*', value.Length);
        var edge = value.Length <= 8 ? 2 : 3;
        return value[..edge] + new string('*', value.Length - edge * 2) + value[^edge..];
    }
    public static string PlateText(JsonObject row) => $"{row.Text("plk4")} /  {row.Text("plk3")} {row.Text("plk2")} {row.Text("plk1")}".Trim();
    public static string ToPersianDate(string value)
    {
        if (!DateTime.TryParse(value, out var date)) return string.IsNullOrWhiteSpace(value) ? "—" : value;
        var pc = new System.Globalization.PersianCalendar();
        return $"{pc.GetYear(date):0000}/{pc.GetMonth(date):00}/{pc.GetDayOfMonth(date):00}";
    }
}
