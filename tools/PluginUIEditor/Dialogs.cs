// 2026/10/18 16:20:00 UTC - PluginUI
// SPDX-FileCopyrightText: 2026 asm8086
// SPDX-License-Identifier: MIT

using System.Globalization;
using PluginUI.Design;

namespace PluginUIEditor;

/// <summary>Small helpers to lay out dialog rows.</summary>
internal static class DialogLayout
{
    public static TableLayoutPanel Table(int rows)
    {
        var table = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, RowCount = rows + 1, Padding = new Padding(12), AutoSize = true };
        table.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
        table.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        return table;
    }

    public static void Row(TableLayoutPanel table, int row, string label, Control control, Control? extra = null)
    {
        table.Controls.Add(new Label { Text = label, AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(3, 7, 9, 3) }, 0, row);
        control.Dock = DockStyle.Fill;
        table.Controls.Add(control, 1, row);
        if (extra is not null) table.Controls.Add(extra, 2, row);
        else table.SetColumnSpan(control, 2);
    }

    public static FlowLayoutPanel Buttons(Form form, string ok, string cancel)
    {
        var panel = new FlowLayoutPanel { Dock = DockStyle.Bottom, FlowDirection = FlowDirection.RightToLeft, AutoSize = true, Padding = new Padding(8) };
        var cancelButton = new Button { Text = cancel, DialogResult = DialogResult.Cancel, AutoSize = true };
        var okButton = new Button { Text = ok, DialogResult = DialogResult.OK, AutoSize = true };
        panel.Controls.Add(cancelButton);
        panel.Controls.Add(okButton);
        form.AcceptButton = okButton;
        form.CancelButton = cancelButton;
        return panel;
    }

    public static Button BrowseFolder(TextBox target, string description)
    {
        var button = new Button { Text = EditorStrings.Current.Browse, AutoSize = true };
        button.Click += (_, _) =>
        {
            using var dialog = new FolderBrowserDialog { Description = description, UseDescriptionForTitle = true, SelectedPath = target.Text };
            if (dialog.ShowDialog() == DialogResult.OK) target.Text = dialog.SelectedPath;
        };
        return button;
    }

    public static Form Dialog(string title, int width)
    {
        var scale = 1f;
        return new Form
        {
            Text = title,
            FormBorderStyle = FormBorderStyle.FixedDialog,
            StartPosition = FormStartPosition.CenterParent,
            MinimizeBox = false,
            MaximizeBox = false,
            ShowInTaskbar = false,
            AutoScaleMode = AutoScaleMode.Dpi,
            ClientSize = new System.Drawing.Size((int)(width * scale), 10),
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
        };
    }
}

/// <summary>Tools ▸ Options: interface language, snap grid, source folders of new projects.</summary>
internal static class OptionsDialog
{
    public static bool Show(IWin32Window owner, EditorSettings settings)
    {
        var t = EditorStrings.Current;
        using var form = DialogLayout.Dialog(t.OptionsTitle, 560);
        var table = DialogLayout.Table(5);
        var language = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList };
        language.Items.AddRange([t.LanguageAuto, "English", "Français"]);
        language.SelectedIndex = settings.Language switch { "en" => 1, "fr" => 2, _ => 0 };
        var grid = new NumericUpDown { Minimum = 0, Maximum = 64, DecimalPlaces = 0, Value = (decimal)Math.Clamp(settings.GridSize, 0f, 64f) };
        var nplug = new TextBox { Text = settings.NPlugRoot, Width = 340 };
        var pluginui = new TextBox { Text = settings.PluginUIRoot, Width = 340 };
        DialogLayout.Row(table, 0, t.OptionLanguage, language);
        DialogLayout.Row(table, 1, t.OptionGrid, grid);
        DialogLayout.Row(table, 2, t.OptionNPlugRoot, nplug, DialogLayout.BrowseFolder(nplug, t.OptionNPlugRoot));
        DialogLayout.Row(table, 3, t.OptionPluginUIRoot, pluginui, DialogLayout.BrowseFolder(pluginui, t.OptionPluginUIRoot));
        var extensions = new TextBox { Multiline = true, ScrollBars = ScrollBars.Vertical, Height = 72, WordWrap = false, Text = string.Join(Environment.NewLine, settings.Extensions) };
        var addExtension = new Button { Text = t.Browse, AutoSize = true };
        addExtension.Click += (_, _) =>
        {
            using var dialog = new OpenFileDialog { Filter = "*.dll|*.dll", Multiselect = true };
            if (dialog.ShowDialog() != DialogResult.OK) return;
            foreach (var file in dialog.FileNames) extensions.AppendText((extensions.TextLength > 0 ? Environment.NewLine : string.Empty) + file);
        };
        DialogLayout.Row(table, 4, t.OptionExtensions, extensions, addExtension);
        form.Controls.Add(table);
        form.Controls.Add(DialogLayout.Buttons(form, t.Ok, t.Cancel));
        if (form.ShowDialog(owner) != DialogResult.OK) return false;
        settings.Language = language.SelectedIndex switch { 1 => "en", 2 => "fr", _ => "auto" };
        settings.GridSize = (float)grid.Value;
        settings.NPlugRoot = nplug.Text.Trim();
        settings.PluginUIRoot = pluginui.Text.Trim();
        settings.Extensions = extensions.Lines.Select(l => l.Trim()).Where(l => l.Length > 0).ToList();
        settings.Save();
        return true;
    }
}

/// <summary>File ▸ New project from design.</summary>
internal static class NewProjectDialog
{
    public static (ScaffoldOptions Options, string Folder)? Show(IWin32Window owner, EditorSettings settings, DesignDocument design)
    {
        var t = EditorStrings.Current;
        using var form = DialogLayout.Dialog(t.NewProjectTitle, 600);
        var table = DialogLayout.Table(7);
        string suggested = CSharpDesignGenerator.IsIdentifier(design.Name) && design.Name != "Editor" ? design.Name.Replace("Editor", string.Empty, StringComparison.Ordinal) : "MyPlugin";
        if (!CSharpDesignGenerator.IsIdentifier(suggested)) suggested = "MyPlugin";
        var name = new TextBox { Text = suggested };
        var kind = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList };
        kind.Items.AddRange([t.KindEffect, t.KindInstrument, t.KindStandalone]);
        kind.SelectedIndex = design.AllNodes().Any(n => n.Type is "PianoKeyboard" or "PitchWheel" or "ModWheel") ? 1 : 0;
        var folder = new TextBox { Text = settings.ProjectFolder.Length > 0 ? settings.ProjectFolder : Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), Width = 360 };
        var vendor = new TextBox { Text = settings.Vendor };
        var url = new TextBox { Text = "https://example.com" };
        var email = new TextBox { Text = "contact@example.com" };
        var live = new CheckBox { Text = t.LiveBridge, Checked = true, AutoSize = true };
        DialogLayout.Row(table, 0, t.ProjectName, name);
        DialogLayout.Row(table, 1, t.ProjectKind, kind);
        DialogLayout.Row(table, 2, t.ProjectFolder, folder, DialogLayout.BrowseFolder(folder, t.ProjectFolder));
        DialogLayout.Row(table, 3, t.Vendor, vendor);
        DialogLayout.Row(table, 4, t.VendorUrl, url);
        DialogLayout.Row(table, 5, t.VendorEmail, email);
        DialogLayout.Row(table, 6, string.Empty, live);
        form.Controls.Add(table);
        var buttons = DialogLayout.Buttons(form, t.Create, t.Cancel);
        form.Controls.Add(buttons);
        form.FormClosing += (_, e) =>
        {
            if (form.DialogResult == DialogResult.OK && ProjectScaffolder.ValidateProjectName(name.Text.Trim()) is { } error)
            {
                string message = CSharpDesignGenerator.IsIdentifier(name.Text.Trim()) ? error : t.InvalidProjectName;
                MessageBox.Show(form, message, t.NewProjectTitle, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                e.Cancel = true;
            }
        };
        if (form.ShowDialog(owner) != DialogResult.OK) return null;

        settings.ProjectFolder = folder.Text.Trim();
        settings.Vendor = vendor.Text.Trim();
        settings.Save();
        string project = name.Text.Trim();
        var options = new ScaffoldOptions
        {
            ProjectName = project,
            Kind = (ScaffoldKind)kind.SelectedIndex,
            Vendor = vendor.Text.Trim(),
            Url = url.Text.Trim(),
            Email = email.Text.Trim(),
            IncludeLiveBridge = live.Checked,
        };
        string target = Path.Combine(folder.Text.Trim(), project);
        if (settings.NPlugRoot.Length > 0) options.NPlugRoot = MsBuildPath(target, settings.NPlugRoot);
        if (settings.PluginUIRoot.Length > 0) options.PluginUIRoot = MsBuildPath(target, settings.PluginUIRoot);
        return (options, target);
    }

    /// <summary>Folder as written in the project: relative to the project when possible.</summary>
    private static string MsBuildPath(string projectFolder, string folder)
    {
        string full = Path.GetFullPath(folder);
        string relative = Path.GetRelativePath(projectFolder, full);
        string path = Path.IsPathRooted(relative) ? full : "$(MSBuildThisFileDirectory)" + relative;
        return path.EndsWith('\\') || path.EndsWith('/') ? path : path + "\\";
    }
}

/// <summary>Help ▸ About.</summary>
internal static class AboutDialog
{
    public static void Show(IWin32Window owner)
    {
        var t = EditorStrings.Current;
        string version = typeof(AboutDialog).Assembly.GetName().Version?.ToString(3) ?? "0.1.0";
        MessageBox.Show(owner, string.Format(CultureInfo.InvariantCulture, t.AboutText, version), t.AboutTitle, MessageBoxButtons.OK, MessageBoxIcon.Information);
    }
}
