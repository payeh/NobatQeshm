using System.Globalization;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;

namespace NobatQeshm.Wpf;

internal static class ActivationSms
{
    private const int BodyId = 307880;
    private static readonly Lazy<string> Key = new(ReadEmbeddedKey);
    private static readonly HttpClient Http = new(new HttpClientHandler { AllowAutoRedirect = false })
    {
        Timeout = TimeSpan.FromSeconds(30)
    };

    internal static async Task SendAsync(string destination, string code, CancellationToken cancellationToken)
    {
        var key = Key.Value;
        if (key.Length == 0)
            throw new InvalidOperationException("این خروجی کلید پیامک ندارد. نسخهٔ آمادهٔ توزیع را اجرا کنید.");

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

    private static string ReadEmbeddedKey()
    {
        using var resource = typeof(ActivationSms).Assembly.GetManifestResourceStream("NobatQeshm.ActivationSmsKey");
        if (resource is null) return "";
        using var reader = new StreamReader(resource);
        return reader.ReadToEnd().Trim();
    }
}
