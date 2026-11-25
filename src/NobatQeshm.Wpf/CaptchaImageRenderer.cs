using System.Globalization;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Xml.Linq;

namespace NobatQeshm.Wpf;

internal static class CaptchaImageRenderer
{
    private static readonly Regex NumberPattern = new(@"[-+]?(?:\d+\.?\d*|\.\d+)", RegexOptions.CultureInvariant);
    private static readonly Regex RotationPattern = new(@"rotate\(([^)]+)\)", RegexOptions.CultureInvariant);

    public static BitmapSource Render(string svg)
    {
        var root = XElement.Parse(svg);
        if (root.Name.LocalName != "svg") throw new FormatException("ساختار تصویر کد امنیتی معتبر نیست.");

        var viewBox = Numbers(root.Attribute("viewBox")?.Value);
        var origin = viewBox.Length == 4 ? new Point(viewBox[0], viewBox[1]) : new Point();
        var width = viewBox.Length == 4 ? viewBox[2] : Number(root, "width", 170);
        var height = viewBox.Length == 4 ? viewBox[3] : Number(root, "height", 52);
        if (!(width > 0 && height > 0 && double.IsFinite(width / height)))
            throw new FormatException("اندازهٔ تصویر کد امنیتی معتبر نیست.");

        var scale = Math.Min(480 / width, 150 / height);
        var pixelWidth = Math.Max(1, (int)Math.Ceiling(width * scale));
        var pixelHeight = Math.Max(1, (int)Math.Ceiling(height * scale));
        var visual = new DrawingVisual();
        using (var drawing = visual.RenderOpen())
        {
            drawing.DrawRectangle(Brushes.White, null, new Rect(0, 0, pixelWidth, pixelHeight));
            drawing.PushTransform(new MatrixTransform(scale, 0, 0, scale, -origin.X * scale, -origin.Y * scale));
            foreach (var element in root.Descendants())
            {
                var opacity = Number(element, "opacity", 1);
                if (opacity < 1) drawing.PushOpacity(Math.Clamp(opacity, 0, 1));
                DrawElement(drawing, element, width, height);
                if (opacity < 1) drawing.Pop();
            }
            drawing.Pop();
        }

        var image = new RenderTargetBitmap(pixelWidth, pixelHeight, 96, 96, PixelFormats.Pbgra32);
        image.Render(visual);
        image.Freeze();
        return image;
    }

    private static void DrawElement(DrawingContext drawing, XElement element, double width, double height)
    {
        switch (element.Name.LocalName)
        {
            case "rect":
                drawing.DrawRectangle(Fill(element), null, new Rect(Number(element, "x"), Number(element, "y"),
                    Math.Max(0, Number(element, "width", width)), Math.Max(0, Number(element, "height", height))));
                break;
            case "line":
                var pen = new Pen(BrushFor(element, "stroke", Colors.SlateGray), Math.Max(0, Number(element, "stroke-width", 1)));
                drawing.DrawLine(pen, new Point(Number(element, "x1"), Number(element, "y1")),
                    new Point(Number(element, "x2"), Number(element, "y2")));
                break;
            case "circle":
                var radius = Math.Max(0, Number(element, "r"));
                drawing.DrawEllipse(Fill(element), null, new Point(Number(element, "cx"), Number(element, "cy")), radius, radius);
                break;
            case "text":
                DrawText(drawing, element);
                break;
        }
    }

    private static void DrawText(DrawingContext drawing, XElement element)
    {
        var content = element.Value.Trim();
        if (content.Length == 0) return;
        var fontFamily = element.Attribute("font-family")?.Value ?? "Times New Roman";
        var weight = element.Attribute("font-weight")?.Value is "normal" or "400" ? FontWeights.Normal : FontWeights.Bold;
        var typeface = new Typeface(new FontFamily(fontFamily), FontStyles.Normal, weight, FontStretches.Normal);
        var size = Math.Clamp(Number(element, "font-size", 26), 1, 256);
        var text = new FormattedText(content, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, typeface, size, Fill(element), 1);
        var x = Number(element, "x");
        var y = Number(element, "y");
        var rotation = RotationPattern.Match(element.Attribute("transform")?.Value ?? "");
        var values = Numbers(rotation.Groups[1].Value);
        if (values.Length > 0)
            drawing.PushTransform(values.Length == 3 ? new RotateTransform(values[0], values[1], values[2]) : new RotateTransform(values[0]));
        drawing.DrawText(text, new Point(x, y - text.Baseline));
        if (values.Length > 0) drawing.Pop();
    }

    private static Brush Fill(XElement element) => BrushFor(element, "fill", Colors.DarkSlateGray);

    private static Brush BrushFor(XElement element, string attribute, Color fallback)
    {
        var value = element.Attribute(attribute)?.Value;
        if (value == "none") return Brushes.Transparent;
        if (value is not null)
        {
            try { if (ColorConverter.ConvertFromString(value) is Color color) return new SolidColorBrush(color); }
            catch (FormatException) { }
        }
        return new SolidColorBrush(fallback);
    }

    private static double Number(XElement element, string attribute, double fallback = 0)
    {
        var match = NumberPattern.Match(element.Attribute(attribute)?.Value ?? "");
        return match.Success && double.TryParse(match.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var value)
            && double.IsFinite(value) ? value : fallback;
    }

    private static double[] Numbers(string? value) => NumberPattern.Matches(value ?? "")
        .Select(match => double.Parse(match.Value, CultureInfo.InvariantCulture)).ToArray();
}
