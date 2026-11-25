using System.Globalization;
using System.Net.Http;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace NobatQeshm.Wpf;

internal static class ActivationSms
{
    private const int BodyId = 307880;
    private static readonly byte[] KeyPurpose = Encoding.UTF8.GetBytes("NobatQeshm.SmsKey.v1");
    private static readonly HttpClient Http = new(new HttpClientHandler { AllowAutoRedirect = false })
    {
        Timeout = TimeSpan.FromSeconds(30)
    };

    internal static async Task SendAsync(string destination, string code, CancellationToken cancellationToken)
    {
        var key = ReadKey();
        if (key.Length == 0)
            throw new InvalidOperationException("کلید پنل ملی پیامک روی این حساب ویندوز تنظیم نشده است.");

        using var request = new HttpRequestMessage(HttpMethod.Post,
            $"https://console.melipayamak.com/api/send/shared/{Uri.EscapeDataString(key)}")
        {
            Content = JsonContent.Create(new { bodyId = BodyId, to = destination, args = new[] { code } })
        };
        try
        {
            using var response = await Http.SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode)
                throw new InvalidOperationException($"پنل پیامک ارسال را تأیید نکرد (HTTP {(int)response.StatusCode}).");

            using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
            if (json.RootElement.ValueKind != JsonValueKind.Object ||
                !IsPositive(json.RootElement, "recId") && !IsPositive(json.RootElement, "status"))
                throw new InvalidOperationException("پنل پیامک شمارهٔ پیگیری معتبر برنگرداند.");
        }
        catch (HttpRequestException)
        {
            throw new InvalidOperationException("اتصال به پنل ملی پیامک انجام نشد.");
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new InvalidOperationException("زمان پاسخ‌گویی پنل پیامک به پایان رسید.");
        }
        catch (JsonException)
        {
            throw new InvalidOperationException("پاسخ پنل پیامک قابل خواندن نبود.");
        }
    }

    private static bool IsPositive(JsonElement result, string key) =>
        result.TryGetProperty(key, out var value) &&
        long.TryParse(value.ToString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var number) &&
        number > 0;

    private static string ReadKey()
    {
        if (Environment.GetEnvironmentVariable("NOBATQESHM_SMS_API_KEY")?.Trim() is { Length: > 0 } environmentKey)
            return environmentKey;
        if (!File.Exists(AppPaths.ActivationSmsKey)) return "";

        try
        {
            var plain = ProtectedData.Unprotect(File.ReadAllBytes(AppPaths.ActivationSmsKey), KeyPurpose, DataProtectionScope.CurrentUser);
            try { return Encoding.UTF8.GetString(plain).Trim(); }
            finally { CryptographicOperations.ZeroMemory(plain); }
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or CryptographicException)
        {
            throw new InvalidOperationException("تنظیم محلی پنل پیامک قابل خواندن نیست.");
        }
    }
}
