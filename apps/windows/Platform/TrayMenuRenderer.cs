using System.Drawing;
using System.Windows.Forms;

namespace VolturaEarner.Platform;

// Flat menu painting and palette taken from Voltura Air's ThemedToolStripRenderer.
internal sealed class TrayMenuRenderer(bool dark) : ToolStripProfessionalRenderer
{
    internal Color Surface { get; } = dark
        ? Color.FromArgb(23, 29, 33)
        : Color.White;
    internal Color Text { get; } =
        dark
            ? Color.FromArgb(247, 242, 233)
            : Color.FromArgb(28, 34, 39);
    private readonly Color _raised = dark
        ? Color.FromArgb(32, 40, 46)
        : Color.FromArgb(237, 242, 245);
    private readonly Color _border = dark
        ? Color.FromArgb(62, 74, 82)
        : Color.FromArgb(211, 220, 226);

    protected override void OnRenderToolStripBackground(ToolStripRenderEventArgs e)
    {
        using var brush = new SolidBrush(Surface);

        e.Graphics.FillRectangle(brush, e.AffectedBounds);
    }

    protected override void OnRenderImageMargin(ToolStripRenderEventArgs e) =>
        OnRenderToolStripBackground(e);

    protected override void OnRenderToolStripBorder(ToolStripRenderEventArgs e)
    {
        using var pen = new Pen(_border);
        var bounds = e.AffectedBounds;

        bounds.Width--;
        bounds.Height--;
        e.Graphics.DrawRectangle(pen, bounds);
    }

    protected override void OnRenderMenuItemBackground(ToolStripItemRenderEventArgs e)
    {
        using var brush = new SolidBrush(e.Item.Selected
            ? _raised
            : Surface);

        e.Graphics.FillRectangle(brush, new Rectangle(Point.Empty, e.Item.Size));
    }

    protected override void OnRenderSeparator(ToolStripSeparatorRenderEventArgs e)
    {
        using var pen = new Pen(_border);
        var padding = e.ToolStrip?.Padding ?? Padding.Empty;
        var y = e.Item.Height / 2;

        e.Graphics.DrawLine(pen, padding.Left, y, e.Item.Width - padding.Right, y);
    }
}
