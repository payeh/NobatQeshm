using System.Diagnostics;
using System.Net;
using System.Text;
using System.Text.Json.Nodes;

namespace NobatQeshm.Wpf;

public static class ReceiptService
{
    private static string V(JsonObject data, params string[] keys) => WebUtility.HtmlEncode(data.TextOr("—", keys));
    public static string BuildHtml(JsonObject data, ReceiptAdvertising advertising, bool printOnOpen = false)
    {
        var date = data.Text("sessionDate", "reservationDate"); var dateLabel = "تاریخ نوبت";
        if (date.Length == 0) { date = data.TextOr("—", "createdAt"); dateLabel = "زمان ثبت"; }
        var ad = advertising.VisibleText.Length == 0 ? "" : $"<aside>{WebUtility.HtmlEncode(advertising.VisibleText)}</aside>";
        var print = printOnOpen ? "<script>window.onload=()=>window.print()</script>" : "";
        var html = """
<!doctype html><html lang="fa" dir="rtl"><head><meta charset="utf-8"><title>رسید نوبت قشم</title>
<style>body{margin:0;background:#eef2f8;font-family:Tahoma,Arial;color:#0f172a}.wrap{max-width:760px;margin:24px auto;background:white;border:1px solid #dbe3ef;border-radius:12px;overflow:hidden}.head{background:#3730a3;color:white;padding:22px}.head small{color:#c7d2fe}.badge{float:left;background:#0f766e;color:#d1fae5;padding:7px 12px;border-radius:6px}.body{padding:22px}.grid{display:grid;grid-template-columns:1fr 1fr;gap:10px}.box{background:#f8fafc;border:1px solid #eef2f7;border-radius:7px;padding:12px}.label{color:#64748b;font-size:12px}.value{font-weight:bold;font-size:17px;margin-top:5px}.plate{font-size:30px}.code{background:#0f172a;color:#facc15;padding:18px;font:bold 30px Consolas;margin-top:18px;border-radius:7px}aside{white-space:pre-line;text-align:center;font-weight:bold;margin:18px auto;max-width:760px}footer{text-align:center;color:#64748b;font-size:12px;padding:12px}@media print{body{background:white}.wrap{margin:0 auto;border:0}button{display:none}}</style></head><body>
<main class="wrap"><header class="head"><span class="badge">%%SERVICE%%</span><small>رسید نهایی</small><h2>اطلاعات نوبت رزرو شده</h2></header><section class="body">
<div class="grid"><div class="box"><div class="label">%%DATE_LABEL%%</div><div class="value">%%DATE%%</div></div><div class="box"><div class="label">ساعت حضور</div><div class="value">%%START%% تا %%END%%</div></div>
<div class="box"><div class="label">شماره باجه</div><div class="value">%%COUNTER%%</div></div><div class="box"><div class="label">کد شماره‌گذاری</div><div class="value">%%TRANSFER%%</div></div></div>
<div class="box" style="margin-top:12px"><div class="label">پلاک خودرو</div><div class="value plate">%%PLATE%% / سری %%SERIES%%</div></div>
<div class="code"><div class="label">کد نهایی رزرو شما</div>%%CODE%%</div></section><footer>ارائه این کد به همراه کارت شناسایی به مسئول باجه الزامی است.</footer></main>%%AD%%%%PRINT%%</body></html>
""";
        return html.Replace("%%SERVICE%%", V(data, "sessionTypeFa"))
            .Replace("%%DATE_LABEL%%", dateLabel).Replace("%%DATE%%", WebUtility.HtmlEncode(date))
            .Replace("%%START%%", V(data, "startTime")).Replace("%%END%%", V(data, "endTime"))
            .Replace("%%COUNTER%%", V(data, "counterNumber")).Replace("%%TRANSFER%%", V(data, "transferCode", "vehicleTransferCode"))
            .Replace("%%PLATE%%", V(data, "plkNumber", "vehiclePlk")).Replace("%%SERIES%%", V(data, "plkSeriesNumber", "vehiclePlkSeries"))
            .Replace("%%CODE%%", V(data, "code", "reservationCode")).Replace("%%AD%%", ad).Replace("%%PRINT%%", print);
    }
    public static string SaveToDesktop(JsonObject data, string customerName, string nationalCode, ReceiptAdvertising advertising)
    {
        static string Safe(string value, string fallback) { var invalid = Path.GetInvalidFileNameChars(); var text = new string(value.Select(c => invalid.Contains(c) ? '-' : c).ToArray()).Trim(' ', '-', '.'); return text.Length == 0 ? fallback : text[..Math.Min(90, text.Length)]; }
        var desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory); var name = Safe(customerName, "نام نامشخص"); var code = Safe(nationalCode, "کد ملی نامشخص");
        for (var i = 1; ; i++) { var path = Path.Combine(desktop, $"{name} - {code}{(i == 1 ? "" : $" ({i})")}.html"); if (File.Exists(path)) continue; File.WriteAllText(path, BuildHtml(data, advertising), new UTF8Encoding(false)); return path; }
    }
    public static void OpenPrint(JsonObject data, ReceiptAdvertising advertising)
    {
        var path = Path.Combine(Path.GetTempPath(), $"qeshm-receipt-{Guid.NewGuid():N}.html"); File.WriteAllText(path, BuildHtml(data, advertising, true), new UTF8Encoding(false));
        Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
    }
}
