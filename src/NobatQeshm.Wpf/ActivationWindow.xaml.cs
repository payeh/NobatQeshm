using System.Security.Cryptography;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;

namespace NobatQeshm.Wpf;

public partial class ActivationWindow : Window
{
    private readonly ActivationService _activation;
    private readonly CancellationTokenSource _windowLifetime = new();
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(1) };
    private bool _isSending;
    private bool _isClosed;

    internal ActivationWindow(ActivationService activation)
    {
        _activation = activation;
        InitializeComponent();
        DestinationBox.ItemsSource = ActivationService.Destinations;
        DestinationBox.SelectedIndex = 0;
        _timer.Tick += (_, _) => UpdateControls();
        Loaded += (_, _) => { UpdateControls(); _timer.Start(); };
        Closed += (_, _) =>
        {
            _isClosed = true;
            _timer.Stop();
            _windowLifetime.Cancel();
            _windowLifetime.Dispose();
        };
    }

    private string? Destination => DestinationBox.SelectedItem as string;

    private async void SendCodeButton_Click(object sender, RoutedEventArgs e)
    {
        if (_isSending || Destination is not { } destination) return;
        _isSending = true;
        CodeBox.Clear();
        UpdateControls();
        SetStatus("در حال ارسال کد فعال‌سازی…");
        try
        {
            await _activation.SendCodeAsync(destination, _windowLifetime.Token);
            if (_isClosed) return;
            SetStatus("کد ارسال شد. کد پنج‌رقمی را تا پنج دقیقهٔ دیگر وارد کنید.");
        }
        catch (OperationCanceledException) when (_isClosed) { }
        catch (InvalidOperationException error)
        {
            if (!_isClosed) SetStatus(error.Message, isError: true);
        }
        finally
        {
            _isSending = false;
            if (!_isClosed)
            {
                UpdateControls();
                if (CodeBox.IsEnabled) CodeBox.Focus();
            }
        }
    }

    private void ActivateButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (_activation.TryActivate(Destination, CodeBox.Text))
            {
                DialogResult = true;
                return;
            }
            SetStatus("کد نادرست است یا اعتبار آن تمام شده است. در صورت نیاز دوباره کد بگیرید.", isError: true);
            UpdateControls();
            if (CodeBox.IsEnabled) CodeBox.SelectAll();
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or CryptographicException)
        {
            SetStatus("ذخیرهٔ فعال‌سازی انجام نشد. دوباره تلاش کنید.", isError: true);
        }
    }

    private void DestinationBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        CodeBox.Clear();
        SetStatus(_activation.HasCodeFor(Destination)
            ? "کد ارسال‌شده به این شماره را وارد کنید."
            : "برای این شماره کد فعال‌سازی درخواست کنید.");
        UpdateControls();
    }

    private void CodeBox_TextChanged(object sender, TextChangedEventArgs e) => UpdateControls();

    private void UpdateControls()
    {
        var remaining = _activation.ResendRemaining();
        SendCodeButton.IsEnabled = !_isSending && Destination is not null && remaining == TimeSpan.Zero;
        SendCodeButton.Content = remaining > TimeSpan.Zero
            ? $"ارسال مجدد کد ({Math.Ceiling(remaining.TotalSeconds)} ثانیه)"
            : "ارسال کد فعال‌سازی";
        DestinationBox.IsEnabled = !_isSending;
        CodeBox.IsEnabled = !_isSending && _activation.HasCodeFor(Destination);
        var digits = JsonHelpers.NormalizeDigits(CodeBox.Text);
        ActivateButton.IsEnabled = CodeBox.IsEnabled && digits.Length == ActivationService.CodeLength && digits.All(char.IsAsciiDigit);
    }

    private void SetStatus(string message, bool isError = false)
    {
        StatusText.Text = message;
        StatusText.Foreground = isError ? Brushes.Firebrick : (Brush)FindResource("MutedBrush");
    }
}
