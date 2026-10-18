// 2026/10/18 16:20:00 UTC - PluginUI
// SPDX-FileCopyrightText: 2026 asm8086
// SPDX-License-Identifier: MIT

namespace PluginUI.Tests;

public class PrimitivesTests
{
    [Test]
    public void RectEdgesAndContainment()
    {
        var r = new Rect(10f, 20f, 30f, 40f);
        Assert.Multiple(() =>
        {
            Assert.That(r.Right, Is.EqualTo(40f));
            Assert.That(r.Bottom, Is.EqualTo(60f));
            Assert.That(r.Contains(new Point(10f, 20f)), Is.True);
            Assert.That(r.Contains(new Point(40f, 60f)), Is.False, "right/bottom edges are exclusive");
            Assert.That(r.Deflate(new Thickness(1f, 2f, 3f, 4f)), Is.EqualTo(new Rect(11f, 22f, 26f, 34f)));
            Assert.That(r.Inflate(-100f).IsEmpty, Is.True);
            Assert.That(Rect.FromEdges(5f, 5f, 1f, 1f).Width, Is.EqualTo(0f));
        });
    }

    [Test]
    public void RectIntersectAndUnion()
    {
        var a = new Rect(0f, 0f, 10f, 10f);
        var b = new Rect(5f, 5f, 10f, 10f);
        Assert.Multiple(() =>
        {
            Assert.That(a.Intersects(b), Is.True);
            Assert.That(a.Intersect(b), Is.EqualTo(new Rect(5f, 5f, 5f, 5f)));
            Assert.That(a.Union(b), Is.EqualTo(new Rect(0f, 0f, 15f, 15f)));
            Assert.That(a.CenteredSquare(), Is.EqualTo(a));
        });
    }

    [TestCase("#FF8000", 1f, 0.50196f, 0f, 1f)]
    [TestCase("00FF0080", 0f, 1f, 0f, 0.50196f)]
    [TestCase("#fff", 1f, 1f, 1f, 1f)]
    public void ColorParses(string text, float r, float g, float b, float a)
    {
        var c = Color.Parse(text);
        Assert.Multiple(() =>
        {
            Assert.That(c.R, Is.EqualTo(r).Within(1e-3));
            Assert.That(c.G, Is.EqualTo(g).Within(1e-3));
            Assert.That(c.B, Is.EqualTo(b).Within(1e-3));
            Assert.That(c.A, Is.EqualTo(a).Within(1e-3));
        });
    }

    [Test]
    public void ColorHexRoundTripAndHsv()
    {
        var c = Color.FromRgb(0x3A7BD5);
        Assert.That(Color.Parse(c.ToHex()), Is.EqualTo(c));
        var hsv = c.ToHsv();
        var back = Color.FromHsv(hsv.H, hsv.S, hsv.V);
        Assert.Multiple(() =>
        {
            Assert.That(back.R, Is.EqualTo(c.R).Within(1e-3));
            Assert.That(back.G, Is.EqualTo(c.G).Within(1e-3));
            Assert.That(back.B, Is.EqualTo(c.B).Within(1e-3));
            Assert.That(Color.Black.Contrasting, Is.EqualTo(Color.White));
            Assert.That(Color.TryParse("nope", out _), Is.False);
        });
    }

    [Test]
    public void PathHelpersProducePointsOnCircle()
    {
        var top = PathGeometry.PointOnCircle(new Point(0f, 0f), 10f, 0f);
        var right = PathGeometry.PointOnCircle(new Point(0f, 0f), 10f, 90f);
        Assert.Multiple(() =>
        {
            Assert.That(top.X, Is.EqualTo(0f).Within(1e-4));
            Assert.That(top.Y, Is.EqualTo(-10f).Within(1e-4));
            Assert.That(right.X, Is.EqualTo(10f).Within(1e-4));
            Assert.That(right.Y, Is.EqualTo(0f).Within(1e-4));
        });
    }

    [TestCase(-135f, 270f)]
    [TestCase(30f, -200f)]
    [TestCase(0f, 360f)]
    public void ArcsAreBezierApproximationsEndingOnTheCircle(float start, float sweep)
    {
        var center = new Point(50f, 40f);
        var path = PathGeometry.Arc(center, 20f, start, sweep);
        var last = path.Commands[^1];
        var expected = PathGeometry.PointOnCircle(center, 20f, start + sweep);
        Assert.Multiple(() =>
        {
            Assert.That(path.Commands.Skip(1).All(c => c.Kind == PathGeometry.CommandKind.Cubic), Is.True);
            Assert.That(path.Commands.Count - 1, Is.EqualTo((int)Math.Ceiling(Math.Abs(sweep) / 90f)));
            Assert.That(last.P3.X, Is.EqualTo(expected.X).Within(1e-3));
            Assert.That(last.P3.Y, Is.EqualTo(expected.Y).Within(1e-3));
        });
    }

    [Test]
    public void EasingEndpoints()
    {
        foreach (var f in new Func<float, float>[] { Easing.Linear, Easing.QuadOut, Easing.CubicInOut, Easing.BackOut, Easing.BounceOut, Easing.ElasticOut })
        {
            Assert.That(f(0f), Is.EqualTo(0f).Within(1e-4));
            Assert.That(f(1f), Is.EqualTo(1f).Within(1e-4));
        }
    }
}
