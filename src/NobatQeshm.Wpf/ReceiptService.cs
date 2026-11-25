using System.Diagnostics;
using System.Text;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace NobatQeshm.Wpf;

public static class ReceiptService
{
    private static readonly Lazy<string> Template = new(() =>
    {
        using var stream = typeof(ReceiptService).Assembly.GetManifestResourceStream("NobatQeshm.ReceiptTemplate.html")
            ?? throw new InvalidOperationException("فایل قالب رسید در برنامه پیدا نشد.");
        using var reader = new StreamReader(stream, Encoding.UTF8);
        return reader.ReadToEnd().ReplaceLineEndings("\n");
    });

    private static readonly Regex Placeholder = new(@"\$(?:\{(?<key>[a-z_]+)\}|(?<key>[a-z_]+)|(?<literal>\$))");

    public static string BuildHtml(JsonObject data, ReceiptAdvertising advertising, bool printOnOpen = false)
    {
        string Value(params string[] keys)
        {
            foreach (var key in keys)
            {
                var node = data[key];
                if (node is null) continue;
                var value = node is JsonValue scalar && scalar.TryGetValue<string>(out var text)
                    ? text : node.ToJsonString();
                if (value is not ("" or "null")) return value;
            }
            return "—";
        }

        var date = Value("sessionDate", "reservationDate");
        var dateLabel = "تاریخ نوبت";
        if (date == "—" && Value("createdAt") is var createdAt && createdAt != "—")
        {
            date = createdAt;
            dateLabel = "زمان ثبت";
        }

        var advertisingText = advertising.VisibleText;
        var variables = new Dictionary<string, string>
        {
            ["date_label"] = Escape(dateLabel),
            ["session_date"] = Escape(date),
            ["time_range"] = Escape($"{Value("startTime")} - {Value("endTime")}"),
            ["counter"] = Escape($"باجه {Value("counterNumber")}"),
            ["transfer"] = Escape(Value("transferCode", "vehicleTransferCode")),
            ["plate"] = Escape(Value("plkNumber", "vehiclePlk")),
            ["series"] = Escape(Value("plkSeriesNumber", "vehiclePlkSeries")),
            ["code"] = Escape(Value("code", "reservationCode")),
            ["service_name"] = Escape(Value("sessionTypeFa")),
            ["advertising_block"] = advertisingText.Length > 0
                ? $"<aside id=\"receipt-advertising\">{Escape(advertisingText)}</aside>" : "",
            ["auto_print"] = printOnOpen
                ? "<script>window.addEventListener(\"load\", () => window.print(), {once: true});</script>" : ""
        };
        return Placeholder.Replace(Template.Value, match =>
            match.Groups["literal"].Success ? "$" : variables[match.Groups["key"].Value]);
    }

    private static string Escape(string value) => value.Replace("&", "&amp;").Replace("<", "&lt;")
        .Replace(">", "&gt;").Replace("\"", "&quot;").Replace("'", "&#x27;");

    public static string SaveToDesktop(JsonObject data, string customerName, string nationalCode, ReceiptAdvertising advertising)
    {
        var desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
        var name = SafeFilePart(customerName, "نام نامشخص");
        var code = SafeFilePart(nationalCode, "کد ملی نامشخص");
        var html = BuildHtml(data, advertising);
        for (var i = 1; ; i++)
        {
            var suffix = i == 1 ? "" : $" ({i})";
            var path = Path.Combine(desktop, $"{name} - {code}{suffix}.html");
            FileStream stream;
            try { stream = File.Open(path, FileMode.CreateNew, FileAccess.Write); }
            catch (IOException) when (File.Exists(path)) { continue; }

            using (stream)
            using (var writer = new StreamWriter(stream, new UTF8Encoding(false)))
                writer.Write(html);
            return path;
        }
    }

    public static string SafeFilePart(string value, string fallback)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var text = new string(value.Select(c => invalid.Contains(c) ? '-' : c).ToArray()).Trim(' ', '-', '.');
        return text.Length == 0 ? fallback : text[..Math.Min(90, text.Length)];
    }

    public static void OpenInBrowser(JsonObject data, ReceiptAdvertising advertising, bool printOnOpen = false)
    {
        var path = Path.Combine(Path.GetTempPath(), $"qeshm-receipt-{Guid.NewGuid():N}.html");
        File.WriteAllText(path, BuildHtml(data, advertising, printOnOpen), new UTF8Encoding(false));
        Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
    }
}
