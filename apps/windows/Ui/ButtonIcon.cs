using System.Windows;

namespace VolturaEarner.Ui;

public static class ButtonIcon
{
    public static readonly DependencyProperty GlyphProperty = DependencyProperty.RegisterAttached("Glyph", typeof(string), typeof(ButtonIcon), new PropertyMetadata(null));
    public static string? GetGlyph(DependencyObject element) => (string?)element.GetValue(GlyphProperty);
    public static void SetGlyph(DependencyObject element, string? value) => element.SetValue(GlyphProperty, value);
}
