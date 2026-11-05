// 2026/11/05 22:35:00 UTC - PluginUI
// SPDX-FileCopyrightText: 2026 asm8086
// SPDX-License-Identifier: MIT

namespace PluginUI.Tests;

/// <summary><see cref="MappedParameter"/>: ranges with a hole, linked followers, events and gestures.</summary>
public class MappedParameterTests
{
    [Test]
    public void RangesSkipTheHole()
    {
        var fb = new LocalParameter("FB", -1.0, 1.0, 0.0);
        var mapped = MappedParameter.Ranges(fb, (-0.9, -0.5), (0.5, 0.9));
        Assert.Multiple(() =>
        {
            Assert.That(mapped.ToPlain(0.0), Is.EqualTo(-0.9).Within(1e-12));
            Assert.That(mapped.ToPlain(0.25), Is.EqualTo(-0.7).Within(1e-12));
            Assert.That(mapped.ToPlain(0.5), Is.EqualTo(0.5).Within(1e-12), "the second range starts at mid course");
            Assert.That(mapped.ToPlain(1.0), Is.EqualTo(0.9).Within(1e-12));
            Assert.That(mapped.ToNormalized(0.7), Is.EqualTo(0.75).Within(1e-12));
            Assert.That(mapped.ToNormalized(0.1), Is.EqualTo(0.5).Within(1e-12), "a value in the hole shows at the nearest end");
            Assert.That(mapped.ToNormalized(-1.0), Is.EqualTo(0.0).Within(1e-12));
        });
        mapped.SetValue(0.75);
        Assert.That(fb.Value, Is.EqualTo(0.7).Within(1e-12));
        Assert.That(mapped.NormalizedValue, Is.EqualTo(0.75).Within(1e-12));
        Assert.That(mapped.ToDisplayString(0.75), Is.EqualTo(fb.ToDisplayString(fb.NormalizedValue)));
    }

    [Test]
    public void LinkedParametersMoveInTheSameGesture()
    {
        var blend = new LocalParameter("BL", -1.0, 1.0, 0.0);
        var fb = new LocalParameter("FB", -1.0, 1.0, 0.0);
        var linked = new MappedParameter(blend).Link(fb, n => 1.0 - n);
        int changes = 0;
        linked.Changed += p => { changes++; Assert.That(p, Is.SameAs(linked)); };
        linked.BeginEdit();
        Assert.That((blend.IsEditing, fb.IsEditing), Is.EqualTo((true, true)));
        linked.PerformEdit(blend.ToNormalized(0.6));
        linked.EndEdit();
        Assert.Multiple(() =>
        {
            Assert.That((blend.IsEditing, fb.IsEditing), Is.EqualTo((false, false)));
            Assert.That((blend.Value, fb.Value), Is.EqualTo((0.6, -0.6)).Within(1e-12));
            Assert.That(changes, Is.GreaterThanOrEqualTo(1));
        });
    }

    [Test]
    public void AKnobBoundToTheViewEditsTheInnerParameter()
    {
        var fb = new LocalParameter("FB", -1.0, 1.0, 0.0);
        var knob = new Knob(MappedParameter.Ranges(fb, (0.5, 0.9)));
        knob.Value = 0.5;
        Assert.That(fb.Value, Is.EqualTo(0.7).Within(1e-12));
        Assert.That(knob.Parameter!.Name, Is.EqualTo("FB"));
    }
}
