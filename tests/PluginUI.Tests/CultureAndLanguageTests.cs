// 2026/10/18 16:20:00 UTC - PluginUI
// SPDX-FileCopyrightText: 2026 asm8086
// SPDX-License-Identifier: MIT

using System.Globalization;

namespace PluginUI.Tests;

/// <summary>
/// Numbers always use a decimal point (display and typing), whatever the regional settings and the interface
/// language; typed values open from the value text; the interface language is selectable.
/// </summary>
[NonParallelizable] // changes CultureInfo.CurrentCulture and UIHost.DefaultLanguage
public class CultureAndLanguageTests
{
    private static readonly string[] Cultures = ["fr-FR", "de-DE", "en-US", "ru-RU"];

    private static void WithCulture(string name, Action test)
    {
        var culture = CultureInfo.CurrentCulture;
        var ui = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(name);
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(name);
            test();
        }
        finally
        {
            CultureInfo.CurrentCulture = culture;
            CultureInfo.CurrentUICulture = ui;
        }
    }

    [TestCaseSource(nameof(Cultures))]
    public void NumbersUseADecimalPointInEveryCulture(string culture) => WithCulture(culture, () =>
    {
        var p = new LocalParameter("Gain", -12.0, 12.0, 0.0, "dB", decimals: 2);
        Assert.Multiple(() =>
        {
            Assert.That(p.ToDisplayString(p.ToNormalized(2.5)), Is.EqualTo("2.50"));
            Assert.That(p.ToDisplayStringWithUnits(p.ToNormalized(-1.25)), Is.EqualTo("-1.25 dB"));
            Assert.That(p.TryParse("2.5", out double n) && Math.Abs(p.ToPlain(n) - 2.5) < 1e-9, Is.True);
            Assert.That(p.TryParse("-1.5 dB", out n) && Math.Abs(p.ToPlain(n) + 1.5) < 1e-9, Is.True);
            Assert.That(p.TryParse("2,5", out _), Is.False, "comma rejected");
            Assert.That(p.TryParse("1,000", out _), Is.False, "no thousands separator");
            Assert.That(InvariantNumber.Format(1234.5, "0.0"), Is.EqualTo("1234.5"));
            Assert.That(InvariantNumber.TryParse(" .5 ", out double v) && v == 0.5, Is.True);
            Assert.That(InvariantNumber.TryParse("1e3", out v) && v == 1000.0, Is.True);
            Assert.That(InvariantNumber.TryParse("NaN", out _), Is.False);
            Assert.That(RotaryLinearHelpers.ParsePercent("12.5 %"), Is.EqualTo(0.125).Within(1e-12));
            Assert.That(RotaryLinearHelpers.ParsePercent("12,5"), Is.Null);
        });
    });

    [TestCaseSource(nameof(Cultures))]
    public void LiveEditorCodeUsesADecimalPoint(string culture) => WithCulture(culture, () =>
    {
        var root = new Canvas();
        var knob = new Knob { Name = "knob" };
        knob.SetBounds(10.5f, 20.25f, 64f, 84f);
        root.Add(knob);
        using var host = new UIHost(root, new Size(300f, 200f));
        host.UpdateLayout();
        host.IsLiveEditEnabled = true;
        host.LiveEditor.Selected = knob;
        host.DispatchKeyDown(new KeyEventArgs(Key.Right, ModifierKeys.None)); // nudge by 1 DIP
        string code = host.LiveEditor.GenerateCode();
        Assert.That(code, Does.Contain("knob.SetBounds(11.5f, 20.25f, 64f, 84f);"));
    });

    private static (UIHost Host, Knob Knob, LocalParameter Parameter) KnobHost()
    {
        var p = new LocalParameter("Cutoff", 0.0, 10.0, 1.0, decimals: 2);
        var knob = new Knob(p);
        knob.SetBounds(10f, 10f, 64f, 84f);
        var root = new Canvas();
        root.Add(knob);
        var host = new UIHost(root, new Size(300f, 200f));
        host.UpdateLayout();
        return (host, knob, p);
    }

    private static Rect ValueArea(Knob knob) => new(knob.Bounds.X, knob.Bounds.Bottom - 12f, knob.Bounds.Width, 10f);

    private static void DoubleClick(UIHost host, Point p, MouseButton button = MouseButton.Left)
    {
        host.DispatchMouseDown(p, button, 1, ModifierKeys.None);
        host.DispatchMouseUp(p, button, ModifierKeys.None);
        host.DispatchMouseDown(p, button, 2, ModifierKeys.None);
        host.DispatchMouseUp(p, button, ModifierKeys.None);
    }

    private static TextEditPopup? Editor(UIHost host) => host.Popups.OfType<TextEditPopup>().SingleOrDefault();

    private static void Type(UIHost host, string text)
    {
        host.ProcessPostedActions(); // focuses the editor
        var box = Editor(host)!.Box;
        box.SelectAll();
        host.DispatchTextInput(text);
        host.DispatchKeyDown(new KeyEventArgs(Key.Enter, ModifierKeys.None));
    }

    [TestCaseSource(nameof(Cultures))]
    public void DoubleClickOnTheValueTextTypesAValueAndRejectsACommma(string culture) => WithCulture(culture, () =>
    {
        var (host, knob, p) = KnobHost();
        using var _ = host;
        DoubleClick(host, ValueArea(knob).Center);
        Assert.That(Editor(host), Is.Not.Null, "double-click on the value text opens the editor");

        Type(host, "2,5");
        Assert.Multiple(() =>
        {
            Assert.That(Editor(host), Is.Not.Null, "invalid text keeps the editor open");
            Assert.That(Editor(host)!.Box.IsInvalid, Is.True);
            Assert.That(p.ToPlain(p.NormalizedValue), Is.EqualTo(1.0).Within(1e-9), "value unchanged");
        });

        Type(host, "2.5");
        Assert.Multiple(() =>
        {
            Assert.That(Editor(host), Is.Null, "valid text closes the editor");
            Assert.That(p.ToPlain(p.NormalizedValue), Is.EqualTo(2.5).Within(1e-9));
            Assert.That(p.IsEditing, Is.False, "single complete gesture");
        });
    });

    [Test]
    public void DoubleClickOnTheKnobBodyStillResets()
    {
        var (host, knob, p) = KnobHost();
        using var _ = host;
        p.SetValue(0.8);
        DoubleClick(host, new Point(knob.Bounds.Center.X, knob.Bounds.Y + 30f));
        Assert.Multiple(() =>
        {
            Assert.That(Editor(host), Is.Null);
            Assert.That(p.NormalizedValue, Is.EqualTo(p.DefaultNormalizedValue).Within(1e-12));
        });
    }

    [Test]
    public void TextEntryTriggersAreConfigurable()
    {
        var (host, knob, p) = KnobHost();
        using var _ = host;
        var body = new Point(knob.Bounds.Center.X, knob.Bounds.Y + 30f);

        host.DispatchMouseDown(body, MouseButton.Left, 1, ModifierKeys.Alt);
        Assert.That(Editor(host), Is.Not.Null, "Alt+click");
        host.DispatchKeyDown(new KeyEventArgs(Key.Escape, ModifierKeys.None));
        Assert.That(Editor(host), Is.Null);

        DoubleClick(host, body, MouseButton.Right);
        Assert.That(Editor(host), Is.Not.Null, "right double-click");
        host.DispatchKeyDown(new KeyEventArgs(Key.Escape, ModifierKeys.None));

        knob.Focus();
        host.DispatchKeyDown(new KeyEventArgs(Key.Enter, ModifierKeys.None));
        Assert.That(Editor(host), Is.Not.Null, "Enter key");
        host.DispatchKeyDown(new KeyEventArgs(Key.Escape, ModifierKeys.None));

        knob.TextEntryTriggers = TextEntryTriggers.DoubleClickAnywhere;
        p.SetValue(0.8);
        DoubleClick(host, body);
        Assert.Multiple(() =>
        {
            Assert.That(Editor(host), Is.Not.Null, "double-click anywhere has priority over reset");
            Assert.That(p.NormalizedValue, Is.EqualTo(0.8).Within(1e-12));
        });
        host.DispatchKeyDown(new KeyEventArgs(Key.Escape, ModifierKeys.None));

        knob.TextEntryTriggers = TextEntryTriggers.None;
        DoubleClick(host, ValueArea(knob).Center);
        host.DispatchMouseDown(body, MouseButton.Left, 1, ModifierKeys.Alt);
        host.DispatchMouseUp(body, MouseButton.Left, ModifierKeys.Alt);
        Assert.That(Editor(host), Is.Null, "typing disabled");
    }

    [Test]
    public void InterfaceLanguageIsSelectable()
    {
        string saved = UIHost.DefaultLanguage;
        try
        {
            using var host = new UIHost(new Canvas(), new Size(400f, 300f));
            UIHost.DefaultLanguage = UILanguages.English;
            Assert.That(host.Strings.Cancel, Is.EqualTo("Cancel"));

            UIHost.DefaultLanguage = UILanguages.French;
            Assert.That(host.Strings.Cancel, Is.EqualTo("Annuler"), "default language");

            host.Language = UILanguages.English;
            Assert.That(host.Strings.Cancel, Is.EqualTo("Cancel"), "per-host language wins");

            host.Language = "fr-CA";
            Assert.That(host.Strings.Save, Is.EqualTo("Enregistrer"), "regional tag falls back to its language");

            host.Language = "xx";
            Assert.That(host.Strings.Save, Is.EqualTo("Save"), "unknown language falls back to English");

            UIStrings.Register("de", UIStrings.English with { Cancel = "Abbrechen" });
            host.Language = "de";
            Assert.Multiple(() =>
            {
                Assert.That(host.Strings.Cancel, Is.EqualTo("Abbrechen"));
                Assert.That(host.Strings.Ok, Is.EqualTo("OK"), "missing texts come from English");
                Assert.That(host.Strings.LanguageCode, Is.EqualTo("de"));
                Assert.That(UIStrings.RegisteredLanguages, Does.Contain("de"));
            });

            host.Strings = UIStrings.French with { Cancel = "Laisser tomber" };
            Assert.That(host.Strings.Cancel, Is.EqualTo("Laisser tomber"), "custom table for one host");
            host.Strings = null;
            Assert.That(host.Strings.Cancel, Is.EqualTo("Abbrechen"));

            host.Language = UILanguages.French;
            var dialog = Dialog.ShowMessage(host, "Title", "Message", MessageBoxButtons.YesNoCancel);
            Assert.That(dialog.ButtonCaptions, Is.EqualTo(new[] { "Annuler", "Non", "Oui" }));
        }
        finally
        {
            UIHost.DefaultLanguage = saved;
        }
    }

    [Test]
    public void AutoLanguageFollowsTheSystem()
    {
        WithCulture("fr-FR", () => Assert.That(UIStrings.ForLanguage(UILanguages.Auto).LanguageCode, Is.EqualTo(OperatingSystem.IsWindows() ? UIStrings.SystemLanguage : "fr")));
        WithCulture("en-US", () => Assert.That(UIStrings.ForLanguage(null).Cancel, Is.EqualTo(UIStrings.ForLanguage(UIStrings.SystemLanguage).Cancel)));
    }

    [Test]
    public void LanguageDoesNotChangeNumbers()
    {
        using var host = new UIHost(new Canvas(), new Size(100f, 100f)) { Language = UILanguages.French };
        var p = new LocalParameter("Mix", 0.0, 100.0, 50.0, "%", decimals: 1);
        WithCulture("fr-FR", () => Assert.That(p.ToDisplayStringWithUnits(p.NormalizedValue), Is.EqualTo("50.0 %")));
    }
}
