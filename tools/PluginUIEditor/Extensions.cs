// 2026/10/18 16:20:00 UTC - PluginUI
// SPDX-FileCopyrightText: 2026 asm8086
// SPDX-License-Identifier: MIT

using System.Reflection;
using System.Runtime.Loader;
using PluginUI.Design;

namespace PluginUIEditor;

/// <summary>
/// Loads the custom elements of extension assemblies (Tools ▸ Options) into the editor's registry, so that they
/// appear in the toolbox and render on the design surface. Every public, non-abstract <see cref="PluginUI.UIElement"/>
/// with a public parameterless constructor is registered; <see cref="System.ComponentModel.CategoryAttribute"/> on the class chooses its
/// toolbox group (default: the assembly name). Assemblies stay loaded until the editor closes.
/// </summary>
internal static class Extensions
{
    private static readonly MethodInfo RegisterMethod = typeof(DesignTypeRegistry).GetMethods()
        .Single(m => m.Name == nameof(DesignTypeRegistry.Register) && m.IsGenericMethodDefinition && m.GetParameters() is [{ ParameterType: var p }] && p == typeof(string));

    private static readonly List<string> SearchFolders = new();
    private static readonly List<string> LoadedFiles = new();

    /// <summary>Extension assemblies loaded so far (their XML documentation feeds the property help).</summary>
    public static IReadOnlyList<string> Loaded => LoadedFiles;

    /// <summary>Registers the elements of the assemblies; returns the problems met (one line each).</summary>
    public static IReadOnlyList<string> Load(DesignTypeRegistry registry, IEnumerable<string> paths)
    {
        var messages = new List<string>();
        foreach (var path in paths.Where(p => !string.IsNullOrWhiteSpace(p)).Select(p => p.Trim()))
        {
            if (!File.Exists(path))
            {
                messages.Add(string.Format(System.Globalization.CultureInfo.InvariantCulture, EditorStrings.Current.ExtensionMissingFormat, path));
                continue;
            }
            try
            {
                AddSearchFolder(Path.GetDirectoryName(Path.GetFullPath(path))!);
                var assembly = AssemblyLoadContext.Default.LoadFromAssemblyPath(Path.GetFullPath(path));
                LoadedFiles.Add(Path.GetFullPath(path));
                Type[] types;
                try
                {
                    types = assembly.GetExportedTypes();
                }
                catch (ReflectionTypeLoadException e)
                {
                    types = e.Types.OfType<Type>().Where(t => t.IsPublic).ToArray();
                }
                int count = 0;
                foreach (var type in types)
                {
                    if (type.IsAbstract || type.IsGenericTypeDefinition || !typeof(PluginUI.UIElement).IsAssignableFrom(type) || type.GetConstructor(Type.EmptyTypes) is null) continue;
                    if (registry.Find(type.Name) is not null)
                    {
                        messages.Add(string.Format(System.Globalization.CultureInfo.InvariantCulture, EditorStrings.Current.ExtensionDuplicateFormat, type.FullName));
                        continue;
                    }
                    string category = type.GetCustomAttribute<System.ComponentModel.CategoryAttribute>()?.Category ?? assembly.GetName().Name ?? "Custom";
                    RegisterMethod.MakeGenericMethod(type).Invoke(registry, [category]);
                    count++;
                }
                messages.Add(string.Format(System.Globalization.CultureInfo.InvariantCulture, EditorStrings.Current.ExtensionLoadedFormat, Path.GetFileName(path), count));
            }
            catch (Exception e) when (e is IOException or BadImageFormatException or FileLoadException or TargetInvocationException or TypeLoadException)
            {
                messages.Add(string.Format(System.Globalization.CultureInfo.InvariantCulture, EditorStrings.Current.ExtensionFailedFormat, path, (e.InnerException ?? e).Message));
            }
        }
        return messages;
    }

    // Dependencies of an extension (other than PluginUI, already loaded) are looked up next to it.
    private static void AddSearchFolder(string folder)
    {
        if (SearchFolders.Contains(folder, StringComparer.OrdinalIgnoreCase)) return;
        if (SearchFolders.Count == 0)
        {
            AssemblyLoadContext.Default.Resolving += (context, name) =>
            {
                foreach (var f in SearchFolders)
                {
                    string candidate = Path.Combine(f, name.Name + ".dll");
                    if (File.Exists(candidate)) return context.LoadFromAssemblyPath(candidate);
                }
                return null;
            };
        }
        SearchFolders.Add(folder);
    }
}
