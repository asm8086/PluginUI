// 2026/10/18 16:20:00 UTC - PluginUI
// SPDX-FileCopyrightText: 2026 asm8086
// SPDX-License-Identifier: MIT

namespace PluginUI.Tests;

public class LayoutTests
{
    private static UIHost Layout(UIElement root, float width, float height)
    {
        var host = new UIHost(root, new Size(width, height));
        host.UpdateLayout();
        return host;
    }

    [Test]
    public void CanvasPlacesChildrenAbsolutely()
    {
        var root = new Canvas { Padding = 5f };
        var a = new Probe();
        a.SetBounds(10f, 20f, 30f, 40f);
        var b = new Probe { Left = 50f, Top = 60f };
        root.Add(a, b);
        Layout(root, 200f, 200f);
        Assert.Multiple(() =>
        {
            Assert.That(a.Bounds, Is.EqualTo(new Rect(15f, 25f, 30f, 40f)));
            Assert.That(b.Bounds, Is.EqualTo(new Rect(55f, 65f, 20f, 10f)), "auto size = desired size");
        });
    }

    [Test]
    public void StackPanelStacksWithSpacingAndMargins()
    {
        var root = new StackPanel(Orientation.Vertical, 4f);
        var a = new Probe { Natural = new Size(10f, 10f) };
        var b = new Probe { Natural = new Size(10f, 20f), Margin = new Thickness(2f) };
        var hidden = new Probe { IsVisible = false };
        var c = new Probe { Natural = new Size(10f, 5f), HorizontalAlignment = HorizontalAlignment.Right };
        root.Add(a, b, hidden, c);
        Layout(root, 100f, 100f);
        Assert.Multiple(() =>
        {
            Assert.That(a.Bounds, Is.EqualTo(new Rect(0f, 0f, 100f, 10f)));
            Assert.That(b.Bounds, Is.EqualTo(new Rect(2f, 16f, 96f, 20f)));
            Assert.That(c.Bounds, Is.EqualTo(new Rect(90f, 42f, 10f, 5f)), "hidden children take no space nor spacing");
        });
    }

    [Test]
    public void GridResolvesPixelAutoAndStar()
    {
        var grid = new Grid("Auto,*", "50,*,2*") { ColumnSpacing = 10f, RowSpacing = 0f };
        var header = new Probe { Natural = new Size(10f, 24f) };
        var cell = new Probe();
        var span = new Probe();
        grid.Add(header, 0, 0).Add(cell, 1, 2).Add(span, 1, 0, columnSpan: 2);
        Layout(grid, 280f, 124f);
        Assert.Multiple(() =>
        {
            Assert.That(grid.ActualRowHeights, Is.EqualTo(new[] { 24f, 100f }));
            Assert.That(grid.ActualColumnWidths, Is.EqualTo(new[] { 50f, 70f, 140f }));
            Assert.That(cell.Bounds, Is.EqualTo(new Rect(140f, 24f, 140f, 100f)));
            Assert.That(span.Bounds, Is.EqualTo(new Rect(0f, 24f, 130f, 100f)));
        });
    }

    [Test]
    public void DockPanelDocksThenFills()
    {
        var dock = new DockPanel();
        var top = new Probe { Dock = Side.Top, Natural = new Size(0f, 20f) };
        var left = new Probe { Dock = Side.Left, Natural = new Size(30f, 0f) };
        var fill = new Probe();
        dock.Add(top, left, fill);
        Layout(dock, 200f, 100f);
        Assert.Multiple(() =>
        {
            Assert.That(top.Bounds, Is.EqualTo(new Rect(0f, 0f, 200f, 20f)));
            Assert.That(left.Bounds, Is.EqualTo(new Rect(0f, 20f, 30f, 80f)));
            Assert.That(fill.Bounds, Is.EqualTo(new Rect(30f, 20f, 170f, 80f)));
        });
    }

    [Test]
    public void WrapPanelWraps()
    {
        var wrap = new WrapPanel { ItemSpacing = 5f, LineSpacing = 5f };
        var items = Enumerable.Range(0, 5).Select(_ => new Probe { Natural = new Size(40f, 10f) }).ToArray();
        wrap.Add(items);
        Layout(wrap, 100f, 100f);
        Assert.Multiple(() =>
        {
            Assert.That(items[1].Bounds.X, Is.EqualTo(45f));
            Assert.That(items[2].Bounds, Is.EqualTo(new Rect(0f, 15f, 40f, 10f)), "third item wraps");
            Assert.That(items[4].Bounds.Y, Is.EqualTo(30f));
        });
    }

    [Test]
    public void AlignmentAndMinMaxAreApplied()
    {
        var root = new Panel();
        var centered = new Probe { Natural = new Size(20f, 10f), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Bottom };
        var clamped = new Probe { MaxWidth = 50f, MinHeight = 80f, VerticalAlignment = VerticalAlignment.Top };
        root.Add(centered, clamped);
        Layout(root, 100f, 100f);
        Assert.Multiple(() =>
        {
            Assert.That(centered.Bounds, Is.EqualTo(new Rect(40f, 90f, 20f, 10f)));
            Assert.That(clamped.Bounds.Width, Is.EqualTo(50f));
            Assert.That(clamped.Bounds.Height, Is.EqualTo(80f));
        });
    }

    [Test]
    public void RelayoutModeUsesPixelSize()
    {
        var root = new Panel();
        var probe = new Probe();
        root.Add(probe);
        var host = new UIHost(root, new Size(100f, 50f), SizingMode.Relayout);
        host.SetPixelSize(300, 120);
        host.UpdateLayout();
        Assert.That(probe.Bounds, Is.EqualTo(new Rect(0f, 0f, 300f, 120f)));
    }
}
