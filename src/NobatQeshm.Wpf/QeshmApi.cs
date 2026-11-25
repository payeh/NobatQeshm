using System.Net;
using System.Net.Http.Headers;
using System.Net.Http;
using System.Text;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Xml.Linq;

namespace NobatQeshm.Wpf;

public sealed class QeshmApiException : Exception
{
    public HttpStatusCode? HttpStatus { get; }
    public int? BusinessStatus { get; }
    public JsonNode? Response { get; }
    public QeshmApiException(string message, HttpStatusCode? httpStatus = null, int? businessStatus = null, JsonNode? response = null, Exception? inner = null)
        : base(message, inner) { HttpStatus = httpStatus; BusinessStatus = businessStatus; Response = response; }
}

public sealed class QeshmClient : IDisposable
{
    public const string DefaultBaseUrl = "https://nobat.qeshm.ir";
    private readonly CookieContainer _cookies = new();
    private readonly HttpClientHandler _handler;
    private readonly HttpClient _http;
    private readonly Action<string> _log;
    public string BaseUrl { get; }
    public Dictionary<string, string> LastLoginInputs { get; } = [];

    public QeshmClient(string baseUrl = DefaultBaseUrl, Action<string>? logger = null)
    {
        BaseUrl = baseUrl.TrimEnd('/');
        _log = logger ?? (_ => { });
        _handler = new HttpClientHandler
        {
            CookieContainer = _cookies,
            UseCookies = true,
            AutomaticDecompression = DecompressionMethods.All,
            AllowAutoRedirect = true
        };
        _http = new HttpClient(_handler) { Timeout = TimeSpan.FromSeconds(30) };
        _http.DefaultRequestHeaders.Accept.ParseAdd("application/json, text/plain, */*");
        _http.DefaultRequestHeaders.AcceptLanguage.ParseAdd("fa,en-US;q=0.9,en;q=0.8");
        _http.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/151.0.0.0 Safari/537.36");
    }

    private Uri Url(string path) => new(path.StartsWith("http", StringComparison.OrdinalIgnoreCase) ? path : BaseUrl + "/" + path.TrimStart('/'));

    private async Task<JsonObject> JsonRequestAsync(HttpMethod method, string path, JsonNode? body = null, HttpContent? content = null, CancellationToken token = default)
    {
        var uri = Url(path);
        _log($"{method.Method.ToUpperInvariant()} {uri.PathAndQuery}");
        using var request = new HttpRequestMessage(method, uri);
        if (content is not null) request.Content = content;
        else if (body is not null) request.Content = new StringContent(body.ToJsonString(JsonHelpers.Options), Encoding.UTF8, "application/json");
        HttpResponseMessage response;
        try { response = await _http.SendAsync(request, token); }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            throw new QeshmApiException(ex is TaskCanceledException && !token.IsCancellationRequested ? "زمان انتظار ارتباط با سامانه به پایان رسید." : $"خطای شبکه: {ex.Message}", inner: ex);
        }
        using (response)
        {
            var raw = await response.Content.ReadAsStringAsync(token);
            JsonNode? parsed = null;
            try { parsed = JsonNode.Parse(raw); } catch { }
            var obj = parsed as JsonObject ?? new JsonObject { ["raw"] = raw.Length > 10000 ? raw[..10000] : raw };
            if (response.StatusCode == HttpStatusCode.TooManyRequests || obj.Int("status") == 429)
                throw new QeshmApiException("سامانه تعداد درخواست‌ها را محدود کرده است. کمی صبر کنید و دوباره تلاش کنید.",
                    HttpStatusCode.TooManyRequests, 429, obj);
            if (!response.IsSuccessStatusCode)
            {
                var message = obj.Text("msg", "message");
                throw new QeshmApiException(string.IsNullOrWhiteSpace(message) ? $"HTTP {(int)response.StatusCode}" : message,
                    response.StatusCode, obj.Int("status"), obj);
            }
            return obj;
        }
    }

    public async Task<JsonObject> GetCaptchaAsync(CancellationToken token = default)
    {
        var body = await JsonRequestAsync(HttpMethod.Get, "/api/auth/captcha", token: token);
        var id = body.Text("captchaId");
        var svg = body.Text("svg");
        if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(svg)) throw new QeshmApiException("شناسه یا تصویر کد امنیتی از سایت دریافت نشد.", response: body);
        body["detectedCode"] = DetectCaptchaCode(svg);
        return body;
    }

    public Task<JsonObject> RequestOtpAsync(string nationalCode, string mobile, string captchaId, string captchaText, CancellationToken token = default)
    {
        captchaText = JsonHelpers.NormalizeDigits(captchaText);
        if (string.IsNullOrWhiteSpace(captchaId)) throw new QeshmApiException("شناسه کد امنیتی موجود نیست؛ آن را به‌روزرسانی کنید.");
        if (!Regex.IsMatch(captchaText, "^[0-9]{5}$")) throw new QeshmApiException("کد امنیتی باید دقیقاً ۵ رقم باشد.");
        LastLoginInputs["codeMelli"] = nationalCode;
        LastLoginInputs["mob"] = mobile;
        return JsonRequestAsync(HttpMethod.Post, "/api/auth/signinVerify", new JsonObject
        {
            ["codeMelli"] = nationalCode, ["mob"] = mobile, ["captchaId"] = captchaId, ["captchaText"] = captchaText
        }, token: token);
    }

    public Task<JsonObject> GetProvidersAsync(CancellationToken token = default) => JsonRequestAsync(HttpMethod.Get, "/api/auth/providers", token: token);
    public Task<JsonObject> GetCsrfAsync(CancellationToken token = default) => JsonRequestAsync(HttpMethod.Get, "/api/auth/csrf", token: token);
    public Task<JsonObject> GetSessionAsync(CancellationToken token = default) => JsonRequestAsync(HttpMethod.Get, "/api/auth/session", token: token);

    public async Task<JsonObject> VerifyOtpAsync(string nationalCode, string mobile, string otp, CancellationToken token = default)
    {
        var providers = await GetProvidersAsync(token);
        var csrf = (await GetCsrfAsync(token)).Text("csrfToken");
        if (string.IsNullOrWhiteSpace(csrf)) throw new QeshmApiException("csrfToken از سایت دریافت نشد.");
        var callback = providers.Object("credentials")?.Text("callbackUrl");
        var callbackPath = string.IsNullOrWhiteSpace(callback) ? "/api/auth/callback/credentials" : new Uri(callback, UriKind.RelativeOrAbsolute).IsAbsoluteUri ? new Uri(callback).PathAndQuery : callback;
        string[] candidates = ["verifyCode", "verificationCode", "smsCode", "otp", "code", "activeCode"];
        Exception? last = null;
        foreach (var field in candidates)
        {
            token.ThrowIfCancellationRequested();
            var form = new Dictionary<string, string>
            {
                ["csrfToken"] = csrf, ["callbackUrl"] = BaseUrl + "/panel", ["json"] = "true",
                ["codeMelli"] = nationalCode, ["mob"] = mobile, [field] = otp
            };
            try { await JsonRequestAsync(HttpMethod.Post, callbackPath, content: new FormUrlEncodedContent(form), token: token); }
            catch (Exception ex) when (ex is not QeshmApiException { HttpStatus: HttpStatusCode.TooManyRequests }) { last = ex; }
            try
            {
                var session = await GetSessionAsync(token);
                if (SessionIsValid(session)) return session;
            }
            catch (Exception ex) when (ex is not QeshmApiException { HttpStatus: HttpStatusCode.TooManyRequests }) { last = ex; }
        }
        throw new QeshmApiException($"تأیید OTP به نشست معتبر نرسید{(last is null ? "." : $": {last.Message}")}", inner: last);
    }

    public Task<JsonObject> GetProfileAsync(CancellationToken token = default) => JsonRequestAsync(HttpMethod.Post, "/api/private/profile/get-info", token: token);
    public Task<JsonObject> ListPlatesAsync(int page = 1, CancellationToken token = default) => JsonRequestAsync(HttpMethod.Post, "/api/private/pol-laft/plk", new JsonObject { ["pageNumber"] = page }, token: token);
    public Task<JsonObject> GetPricesAsync(CancellationToken token = default) => JsonRequestAsync(HttpMethod.Post, "/api/public/pol-laft/pol-prices", token: token);
    public Task<JsonObject> ListTrafficAsync(int page = 1, CancellationToken token = default) => JsonRequestAsync(HttpMethod.Post, "/api/private/pol-laft/pol-entry", new JsonObject { ["pageNumber"] = page }, token: token);
    public Task<JsonObject> ListReservationsAsync(CancellationToken token = default) => JsonRequestAsync(HttpMethod.Get, "/api/private/pelak/reservation", token: token);
    public Task<JsonObject> ReservationAvailabilityAsync(string serviceId, string transferCode, string plateNumber, string series, CancellationToken token = default) =>
        JsonRequestAsync(HttpMethod.Post, "/api/private/pelak/reservation", new JsonObject
        {
            ["sessionTypeId"] = serviceId, ["transferCode"] = transferCode, ["plkNumber"] = plateNumber, ["plkSeriesNumber"] = series
        }, token: token);

    public async Task<SavedPlate> VerifySavedPlateAsync(SavedPlate plate, string serviceId, CancellationToken token = default)
    {
        var response = await ReservationAvailabilityAsync(serviceId, plate.TransferCode, plate.Number, plate.Series, token);
        if (response.Int("status") == 401) throw new QeshmApiException("نشست ورود معتبر نیست. دوباره وارد شوید.", HttpStatusCode.Unauthorized);
        var vehicle = response.Object("vehicleInfo") ?? throw new QeshmApiException("سامانه مشخصات پلاک را تأیید نکرد.");
        var transfer = JsonHelpers.NormalizeDigits(vehicle.Text("transferCode")); var number = JsonHelpers.NormalizeDigits(vehicle.Text("plkNumber")); var series = JsonHelpers.NormalizeDigits(vehicle.Text("plkSeriesNumber"));
        if (transfer != plate.TransferCode || number != plate.Number || (plate.Series.Length > 0 && series != plate.Series)) throw new QeshmApiException("کد شماره‌گذاری، پلاک یا سری با اطلاعات سامانه مطابقت ندارد.");
        return plate with { Series = series };
    }

    public async Task<List<ServiceOption>> GetReservationServiceTypesAsync(CancellationToken token = default)
    {
        _log("GET /panel/pelak/reservation (لیست نوع خدمت)");
        string html;
        try { html = await _http.GetStringAsync(Url("/panel/pelak/reservation"), token); }
        catch (Exception ex) { throw new QeshmApiException($"دریافت نوع خدمت‌ها ناموفق بود: {ex.Message}", inner: ex); }
        var normalized = WebUtility.HtmlDecode(html);
        for (var i = 0; i < 3; i++) normalized = normalized.Replace("\\\"", "\"");
        var results = new List<ServiceOption>();
        var cursor = 0;
        while (cursor < normalized.Length)
        {
            var key = normalized.IndexOf("\"sessionTypes\"", cursor, StringComparison.Ordinal);
            if (key < 0) break;
            var start = normalized.IndexOf('[', key + 14); if (start < 0) break;
            var end = MatchingArrayEnd(normalized, start);
            if (end > start)
            {
                try
                {
                    if (JsonNode.Parse(normalized[start..(end + 1)]) is JsonArray array)
                    {
                        foreach (var row in array.OfType<JsonObject>())
                        {
                            var id = row.Text("id"); var name = row.Text("sessionTypeFa", "name", "label"); var active = row.TextOr("1", "isActive");
                            if (id.Length > 0 && name.Length > 0 && active is not ("0" or "false" or "inactive") && results.All(x => x.Id != id)) results.Add(new() { Id = id, Name = Regex.Replace(name, "\\s+", " ") });
                        }
                    }
                }
                catch { }
                if (results.Count > 0) break;
                cursor = end + 1;
            }
            else cursor = start + 1;
        }
        foreach (Match match in Regex.Matches(normalized, "\\{[^{}]*?\\\"id\\\"\\s*:\\s*\\\"?(?<id>[^\\\",}]+)\\\"?[^{}]*?\\\"sessionTypeFa\\\"\\s*:\\s*\\\"(?<name>[^\\\"]+)\\\"[^{}]*?\\}", RegexOptions.Singleline))
        {
            var id = match.Groups["id"].Value.Trim();
            var name = Regex.Unescape(match.Groups["name"].Value).Trim();
            if (id.Length > 0 && name.Length > 0 && results.All(x => x.Id != id)) results.Add(new() { Id = id, Name = name });
        }
        if (results.Count == 0)
        {
            var select = Regex.Match(html, "<select[^>]*name=[\\\"']sessionTypeId[\\\"'][^>]*>(?<body>.*?)</select>", RegexOptions.IgnoreCase | RegexOptions.Singleline).Groups["body"].Value;
            foreach (Match match in Regex.Matches(select, "<option[^>]*value=[\\\"'](?<id>[^\\\"']+)[\\\"'][^>]*>(?<name>.*?)</option>", RegexOptions.IgnoreCase | RegexOptions.Singleline))
            {
                var name = Regex.Replace(WebUtility.HtmlDecode(match.Groups["name"].Value), "<.*?>", "").Trim();
                if (name.Length > 0) results.Add(new() { Id = match.Groups["id"].Value.Trim(), Name = name });
            }
        }
        if (results.Count == 0) throw new QeshmApiException("لیست نوع خدمت در صفحه رزرو پیدا نشد. نشست ورود را بررسی کنید.");
        return results;
    }

    private static int MatchingArrayEnd(string text, int start)
    {
        var depth = 0; var quote = '\0'; var escaped = false;
        for (var i = start; i < text.Length; i++)
        {
            var ch = text[i];
            if (quote != '\0') { if (escaped) escaped = false; else if (ch == '\\') escaped = true; else if (ch == quote) quote = '\0'; continue; }
            if (ch is '\"' or '\'') { quote = ch; continue; }
            if (ch == '[') depth++; else if (ch == ']' && --depth == 0) return i;
        }
        return -1;
    }

    public Task<JsonObject> ConfirmReservationAsync(JsonObject search, JsonObject selected, CancellationToken token = default)
    {
        var sessionId = selected.Text("id", "sessionId", "session_id");
        var date = selected.Text("sessionDate", "date", "dateFa");
        if (string.IsNullOrWhiteSpace(sessionId) || string.IsNullOrWhiteSpace(date)) throw new QeshmApiException("شناسه یا تاریخ نوبت انتخاب‌شده معتبر نیست.");
        return JsonRequestAsync(HttpMethod.Put, "/api/private/pelak/reservation", new JsonObject
        {
            ["sessionId"] = sessionId, ["sessionDate"] = date, ["plkNumber"] = search.Text("plkNumber"),
            ["plkSeriesNumber"] = search.Text("plkSeriesNumber"), ["transferCode"] = search.Text("transferCode"),
            ["sessionTypeId"] = search.Text("sessionTypeId")
        }, token: token);
    }

    public async Task<JsonObject> CreatePlateAsync(string p1, string p2, string p3, string p4, string front, string back, CancellationToken token = default) =>
        await JsonRequestAsync(HttpMethod.Put, "/api/private/pol-laft/plk", await PlatePayloadAsync("", p1, p2, p3, p4, front, back), token: token);
    public async Task<JsonObject> UpdatePlateAsync(JsonObject row, string p1, string p2, string p3, string p4, string front, string back, CancellationToken token = default)
    {
        var id = row.Text("id");
        if (id.Length == 0) throw new QeshmApiException("شناسه پلاک برای ویرایش موجود نیست.");
        return await JsonRequestAsync(HttpMethod.Patch, "/api/private/pol-laft/plk", await PlatePayloadAsync(id, p1, p2, p3, p4, front, back), token: token);
    }
    public Task<JsonObject> DeletePlateAsync(JsonObject row, CancellationToken token = default)
    {
        var id = row.Text("id");
        if (id.Length == 0) throw new QeshmApiException("شناسه پلاک برای حذف موجود نیست.");
        return JsonRequestAsync(HttpMethod.Delete, "/api/private/pol-laft/plk", new JsonObject { ["id"] = id }, token: token);
    }
    private static async Task<JsonObject> PlatePayloadAsync(string id, string p1, string p2, string p3, string p4, string front, string back)
    {
        ValidatePlateParts(p1, p2, p3, p4);
        return new JsonObject
        {
            ["id"] = id, ["plk1"] = p1, ["plk2"] = p2, ["plk3"] = p3, ["plk4"] = p4,
            ["img1"] = await ImageEncoding.AsSiteImageValueAsync(front), ["img2"] = await ImageEncoding.AsSiteImageValueAsync(back)
        };
    }
    public static void ValidatePlateParts(string p1, string p2, string p3, string p4)
    {
        if (!Regex.IsMatch(p1, "^[0-9]{2}$")) throw new QeshmApiException("بخش اول پلاک باید دقیقاً ۲ رقم باشد.");
        if (!Regex.IsMatch(p2, "^[\\u0600-\\u06FF]$")) throw new QeshmApiException("حرف پلاک باید دقیقاً یک حرف فارسی باشد.");
        if (!Regex.IsMatch(p3, "^[0-9]{3}$")) throw new QeshmApiException("بخش سه‌رقمی پلاک باید دقیقاً ۳ رقم باشد.");
        if (!Regex.IsMatch(p4, "^[0-9]{2}$")) throw new QeshmApiException("کد ایران پلاک باید دقیقاً ۲ رقم باشد.");
    }

    public List<CookieInfo> ExportCookies() => _cookies.GetAllCookies().Cast<Cookie>().Select(c => new CookieInfo
    {
        Name = c.Name, Value = c.Value, Domain = c.Domain, Path = c.Path, Secure = c.Secure,
        Expires = c.Expires == DateTime.MinValue ? null : new DateTimeOffset(c.Expires.ToUniversalTime()).ToUnixTimeSeconds()
    }).ToList();
    public void ImportCookies(IEnumerable<CookieInfo> items)
    {
        foreach (var item in items)
        {
            try
            {
                var domain = string.IsNullOrWhiteSpace(item.Domain) ? new Uri(BaseUrl).Host : item.Domain;
                var cookie = new Cookie(item.Name, item.Value, string.IsNullOrWhiteSpace(item.Path) ? "/" : item.Path, domain) { Secure = item.Secure };
                if (item.Expires is long expires) cookie.Expires = DateTimeOffset.FromUnixTimeSeconds(expires).UtcDateTime;
                _cookies.Add(cookie);
            }
            catch { }
        }
    }
    public void ClearAuthCookies()
    {
        foreach (Cookie c in _cookies.GetAllCookies()) if (c.Name.Contains("next-auth", StringComparison.OrdinalIgnoreCase) || c.Name.StartsWith("__Host-") || c.Name.StartsWith("__Secure-")) c.Expired = true;
    }
    public async Task<byte[]> DownloadAsync(string path, CancellationToken token = default) => await _http.GetByteArrayAsync(Url(path), token);
    public static JsonObject SessionUser(JsonObject? session) => session?.Object("user") ?? [];
    public static bool SessionIsValid(JsonObject? session) => SessionUser(session).Text("userId").Length > 0;
    public static JsonObject ExtractPerson(JsonObject data)
    {
        if (data["person"] is JsonObject direct) return direct;
        foreach (var key in new[] { "data", "body", "result" })
        {
            if (data[key] is not JsonObject wrapper) continue;
            if (wrapper["person"] is JsonObject person) return person;
            if (new[] { "FirstName", "LastName", "MelliCode", "CellPhone", "userPic" }.Any(wrapper.ContainsKey)) return wrapper;
        }
        return new[] { "FirstName", "LastName", "MelliCode", "CellPhone", "userPic" }.Any(data.ContainsKey) ? data : [];
    }
    public static string DetectCaptchaCode(string svg)
    {
        try
        {
            var doc = XDocument.Parse(svg);
            var digits = doc.Descendants().Where(x => x.Name.LocalName.Equals("text", StringComparison.OrdinalIgnoreCase))
                .Select((x, i) => new { X = double.TryParse(Regex.Match(x.Attribute("x")?.Value ?? "", "-?\\d+(?:\\.\\d+)?").Value, out var n) ? n : i, Value = JsonHelpers.NormalizeDigits(x.Value) })
                .Where(x => Regex.IsMatch(x.Value, "^[0-9]$")).OrderBy(x => x.X).Select(x => x.Value);
            var code = string.Concat(digits);
            return Regex.IsMatch(code, "^[0-9]{5}$") ? code : "";
        }
        catch { return ""; }
    }
    public void Dispose() { _http.Dispose(); _handler.Dispose(); }
}

public static class ImageEncoding
{
    public static Task<string> AsSiteImageValueAsync(string value) => Task.Run(() =>
    {
        if (string.IsNullOrWhiteSpace(value) || !File.Exists(value)) return value?.Trim() ?? "";
        using var input = File.OpenRead(value);
        var decoder = BitmapDecoder.Create(input, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
        BitmapSource source = decoder.Frames[0];
        var scale = Math.Min(1d, 8000d / Math.Max(source.PixelWidth, source.PixelHeight));
        if (scale < 1d) source = new TransformedBitmap(source, new ScaleTransform(scale, scale));
        if (source.Format != PixelFormats.Bgr24 && source.Format != PixelFormats.Bgra32 && source.Format != PixelFormats.Pbgra32)
            source = new FormatConvertedBitmap(source, PixelFormats.Bgra32, null, 0);
        var encoder = new JpegBitmapEncoder { QualityLevel = 70 };
        encoder.Frames.Add(BitmapFrame.Create(source));
        using var output = new MemoryStream();
        encoder.Save(output);
        return "data:image/jpeg;base64," + Convert.ToBase64String(output.ToArray());
    });
}
