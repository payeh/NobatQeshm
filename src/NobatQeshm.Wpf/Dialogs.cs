using Microsoft.Win32;
using System.Collections.ObjectModel;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace NobatQeshm.Wpf;

public sealed class PlateEditorWindow : Window
{
    private readonly TextBox _p1 = Box(2), _p2 = Box(1), _p3 = Box(3), _p4 = Box(2);
    private readonly TextBox _front = new(), _back = new();
    public string Part1 => JsonHelpers.NormalizeDigits(_p1.Text);
    public string Part2 => _p2.Text.Trim();
    public string Part3 => JsonHelpers.NormalizeDigits(_p3.Text);
    public string Part4 => JsonHelpers.NormalizeDigits(_p4.Text);
    public string FrontImage => _front.Text.Trim();
    public string BackImage => _back.Text.Trim();

    public PlateEditorWindow(JsonObjectAdapter? row = null)
    {
        Title = row is null ? "افزودن پلاک جدید" : "ویرایش پلاک";
        Width = 830; Height = 665; MinWidth = 720; MinHeight = 565;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = (Brush)Application.Current.Resources["PageBrush"];
        FlowDirection = FlowDirection.RightToLeft;
        if (row is not null)
        {
            _p1.Text = row.Text("plk1"); _p2.Text = row.Text("plk2"); _p3.Text = row.Text("plk3"); _p4.Text = row.Text("plk4");
            _front.Text = row.Text("url_img1"); _back.Text = row.Text("url_img2");
        }
        var root = new Grid { Margin = new Thickness(18) };
        root.RowDefinitions.Add(new() { Height = GridLength.Auto }); root.RowDefinitions.Add(new() { Height = GridLength.Auto }); root.RowDefinitions.Add(new() { Height = new(1, GridUnitType.Star) }); root.RowDefinitions.Add(new() { Height = GridLength.Auto });
        root.Children.Add(new TextBlock { Text = Title, FontSize = 20, FontWeight = FontWeights.Bold, Margin = new Thickness(4, 4, 4, 14) });
        var plate = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        plate.Children.Add(new Label { Content = "پلاک" });
        foreach (var box in new[] { _p1, _p2, _p3, _p4 }) plate.Children.Add(box);
        Grid.SetRow(plate, 1); root.Children.Add(plate);
        var images = new Grid { Margin = new Thickness(0, 14, 0, 8) };
        images.ColumnDefinitions.Add(new()); images.ColumnDefinitions.Add(new());
        images.Children.Add(ImageCard("تصویر روی کارت خودرو", _front, 0)); images.Children.Add(ImageCard("تصویر پشت کارت خودرو", _back, 1));
        Grid.SetRow(images, 2); root.Children.Add(images);
        var footer = new StackPanel { Orientation = Orientation.Horizontal };
        var save = new Button { Content = "ثبت اطلاعات", Style = (Style)Application.Current.Resources["SuccessButton"] };
        save.Click += Save_Click;
        footer.Children.Add(save);
        var cancel = new Button { Content = "انصراف", Style = (Style)Application.Current.Resources["SecondaryButton"] }; cancel.Click += (_, _) => DialogResult = false;
        footer.Children.Add(cancel);
        Grid.SetRow(footer, 3); root.Children.Add(footer);
        Content = root;
    }
    private static TextBox Box(int max) => new() { Width = max == 3 ? 100 : 80, MaxLength = max, TextAlignment = TextAlignment.Center, FontSize = 18, FontWeight = FontWeights.Bold };
    private Border ImageCard(string title, TextBox path, int column)
    {
        var preview = new Image { Height = 250, Stretch = Stretch.Uniform, FlowDirection = FlowDirection.LeftToRight };
        var choose = new Button { Content = "انتخاب / تغییر تصویر" };
        choose.Click += (_, _) =>
        {
            var dialog = new OpenFileDialog { Title = title, Filter = "تصاویر|*.jpg;*.jpeg;*.png;*.webp;*.bmp|همه فایل‌ها|*.*" };
            if (dialog.ShowDialog(this) != true) return;
            path.Text = dialog.FileName;
            try { preview.Source = new BitmapImage(new Uri(dialog.FileName)); } catch { }
        };
        var remove = new Button { Content = "حذف تصویر", Style = (Style)Application.Current.Resources["DangerButton"] };
        remove.Click += (_, _) => { path.Text = ""; preview.Source = null; };
        var panel = new StackPanel(); panel.Children.Add(new TextBlock { Text = title, FontWeight = FontWeights.Bold, Margin = new Thickness(4) }); panel.Children.Add(preview);
        path.IsReadOnly = true; panel.Children.Add(path); panel.Children.Add(new WrapPanel { Children = { choose, remove } });
        var border = new Border { Style = (Style)Application.Current.Resources["CardStyle"], Child = panel };
        Grid.SetColumn(border, column); return border;
    }
    private void Save_Click(object sender, RoutedEventArgs e)
    {
        try { QeshmClient.ValidatePlateParts(Part1, Part2, Part3, Part4); DialogResult = true; }
        catch (Exception ex) { MessageBox.Show(this, ex.Message, "پلاک", MessageBoxButton.OK, MessageBoxImage.Warning); }
    }
}

public sealed class JsonObjectAdapter
{
    private readonly System.Text.Json.Nodes.JsonObject _value;
    public JsonObjectAdapter(System.Text.Json.Nodes.JsonObject value) => _value = value;
    public string Text(params string[] keys) => _value.Text(keys);
}

public sealed class SavedPlatesWindow : Window
{
    private readonly SavedPlateStore _store;
    private readonly ObservableCollection<SavedPlate> _items;
    private readonly DataGrid _grid = new();
    private readonly Func<SavedPlate, SavedPlate?, Task<SavedPlate>>? _verifier;
    public Action? Changed { get; init; }
    public SavedPlatesWindow(SavedPlateStore store, Func<SavedPlate, SavedPlate?, Task<SavedPlate>>? verifier = null)
    {
        _store = store; _verifier = verifier; _items = new(store.Load());
        Title = "مدیریت پلاک‌های ذخیره‌شده"; Width = 850; Height = 480; MinWidth = 700; MinHeight = 380;
        WindowStartupLocation = WindowStartupLocation.CenterOwner; FlowDirection = FlowDirection.RightToLeft;
        var root = new Grid { Margin = new Thickness(14) }; root.RowDefinitions.Add(new() { Height = new(1, GridUnitType.Star) }); root.RowDefinitions.Add(new() { Height = GridLength.Auto });
        _grid.ItemsSource = _items; _grid.AutoGenerateColumns = false; _grid.Columns.Add(new DataGridTextColumn { Header = "نام پلاک", Binding = new System.Windows.Data.Binding("Name"), Width = new DataGridLength(2, DataGridLengthUnitType.Star) });
        _grid.Columns.Add(new DataGridTextColumn { Header = "نام خودرو", Binding = new System.Windows.Data.Binding(nameof(SavedPlate.VehicleName)), Width = new DataGridLength(1.6, DataGridLengthUnitType.Star) });
        _grid.Columns.Add(new DataGridTextColumn { Header = "کد شماره‌گذاری", Binding = new System.Windows.Data.Binding("TransferCode"), Width = new DataGridLength(1, DataGridLengthUnitType.Star) });
        _grid.Columns.Add(new DataGridTextColumn { Header = "شماره پلاک", Binding = new System.Windows.Data.Binding("Number"), Width = new DataGridLength(1, DataGridLengthUnitType.Star) });
        _grid.Columns.Add(new DataGridTextColumn { Header = "سری", Binding = new System.Windows.Data.Binding("Series"), Width = 90 });
        _grid.Columns.Add(new DataGridTextColumn { Header = "تاریخ ثبت شمسی", Binding = new System.Windows.Data.Binding("PersianSavedDate"), Width = 140 });
        foreach (var column in _grid.Columns.OfType<DataGridTextColumn>())
            column.ElementStyle = (Style)Application.Current.Resources["TableCellText"];
        root.Children.Add(_grid);
        var buttons = new DockPanel();
        var close = Make("بستن", (_, _) => Close(), "SecondaryButton"); DockPanel.SetDock(close, Dock.Left); buttons.Children.Add(close);
        buttons.Children.Add(Make("ثبت پلاک جدید", (_, _) => Edit(null), "SuccessButton")); buttons.Children.Add(Make("ویرایش", (_, _) => Edit(_grid.SelectedItem as SavedPlate)));
        buttons.Children.Add(Make("حذف", (_, _) => Delete(), "DangerButton")); Grid.SetRow(buttons, 1); root.Children.Add(buttons); Content = root;
        _grid.MouseDoubleClick += (_, _) => Edit(_grid.SelectedItem as SavedPlate);
    }
    private Button Make(string text, RoutedEventHandler click, string? style = null) { var b = new Button { Content = text }; if (style is not null) b.Style = (Style)Application.Current.Resources[style]; b.Click += click; return b; }
    private async void Edit(SavedPlate? original)
    {
        var dialog = new SavedPlateEditWindow(original) { Owner = this };
        if (dialog.ShowDialog() != true) return;
        try
        {
            var value = _verifier is null ? dialog.Value : await _verifier(dialog.Value, original);
            if (original is not null) value = value with { SavedDate = original.SavedDate };
            var list = _store.Save(value, original: original); _items.Clear(); foreach (var item in list) _items.Add(item); Changed?.Invoke();
        }
        catch (Exception ex) { MessageBox.Show(this, ex.Message, "ذخیره پلاک", MessageBoxButton.OK, MessageBoxImage.Error); }
    }
    private void Delete()
    {
        if (_grid.SelectedItem is not SavedPlate item || MessageBox.Show(this, $"پلاک «{item.Name}» حذف شود؟", "حذف پلاک", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
        try { var list = _store.Delete(item); _items.Clear(); foreach (var row in list) _items.Add(row); Changed?.Invoke(); }
        catch (Exception ex) { MessageBox.Show(this, ex.Message, "حذف پلاک", MessageBoxButton.OK, MessageBoxImage.Error); }
    }
}

public sealed class SavedPlateEditWindow : Window
{
    private readonly TextBox _name = new(), _transfer = new(), _number = new(), _series = new();
    public SavedPlate Value { get; private set; } = new("", "", "", "");
    public SavedPlateEditWindow(SavedPlate? value)
    {
        Title = value is null ? "ثبت پلاک جدید" : "ویرایش پلاک"; Width = 480; SizeToContent = SizeToContent.Height; ResizeMode = ResizeMode.NoResize; WindowStartupLocation = WindowStartupLocation.CenterOwner;
        _name.Text = value?.Name ?? ""; _transfer.Text = value?.TransferCode ?? ""; _number.Text = value?.Number ?? ""; _series.Text = value?.Series ?? "";
        var panel = new StackPanel { Margin = new Thickness(18) };
        foreach (var (label, field) in new[] { ("نام پلاک", _name), ("کد شماره‌گذاری", _transfer), ("شماره پلاک", _number), ("سری پلاک (اختیاری)", _series) })
        {
            panel.Children.Add(new Label { Content = label, Target = field });
            panel.Children.Add(field);
        }
        var buttons = new StackPanel { Orientation = Orientation.Horizontal }; var save = new Button { Content = "ذخیره", Style = (Style)Application.Current.Resources["SuccessButton"] }; save.Click += Save_Click;
        var cancel = new Button { Content = "انصراف", Style = (Style)Application.Current.Resources["SecondaryButton"] }; cancel.Click += (_, _) => DialogResult = false; buttons.Children.Add(save); buttons.Children.Add(cancel); panel.Children.Add(buttons); Content = panel;
    }
    private void Save_Click(object sender, RoutedEventArgs e)
    {
        var transfer = JsonHelpers.NormalizeDigits(_transfer.Text); var number = JsonHelpers.NormalizeDigits(_number.Text); var series = JsonHelpers.NormalizeDigits(_series.Text);
        if (string.IsNullOrWhiteSpace(_name.Text) || !Regex.IsMatch(transfer, "^[0-9]+$") || !Regex.IsMatch(number, "^[0-9]+$") || (series.Length > 0 && !Regex.IsMatch(series, "^[0-9]+$"))) { MessageBox.Show(this, "نام، کد شماره‌گذاری و شماره پلاک را صحیح وارد کنید.", "ورودی", MessageBoxButton.OK, MessageBoxImage.Warning); return; }
        Value = new(_name.Text.Trim(), transfer, number, series, DateTime.Today.ToString("yyyy-MM-dd")); DialogResult = true;
    }
}

public sealed class TextEditDialog : Window
{
    private readonly TextBox _text = new() { AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Height = 190 };
    private readonly CheckBox _enabled = new() { Content = "نمایش تبلیغات زیر رسید" };
    public ReceiptAdvertising Value { get; private set; }
    public TextEditDialog(ReceiptAdvertising current)
    {
        Value = current; Title = "تنظیم تبلیغات رسید"; Width = 620; Height = 380; WindowStartupLocation = WindowStartupLocation.CenterOwner;
        _enabled.IsChecked = current.Enabled; _text.Text = current.Text;
        var panel = new StackPanel { Margin = new Thickness(16) }; panel.Children.Add(_enabled); panel.Children.Add(_text);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal }; var save = new Button { Content = "ذخیره تنظیمات", Style = (Style)Application.Current.Resources["SuccessButton"] }; save.Click += (_, _) => { if (_enabled.IsChecked == true && string.IsNullOrWhiteSpace(_text.Text)) { MessageBox.Show(this, "متن تبلیغ را وارد کنید یا نمایش را خاموش کنید."); return; } Value = new(_enabled.IsChecked == true, _text.Text); DialogResult = true; };
        var reset = new Button { Content = "متن پیش‌فرض", Style = (Style)Application.Current.Resources["SecondaryButton"] }; reset.Click += (_, _) => _text.Text = ReceiptAdvertising.DefaultText;
        buttons.Children.Add(save); buttons.Children.Add(reset); panel.Children.Add(buttons); Content = panel;
    }
}

public sealed class LogWindow : Window
{
    public TextBox TextBox { get; } = new() { IsReadOnly = true, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, FontFamily = new FontFamily("Consolas"), FlowDirection = FlowDirection.LeftToRight };
    public LogWindow() { Title = "گزارش عملیات"; Width = 780; Height = 390; WindowStartupLocation = WindowStartupLocation.CenterOwner; Content = TextBox; }
}
