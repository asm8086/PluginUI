// 2026/10/18 16:20:00 UTC - PluginUI
// SPDX-FileCopyrightText: 2026 asm8086
// SPDX-License-Identifier: MIT

using System.ComponentModel;
using System.Reflection;

namespace PluginUI.Tests;

/// <summary>
/// Conventions that PluginUIEditor relies on (see docs/EXTENDING.md, "Design-time conventions"): every public
/// settable property of an element is categorized or hidden, and every element can be created without arguments.
/// </summary>
public class DesignConventionTests
{
    private static readonly string[] Categories = ["Design", "Layout", "Appearance", "Text", "Value", "Data", "Behavior"];

    // Elements that are created by other elements, never placed by the designer.
    private static readonly HashSet<string> NotDesignable = ["AboutBox", "ColorPickerPopup", "MenuPopup", "TextEditPopup", "DialogText"];

    private static IEnumerable<Type> ElementTypes() => typeof(UIElement).Assembly.GetExportedTypes()
        .Where(t => typeof(UIElement).IsAssignableFrom(t) && !t.IsAbstract && !NotDesignable.Contains(t.Name));

    [Test]
    public void EveryElementHasAParameterlessConstructor()
    {
        var missing = ElementTypes().Where(t => t.GetConstructor(Type.EmptyTypes) is null
            && !t.GetConstructors().Any(c => c.GetParameters().All(p => p.IsOptional))).Select(t => t.Name).ToList();
        Assert.That(missing, Is.Empty);
    }

    [Test]
    public void EverySettablePropertyIsCategorizedOrHidden()
    {
        var problems = new List<string>();
        foreach (var type in ElementTypes())
        {
            foreach (var p in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
            {
                if (p.SetMethod is not { IsPublic: true } || p.GetIndexParameters().Length > 0) continue;
                var browsable = p.GetCustomAttribute<BrowsableAttribute>(inherit: true);
                var category = p.GetCustomAttribute<System.ComponentModel.CategoryAttribute>(inherit: true);
                if (browsable is { Browsable: false }) continue;
                if (category is null) problems.Add($"{type.Name}.{p.Name}: no [Category] nor [Browsable(false)]");
                else if (!Categories.Contains(category.Category)) problems.Add($"{type.Name}.{p.Name}: unknown category '{category.Category}'");
            }
        }
        Assert.That(problems, Is.Empty, string.Join("\n", problems));
    }
}
