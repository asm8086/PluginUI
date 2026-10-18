// 2026/10/18 16:20:00 UTC - PluginUI
// SPDX-FileCopyrightText: 2026 asm8086
// SPDX-License-Identifier: MIT

using PluginUI.Interop;
using PluginUI.Rendering;
using TerraFX.Interop.Windows;
using static TerraFX.Interop.Windows.Windows;

namespace PluginUI;

/// <summary>A file type filter: display name and semicolon-separated patterns ("Audio files", "*.wav;*.aif").</summary>
public readonly record struct FileFilter(string Name, string Patterns);

/// <summary>
/// Modern Windows file dialogs (IFileOpenDialog / IFileSaveDialog), owned by the host window.
/// Modal: they run a nested message loop, call them from the UI thread.
/// </summary>
public static unsafe class FileDialogs
{
    /// <summary>Asks for one file to open. Returns null if cancelled.</summary>
    public static string? OpenFile(UIHost? owner, string title, IReadOnlyList<FileFilter>? filters = null, string? initialFolder = null)
        => Open(owner, title, filters, initialFolder, multiple: false, folders: false) is { Count: > 0 } r ? r[0] : null;

    /// <summary>Asks for one or more files to open. Returns an empty list if cancelled.</summary>
    public static IReadOnlyList<string> OpenFiles(UIHost? owner, string title, IReadOnlyList<FileFilter>? filters = null, string? initialFolder = null)
        => Open(owner, title, filters, initialFolder, multiple: true, folders: false);

    /// <summary>Asks for a folder. Returns null if cancelled.</summary>
    public static string? PickFolder(UIHost? owner, string title, string? initialFolder = null)
        => Open(owner, title, null, initialFolder, multiple: false, folders: true) is { Count: > 0 } r ? r[0] : null;

    /// <summary>Asks for a destination file. Returns null if cancelled.</summary>
    public static string? SaveFile(UIHost? owner, string title, IReadOnlyList<FileFilter>? filters = null, string? defaultFileName = null, string? defaultExtension = null, string? initialFolder = null)
    {
        using var com = new ComScope();
        IFileSaveDialog* dialog = null;
        IShellItem* item = null;
        try
        {
            Guid clsid = CLSID.CLSID_FileSaveDialog;
            if (CoCreateInstance(&clsid, null, (uint)CLSCTX.CLSCTX_INPROC_SERVER, __uuidof<IFileSaveDialog>(), (void**)&dialog).FAILED) return null;
            var fd = (IFileDialog*)dialog;
            Configure(fd, title, filters, initialFolder);
            uint options;
            fd->GetOptions(&options);
            fd->SetOptions(options | (uint)FILEOPENDIALOGOPTIONS.FOS_OVERWRITEPROMPT | (uint)FILEOPENDIALOGOPTIONS.FOS_FORCEFILESYSTEM);
            if (defaultFileName is not null) fixed (char* p = defaultFileName) fd->SetFileName(p);
            if (defaultExtension is not null) fixed (char* p = defaultExtension.TrimStart('.')) fd->SetDefaultExtension(p);
            if (fd->Show(OwnerOf(owner)).FAILED) return null;
            if (fd->GetResult(&item).FAILED) return null;
            return PathOf(item);
        }
        finally
        {
            Com.Release(ref item);
            Com.Release(ref dialog);
        }
    }

    private static List<string> Open(UIHost? owner, string title, IReadOnlyList<FileFilter>? filters, string? initialFolder, bool multiple, bool folders)
    {
        var result = new List<string>();
        using var com = new ComScope();
        IFileOpenDialog* dialog = null;
        IShellItemArray* items = null;
        try
        {
            Guid clsid = CLSID.CLSID_FileOpenDialog;
            if (CoCreateInstance(&clsid, null, (uint)CLSCTX.CLSCTX_INPROC_SERVER, __uuidof<IFileOpenDialog>(), (void**)&dialog).FAILED) return result;
            var fd = (IFileDialog*)dialog;
            Configure(fd, title, folders ? null : filters, initialFolder);
            uint options;
            fd->GetOptions(&options);
            options |= (uint)FILEOPENDIALOGOPTIONS.FOS_FORCEFILESYSTEM | (uint)FILEOPENDIALOGOPTIONS.FOS_PATHMUSTEXIST;
            if (folders) options |= (uint)FILEOPENDIALOGOPTIONS.FOS_PICKFOLDERS;
            else options |= (uint)FILEOPENDIALOGOPTIONS.FOS_FILEMUSTEXIST;
            if (multiple) options |= (uint)FILEOPENDIALOGOPTIONS.FOS_ALLOWMULTISELECT;
            fd->SetOptions(options);
            if (fd->Show(OwnerOf(owner)).FAILED) return result;
            if (dialog->GetResults(&items).FAILED) return result;
            uint count;
            items->GetCount(&count);
            for (uint i = 0; i < count; i++)
            {
                IShellItem* item = null;
                if (items->GetItemAt(i, &item).SUCCEEDED)
                {
                    if (PathOf(item) is { } path) result.Add(path);
                    Com.Release(ref item);
                }
            }
            return result;
        }
        finally
        {
            Com.Release(ref items);
            Com.Release(ref dialog);
        }
    }

    private static HWND OwnerOf(UIHost? owner) => owner is { IsAttached: true } ? (HWND)owner.WindowHandle : HWND.NULL;

    private static void Configure(IFileDialog* fd, string title, IReadOnlyList<FileFilter>? filters, string? initialFolder)
    {
        fixed (char* t = title) fd->SetTitle(t);

        if (filters is { Count: > 0 })
        {
            var specs = new COMDLG_FILTERSPEC[filters.Count];
            var pins = new System.Runtime.InteropServices.GCHandle[filters.Count * 2];
            try
            {
                for (int i = 0; i < filters.Count; i++)
                {
                    pins[2 * i] = System.Runtime.InteropServices.GCHandle.Alloc(filters[i].Name + "\0", System.Runtime.InteropServices.GCHandleType.Pinned);
                    pins[2 * i + 1] = System.Runtime.InteropServices.GCHandle.Alloc(filters[i].Patterns + "\0", System.Runtime.InteropServices.GCHandleType.Pinned);
                    specs[i].pszName = (char*)pins[2 * i].AddrOfPinnedObject();
                    specs[i].pszSpec = (char*)pins[2 * i + 1].AddrOfPinnedObject();
                }
                fixed (COMDLG_FILTERSPEC* p = specs) fd->SetFileTypes((uint)specs.Length, p);
            }
            finally
            {
                foreach (var pin in pins) if (pin.IsAllocated) pin.Free();
            }
        }

        if (initialFolder is not null && Directory.Exists(initialFolder))
        {
            IShellItem* folder = null;
            fixed (char* path = Path.GetFullPath(initialFolder))
            {
                if (SHCreateItemFromParsingName(path, null, __uuidof<IShellItem>(), (void**)&folder).SUCCEEDED)
                {
                    fd->SetFolder(folder);
                    Com.Release(ref folder);
                }
            }
        }
    }

    private static string? PathOf(IShellItem* item)
    {
        char* path = null;
        if (item->GetDisplayName(SIGDN.SIGDN_FILESYSPATH, &path).FAILED || path is null) return null;
        try
        {
            return new string(path);
        }
        finally
        {
            CoTaskMemFree(path);
        }
    }
}

/// <summary>Shell helpers.</summary>
public static unsafe class Shell
{
    /// <summary>Opens a URL or a document with its default application.</summary>
    public static bool Open(string target)
    {
        ArgumentNullException.ThrowIfNull(target);
        fixed (char* verb = "open")
        fixed (char* file = target)
        {
            return (nint)ShellExecuteW(HWND.NULL, verb, file, null, null, Win32.SW_SHOW) > 32;
        }
    }

    /// <summary>Shows a file selected in Explorer.</summary>
    public static bool RevealInExplorer(string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        fixed (char* verb = "open")
        fixed (char* exe = "explorer.exe")
        fixed (char* args = "/select,\"" + Path.GetFullPath(path) + "\"")
        {
            return (nint)ShellExecuteW(HWND.NULL, verb, exe, args, null, Win32.SW_SHOW) > 32;
        }
    }
}
