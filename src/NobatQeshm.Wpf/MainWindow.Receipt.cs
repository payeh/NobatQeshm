using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;
using Microsoft.Win32;
using System.Text.Json;
using System.Windows;
using System.Windows.Media.Imaging;

namespace NobatQeshm.Wpf;

public partial class MainWindow
{
    private WebView2? _receiptPreview;
    private Task<CoreWebView2>? _receiptBrowserTask;
    private string? _previewHtml;
    private bool _receiptReady;
    private bool _copyingReceipt;

    private void ReceiptHost_Loaded(object sender, RoutedEventArgs e) => RenderReceipt();

    private async void RenderReceipt()
    {
        var hasReceipt = _lastReceipt.Count > 0;
        PrintReceiptButton.IsEnabled = hasReceipt;
        OpenReceiptButton.IsEnabled = hasReceipt;
        SaveReceiptButton.IsEnabled = hasReceipt;
        CopyReceiptCodeButton.IsEnabled = hasReceipt;
        if (!hasReceipt || !ReceiptHost.IsLoaded || !ReceiptTab.IsSelected) return;

        try
        {
            var browser = await (_receiptBrowserTask ??= InitializeReceiptBrowserAsync());
            if (_closing) return;
            var html = ReceiptService.BuildHtml(_lastReceipt, _advertising);
            if (html == _previewHtml) return;
            _previewHtml = html;
            _receiptReady = false;
            CopyReceiptImageButton.IsEnabled = false;
            // The desktop tab already provides its own receipt commands.
            browser.NavigateToString(html.Replace("</head>", "<style>#receipt-actions{display:none}</style>\n</head>"));
        }
        catch (Exception ex)
        {
            if (_closing) return;
            _receiptReady = false;
            CopyReceiptImageButton.IsEnabled = false;
            ReceiptPreviewStatus.Text = "پیش‌نمایش داخل برنامه در دسترس نیست. از «باز کردن در مرورگر» استفاده کنید.";
            ReceiptPreviewStatus.Visibility = Visibility.Visible;
            Log($"پیش‌نمایش رسید: {ex.Message}");
        }
    }

    private async Task<CoreWebView2> InitializeReceiptBrowserAsync()
    {
        var environment = await CoreWebView2Environment.CreateAsync(
            userDataFolder: Path.Combine(AppPaths.DataDirectory, "ReceiptWebView"));
        var view = new WebView2 { FlowDirection = FlowDirection.LeftToRight };
        _receiptPreview = view;
        ReceiptHost.Content = view;
        await view.EnsureCoreWebView2Async(environment);
        var browser = view.CoreWebView2;
        browser.Settings.AreDefaultContextMenusEnabled = false;
        browser.Settings.AreDevToolsEnabled = false;
        browser.NavigationCompleted += (_, args) =>
        {
            if (args.WebErrorStatus == CoreWebView2WebErrorStatus.OperationCanceled) return;
            _receiptReady = args.IsSuccess;
            CopyReceiptImageButton.IsEnabled = _receiptReady && !_copyingReceipt;
            ReceiptPreviewStatus.Text = _receiptReady ? "" : "بارگذاری پیش‌نمایش رسید ناموفق بود.";
            ReceiptPreviewStatus.Visibility = _receiptReady ? Visibility.Collapsed : Visibility.Visible;
        };
        return browser;
    }

    private void CopyReceiptCode_Click(object sender, RoutedEventArgs e)
    {
        var code = _lastReceipt.Text("code", "reservationCode");
        if (code.Length == 0) { Warn("کد رزرو در رسید موجود نیست."); return; }
        Clipboard.SetText(code);
        SetStatus("کد رزرو کپی شد.");
    }

    private async void CopyReceiptImage_Click(object sender, RoutedEventArgs e)
    {
        if (!_receiptReady || _copyingReceipt || _receiptPreview?.CoreWebView2 is not { } browser) return;
        _copyingReceipt = true;
        CopyReceiptImageButton.IsEnabled = false;
        try
        {
            var image = await CaptureReceiptAsync(browser);
            Clipboard.SetImage(image);
            SetStatus("عکس رسید در کلیپ‌بورد کپی شد.");
        }
        catch (Exception ex)
        {
            Log($"کپی تصویر رسید: {ex.Message}");
            Warn("کپی تصویر رسید انجام نشد. دوباره امتحان کنید.");
        }
        finally
        {
            _copyingReceipt = false;
            CopyReceiptImageButton.IsEnabled = _receiptReady;
        }
    }

    private static async Task<BitmapSource> CaptureReceiptAsync(CoreWebView2 browser)
    {
        const string boundsScript = """
            (async () => {
                await document.fonts.ready;
                const rect = document.getElementById("receipt-image-area").getBoundingClientRect();
                const x = Math.floor(rect.left + scrollX);
                const y = Math.floor(rect.top + scrollY);
                return {
                    x, y, width: Math.ceil(rect.right + scrollX) - x,
                    height: Math.ceil(rect.bottom + scrollY) - y, scale: 2
                };
            })()
            """;
        using var evaluation = JsonDocument.Parse(await browser.CallDevToolsProtocolMethodAsync("Runtime.evaluate",
            JsonSerializer.Serialize(new { expression = boundsScript, awaitPromise = true, returnByValue = true })));
        var clip = evaluation.RootElement.GetProperty("result").GetProperty("value");
        using var screenshot = JsonDocument.Parse(await browser.CallDevToolsProtocolMethodAsync("Page.captureScreenshot",
            JsonSerializer.Serialize(new { format = "png", captureBeyondViewport = true, clip })));
        var bytes = Convert.FromBase64String(screenshot.RootElement.GetProperty("data").GetString()!);
        using var stream = new MemoryStream(bytes);
        return BitmapFrame.Create(stream, BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
    }

    private void PrintReceipt_Click(object sender, RoutedEventArgs e) => OpenReceiptInBrowser(print: true);
    private void OpenReceipt_Click(object sender, RoutedEventArgs e) => OpenReceiptInBrowser(print: false);

    private void OpenReceiptInBrowser(bool print)
    {
        if (_lastReceipt.Count == 0) return;
        try { ReceiptService.OpenInBrowser(_lastReceipt, _advertising, print); }
        catch (Exception ex) { Log(ex.Message); Warn("باز کردن رسید در مرورگر انجام نشد."); }
    }

    private void SaveReceipt_Click(object sender, RoutedEventArgs e)
    {
        if (_lastReceipt.Count == 0) return;
        var code = ReceiptService.SafeFilePart(_lastReceipt.Text("code", "reservationCode"), "receipt");
        var dialog = new SaveFileDialog
        {
            Filter = "HTML|*.html", DefaultExt = ".html", FileName = $"qeshm-reservation-{code}.html"
        };
        if (dialog.ShowDialog(this) != true) return;
        try
        {
            File.WriteAllText(dialog.FileName, ReceiptService.BuildHtml(_lastReceipt, _advertising));
            SetStatus("رسید HTML ذخیره شد.");
        }
        catch (Exception ex) { Log(ex.Message); Warn("ذخیرهٔ رسید انجام نشد."); }
    }

    private void ReceiptAdvertising_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new TextEditDialog(_advertising) { Owner = this };
        if (dialog.ShowDialog() != true) return;
        try
        {
            AdvertisingStore.Save(dialog.Value);
            _advertising = dialog.Value;
            RenderReceipt();
        }
        catch (Exception ex) { Log(ex.Message); Warn("ذخیرهٔ تنظیمات تبلیغات انجام نشد."); }
    }
}
