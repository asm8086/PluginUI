// 2026/10/18 16:20:00 UTC - PluginUI
// SPDX-FileCopyrightText: 2026 asm8086
// SPDX-License-Identifier: MIT

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using PluginUIEditor;

namespace PluginUI.Design.Tests;

/// <summary>Editing commands of PluginUIEditor's document session (undo/redo, clipboard, structure).</summary>
public class SessionTests
{
    private static DesignSession NewSession()
    {
        var session = new DesignSession(DesignTypeRegistry.CreateDefault()) { GridSize = 4f };
        session.New();
        return session;
    }

    [Test]
    public void InsertSnapsToTheGridAndUndoRedoRestoreTheDocument()
    {
        var session = NewSession();
        var root = session.Document.Root;
        var knob = session.CreateNode("Knob", new Point(21f, 30f), root);
        session.Insert(knob, root);
        Assert.Multiple(() =>
        {
            Assert.That(knob.Name, Is.EqualTo("knob"));
            Assert.That(knob.Get("Left"), Is.EqualTo(20f));
            Assert.That(knob.Get("Top"), Is.EqualTo(32f));
            Assert.That(session.Selected, Is.SameAs(knob));
            Assert.That(session.IsDirty, Is.True);
        });

        session.SetProperty(knob, "Label", "Gain");
        session.Undo();
        Assert.That(session.Document.Root.Children[0].Get("Label"), Is.Not.EqualTo("Gain"));
        Assert.That(session.Selected, Is.SameAs(session.Document.Root.Children[0]), "selection follows the restored tree");
        session.Undo();
        Assert.That(session.Document.Root.Children, Is.Empty);
        Assert.That(session.IsDirty, Is.False);
        session.Redo();
        session.Redo();
        Assert.That(session.Document.Root.Children[0].Get("Label"), Is.EqualTo("Gain"));
        Assert.That(session.CanRedo, Is.False);
    }

    [Test]
    public void ChangesThatChangeNothingAreNotRecorded()
    {
        var session = NewSession();
        session.EditSettings(() => session.Document.Name = session.Document.Name);
        Assert.That(session.CanUndo, Is.False);
    }

    [Test]
    public void CopyPasteRenamesAndOffsetsAndRespectsContainerCapacity()
    {
        var session = NewSession();
        var root = session.Document.Root;
        var knob = session.CreateNode("Knob", new Point(16f, 16f), root);
        session.Insert(knob, root);
        string clip = session.Copy(knob);

        session.Selected = root;
        Assert.That(session.Paste(clip), Is.True);
        var copy = root.Children[1];
        Assert.Multiple(() =>
        {
            Assert.That(copy.Name, Is.EqualTo("knob2"));
            Assert.That(copy.Get("Left"), Is.EqualTo(24f));
            Assert.That(session.Selected, Is.SameAs(copy));
        });

        var split = session.CreateNode("SplitView", null, root);
        session.Insert(split, root);
        session.Selected = split;
        session.Paste(clip);
        session.Selected = split;
        session.Paste(clip);
        Assert.That(split.Children.Select(c => c.Slot), Is.EqualTo(new[] { "First", "Second" }));
        session.Selected = split;
        session.Paste(clip);
        Assert.That(split.Children, Has.Count.EqualTo(2), "a full split view sends the paste to its parent");
        Assert.That(root.Children.Last().Type, Is.EqualTo("Knob"));
        Assert.That(session.Paste("not json"), Is.False);
    }

    [Test]
    public void RenameKeepsNamesValidAndUnique()
    {
        var session = NewSession();
        var root = session.Document.Root;
        var a = session.CreateNode("Knob", null, root);
        session.Insert(a, root);
        var b = session.CreateNode("Slider", null, root);
        session.Insert(b, root);
        Assert.Multiple(() =>
        {
            Assert.That(session.Rename(b, "knob"), Is.True);
            Assert.That(b.Name, Is.EqualTo("knob2"));
            Assert.That(session.Rename(b, "2 bad"), Is.False);
            Assert.That(b.Name, Is.EqualTo("knob2"));
            Assert.That(session.Rename(b, "  "), Is.True);
            Assert.That(b.Name, Is.Null, "an empty name makes an anonymous element");
        });
    }

    [Test]
    public void ReparentMoveAndDeleteKeepTheTreeConsistent()
    {
        var session = NewSession();
        var root = session.Document.Root;
        var group = session.CreateNode("StackPanel", null, root);
        session.Insert(group, root);
        var knob = session.CreateNode("Knob", null, root);
        session.Insert(knob, root);

        session.Reparent(group, group, 0);
        Assert.That(session.ParentOf(group), Is.SameAs(root), "an element cannot go inside itself");
        session.Reparent(knob, group, -1);
        Assert.That(session.ParentOf(knob), Is.SameAs(group));
        Assert.That(knob.Get("Left"), Is.Not.Null, "layout values are kept when moving (harmless outside a canvas)");

        var label = session.CreateNode("Label", null, group);
        session.Insert(label, group);
        session.MoveInParent(label, toFront: false);
        Assert.That(group.Children[0], Is.SameAs(label));

        session.Delete(label);
        Assert.That(session.Selected, Is.SameAs(knob), "deleting selects the next sibling");
        session.Delete(root);
        Assert.That(session.Document.Root, Is.SameAs(root), "the root cannot be deleted");
    }

    [Test]
    public void ReparentRespectsSlotsAndCapacity()
    {
        var session = NewSession();
        var root = session.Document.Root;
        var tabs = session.CreateNode("TabView", null, root);
        session.Insert(tabs, root);
        var page = session.CreateNode("Canvas", null, tabs);
        session.Insert(page, tabs);
        var split = session.CreateNode("SplitView", null, root);
        session.Insert(split, root);
        var first = session.CreateNode("Label", null, split);
        session.Insert(first, split);
        Assert.That(first.Slot, Is.EqualTo("First"));

        Assert.That(session.Reparent(page, split, -1), Is.True);
        Assert.That(page.Slot, Is.EqualTo("Second"), "the tab title is replaced by the free pane");
        var border = session.CreateNode("Border", null, root);
        session.Insert(border, root);
        Assert.That(session.Reparent(first, border, -1), Is.True);
        Assert.That(first.Slot, Is.Null);
        var label = session.CreateNode("Label", null, root);
        session.Insert(label, root);
        Assert.That(session.Reparent(label, border, -1), Is.False, "a border holds one child");
        Assert.That(session.ParentOf(label), Is.SameAs(root));

        var sw = session.CreateNode("Switch", null, root);
        Assert.That(sw.Name, Is.EqualTo("switch1"), "C# keywords are not used as names");
        Assert.That(session.Rename(sw, "Color"), Is.False, "type names used by the generated code are refused");
    }

    [Test]
    public void TargetContainerWalksUpToAContainerThatAcceptsChildren()
    {
        var session = NewSession();
        var root = session.Document.Root;
        var border = session.CreateNode("Border", null, root);
        session.Insert(border, root);
        Assert.That(session.TargetContainer(), Is.SameAs(border));
        var knob = session.CreateNode("Knob", null, border);
        session.Insert(knob, border);
        Assert.That(session.TargetContainer(), Is.SameAs(root), "a border holds one child");

        var tabs = session.CreateNode("TabView", null, root);
        session.Insert(tabs, root);
        var page = session.CreateNode("Canvas", null, tabs);
        Assert.That(page.Slot, Is.EqualTo("Tab 1"));
    }

    [Test]
    public void SaveAndOpenClearTheDirtyFlag()
    {
        var session = NewSession();
        var root = session.Document.Root;
        session.Insert(session.CreateNode("Knob", null, root), root);
        string path = Path.Combine(Path.GetTempPath(), $"pluginui-session-{Guid.NewGuid():N}.pluginui");
        try
        {
            session.Document.Comment = "2026/10/18 14:24:00 UTC - PluginUI";
            session.Save(path);
            Assert.That(session.IsDirty, Is.False);
            string text = File.ReadAllText(path);
            Assert.That(text, Does.StartWith("{\n  \"comment\": \"2026/10/18 14:24:00 UTC - PluginUI\",").Or.StartWith("{\r\n  \"comment\""));
            Assert.That(text, Does.Contain("\"size\": [640, 400]"), "small tuples stay on one line");
            var other = new DesignSession(DesignTypeRegistry.CreateDefault());
            Assert.That(other.Open(path), Is.Empty);
            Assert.That(other.Document.Root.Children.Single().Type, Is.EqualTo("Knob"));
            Assert.That(other.CanUndo, Is.False);
            Assert.That(other.Document.Comment, Is.EqualTo("2026/10/18 14:24:00 UTC - PluginUI"));
            Assert.That(other.DisplayName, Is.EqualTo(Path.GetFileName(path)));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Test]
    public void ExtensionAssembliesAddTheirElementsToTheToolbox()
    {
        const string source = """
            using System.ComponentModel;
            namespace Acme.Controls;
            [Category("Acme")]
            public sealed class AcmeLamp : PluginUI.UIElement
            {
                public bool IsLit { get; set; }
            }
            public sealed class AcmeGauge : PluginUI.UIElement
            {
                public float Value { get; set; }
            }
            public sealed class NeedsArguments(int size) : PluginUI.UIElement
            {
                public int Size { get; } = size;
            }
            internal sealed class Hidden : PluginUI.UIElement { }
            public sealed class Knob : PluginUI.UIElement { }
            """;
        var trusted = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!).Split(Path.PathSeparator);
        var references = trusted.Select(p => MetadataReference.CreateFromFile(p)).ToList();
        references.Add(MetadataReference.CreateFromFile(typeof(UIElement).Assembly.Location));
        string name = "AcmeControls" + Guid.NewGuid().ToString("N");
        var compilation = CSharpCompilation.Create(name, [CSharpSyntaxTree.ParseText(source)], references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        string folder = Path.Combine(Path.GetTempPath(), name);
        Directory.CreateDirectory(folder);
        string path = Path.Combine(folder, name + ".dll");
        try
        {
            var emit = compilation.Emit(path);
            Assert.That(emit.Success, Is.True, string.Join("\n", emit.Diagnostics));

            var registry = DesignTypeRegistry.CreateDefault();
            var messages = Extensions.Load(registry, [path, Path.Combine(folder, "missing.dll")]);
            Assert.Multiple(() =>
            {
                Assert.That(registry.Find("AcmeLamp")!.ToolboxCategory, Is.EqualTo("Acme"));
                Assert.That(registry.Find("AcmeLamp")!.Find("IsLit"), Is.Not.Null);
                Assert.That(registry.Find("AcmeGauge")!.ToolboxCategory, Is.EqualTo(name), "default group: the assembly name");
                Assert.That(registry.Find("NeedsArguments"), Is.Null);
                Assert.That(registry.Find("Hidden"), Is.Null);
                Assert.That(registry.Find("Knob")!.Type, Is.EqualTo(typeof(Knob)), "built-in names are not replaced");
                Assert.That(messages, Has.Count.EqualTo(3), string.Join("\n", messages));
                Assert.That(messages, Has.Some.Contains("Acme.Controls.Knob"));
                Assert.That(messages, Has.Some.Contains("2 element"));
                Assert.That(messages, Has.Some.Contains("missing.dll"));
            });

            // A design using the custom element loads and builds like any other.
            var doc = new DesignDocument();
            doc.Root.Add(new DesignNode("AcmeLamp") { Name = "lamp" }.Set("IsLit", true));
            var result = DesignLoader.Build(DesignJson.Read(DesignJson.Write(doc), registry), registry);
            Assert.That(result.Warnings, Is.Empty);
            Assert.That(result.Elements["lamp"].GetType().GetProperty("IsLit")!.GetValue(result.Elements["lamp"]), Is.True);
        }
        finally
        {
            // The assembly stays loaded (and locked on Windows) until the test process ends.
            try { Directory.Delete(folder, recursive: true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        }
    }
}
