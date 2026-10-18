// 2026/10/18 16:20:00 UTC - PluginUI
// SPDX-FileCopyrightText: 2026 asm8086
// SPDX-License-Identifier: MIT

using System.Diagnostics;
using System.Globalization;
using PluginUI.Design;

namespace PluginUIEditor;

/// <summary>
/// Main window: toolbox and outline on the left, the design surface in the middle, properties, design settings,
/// parameters and messages on the right.
/// </summary>
internal sealed class MainForm : Form
{
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    private readonly EditorSettings _settings;
    private readonly DesignSession _session;
    private readonly DesignSurface _surface;
    private readonly MenuStrip _menu = new();
    private readonly ToolStrip _tools = new() { GripStyle = ToolStripGripStyle.Hidden };
    private readonly StatusStrip _status = new();
    private readonly ToolStripStatusLabel _statusText = new() { Spring = true, TextAlign = System.Drawing.ContentAlignment.MiddleLeft };
    private readonly ToolStripStatusLabel _statusCount = new();
    private readonly ToolStripStatusLabel _statusZoom = new();
    private readonly TreeView _toolbox = new() { Dock = DockStyle.Fill, HideSelection = false, ShowLines = false };
    private readonly TreeView _outline = new() { Dock = DockStyle.Fill, HideSelection = false, AllowDrop = true, LabelEdit = true };
    private readonly PropertyGrid _properties = new() { Dock = DockStyle.Fill, ToolbarVisible = true, PropertySort = PropertySort.Categorized };
    private readonly PropertyGrid _design = new() { Dock = DockStyle.Fill, ToolbarVisible = false, PropertySort = PropertySort.Categorized };
    private readonly DataGridView _parameters = new() { Dock = DockStyle.Fill, AllowUserToAddRows = true, AllowUserToDeleteRows = true, AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.DisplayedCells, RowHeadersWidth = 24 };
    private readonly ListBox _messages = new() { Dock = DockStyle.Fill, HorizontalScrollbar = true, IntegralHeight = false };
    private readonly TabControl _tabs = new() { Dock = DockStyle.Fill };
    private readonly Label _toolboxTitle = Title();
    private readonly Label _outlineTitle = Title();
    private readonly TabPage _propertiesPage = new(), _designPage = new(), _parametersPage = new(), _messagesPage = new();
    private bool _refreshingOutline;
    private bool _refreshingParameters;

    public MainForm(EditorSettings settings, string? path)
    {
        _settings = settings;
        var registry = DesignTypeRegistry.CreateDefault();
        var extensionMessages = Extensions.Load(registry, settings.Extensions);
        _session = new DesignSession(registry) { GridSize = settings.GridSize };
        _surface = new DesignSurface(_session) { Dock = DockStyle.Fill };
        AutoScaleMode = AutoScaleMode.Dpi;
        ClientSize = new System.Drawing.Size(1400, 860);
        StartPosition = FormStartPosition.CenterScreen;
        KeyPreview = false;
        Icon = System.Drawing.SystemIcons.Application;

        BuildLayout();
        ApplyTexts();
        WireEvents();
        if (path is not null) Open(path);
        else RefreshAll();
        _startupMessages.AddRange(extensionMessages);
        RenderMessages();
    }

    private static Label Title() => new() { Dock = DockStyle.Top, Height = 24, TextAlign = System.Drawing.ContentAlignment.MiddleLeft, Padding = new Padding(6, 0, 0, 0), BackColor = System.Drawing.SystemColors.ControlLight };

    // =====================================================================================
    // Layout and texts
    // =====================================================================================

    private void BuildLayout()
    {
        var left = new SplitContainer { Dock = DockStyle.Fill, Orientation = Orientation.Horizontal, SplitterDistance = 420 };
        left.Panel1.Controls.Add(_toolbox);
        left.Panel1.Controls.Add(_toolboxTitle);
        left.Panel2.Controls.Add(_outline);
        left.Panel2.Controls.Add(_outlineTitle);

        _propertiesPage.Controls.Add(_properties);
        _designPage.Controls.Add(_design);
        _parametersPage.Controls.Add(_parameters);
        _messagesPage.Controls.Add(_messages);
        _tabs.TabPages.AddRange([_propertiesPage, _designPage, _parametersPage, _messagesPage]);

        var right = new SplitContainer { Dock = DockStyle.Fill, FixedPanel = FixedPanel.Panel2 };
        right.Panel1.Controls.Add(_surface);
        right.Panel2.Controls.Add(_tabs);

        var main = new SplitContainer { Dock = DockStyle.Fill, FixedPanel = FixedPanel.Panel1 };
        main.Panel1.Controls.Add(left);
        main.Panel2.Controls.Add(right);

        _status.Items.AddRange([_statusText, _statusCount, _statusZoom]);
        Controls.Add(main);
        Controls.Add(_tools);
        Controls.Add(_menu);
        Controls.Add(_status);
        MainMenuStrip = _menu;
        Load += (_, _) =>
        {
            main.SplitterDistance = 230;
            right.SplitterDistance = Math.Max(400, right.Width - 380);
        };
        Shown += (_, _) => BeginInvoke(FitIfNeeded);

        _parameters.Columns.Add(new DataGridViewTextBoxColumn { Name = "Name" });
        _parameters.Columns.Add(new DataGridViewComboBoxColumn { Name = "Kind", DataSource = Enum.GetNames<DesignParameterKind>(), FlatStyle = FlatStyle.Flat });
        foreach (var column in new[] { "Min", "Max", "Default", "Unit", "Decimals", "Choices" }) _parameters.Columns.Add(new DataGridViewTextBoxColumn { Name = column });
        // Rows map to parameters through their Tag: sorting would only confuse the edits.
        foreach (DataGridViewColumn column in _parameters.Columns) column.SortMode = DataGridViewColumnSortMode.NotSortable;
    }

    /// <summary>Menus, toolbar and panel titles in the current language (rebuilt when the language changes).</summary>
    private void ApplyTexts()
    {
        var t = EditorStrings.Current;
        _menu.Items.Clear();
        _tools.Items.Clear();

        var recent = new ToolStripMenuItem(t.MenuOpenRecent);
        foreach (var file in _settings.RecentFiles.Where(File.Exists)) recent.DropDownItems.Add(file, null, (_, _) => { if (ConfirmDiscard()) Open(file); });
        recent.Enabled = recent.DropDownItems.Count > 0;

        _menu.Items.Add(MenuOf(t.MenuFile,
            Item(t.MenuNew, Keys.Control | Keys.N, (_, _) => { if (ConfirmDiscard()) NewDocument(); }),
            Item(t.MenuOpen, Keys.Control | Keys.O, (_, _) => OpenDialog()),
            recent,
            Item(t.MenuSave, Keys.Control | Keys.S, (_, _) => Save(false)),
            Item(t.MenuSaveAs, Keys.Control | Keys.Shift | Keys.S, (_, _) => Save(true)),
            new ToolStripSeparator(),
            Item(t.MenuImportTheme, Keys.None, (_, _) => ImportTheme()),
            MenuOf(t.MenuExport,
                Item(t.MenuExportPng1, Keys.None, (_, _) => ExportPng(1f)),
                Item(t.MenuExportPng2, Keys.None, (_, _) => ExportPng(2f)),
                Item(t.MenuExportTheme, Keys.None, (_, _) => ExportTheme()),
                Item(t.MenuExportAssets, Keys.None, (_, _) => ExportAssets())),
            Item(t.MenuGenerateCode, Keys.Control | Keys.G, (_, _) => GenerateCode()),
            Item(t.MenuNewProject, Keys.Control | Keys.Shift | Keys.N, (_, _) => NewProject()),
            new ToolStripSeparator(),
            Item(t.MenuExit, Keys.Alt | Keys.F4, (_, _) => Close())));

        // Edit commands use shortcuts only when the design has the focus (see ProcessCmdKey): text boxes keep theirs.
        _menu.Items.Add(MenuOf(t.MenuEdit,
            Item(t.MenuUndo, Keys.None, (_, _) => _session.Undo(), "Ctrl+Z"),
            Item(t.MenuRedo, Keys.None, (_, _) => _session.Redo(), "Ctrl+Y"),
            new ToolStripSeparator(),
            Item(t.MenuCut, Keys.None, (_, _) => Cut(), "Ctrl+X"),
            Item(t.MenuCopy, Keys.None, (_, _) => CopySelection(), "Ctrl+C"),
            Item(t.MenuPaste, Keys.None, (_, _) => Paste(), "Ctrl+V"),
            Item(t.MenuDuplicate, Keys.None, (_, _) => Duplicate(), "Ctrl+D"),
            Item(t.MenuDelete, Keys.None, (_, _) => DeleteSelection(), "Del"),
            new ToolStripSeparator(),
            Item(t.MenuBringToFront, Keys.None, (_, _) => { if (_session.Selected is { } n) _session.MoveInParent(n, true); }, "Ctrl+Shift+]"),
            Item(t.MenuSendToBack, Keys.None, (_, _) => { if (_session.Selected is { } n) _session.MoveInParent(n, false); }, "Ctrl+Shift+["),
            Item(t.MenuSelectParent, Keys.None, (_, _) => SelectParent(), "Alt+Up")));

        var preview = Item(t.MenuPreview, Keys.F5, (_, _) => TogglePreview());
        preview.Checked = _surface.PreviewMode;
        var outlines = Item(t.MenuShowOutlines, Keys.None, (s, _) => { _surface.ShowOutlines = !_surface.ShowOutlines; ((ToolStripMenuItem)s!).Checked = _surface.ShowOutlines; });
        outlines.Checked = _surface.ShowOutlines;
        _menu.Items.Add(MenuOf(t.MenuView,
            Item(t.MenuZoomIn, Keys.Control | Keys.Oemplus, (_, _) => SetZoom(_surface.Zoom * 1.25f)),
            Item(t.MenuZoomOut, Keys.Control | Keys.OemMinus, (_, _) => SetZoom(_surface.Zoom / 1.25f)),
            Item(t.MenuZoom100, Keys.Control | Keys.D0, (_, _) => SetZoom(1f)),
            Item(t.MenuZoomFit, Keys.Control | Keys.D9, (_, _) => SetZoom(_surface.FitZoom())),
            new ToolStripSeparator(),
            preview,
            outlines));
        _menu.Items.Add(MenuOf(t.MenuTools, Item(t.MenuOptions, Keys.None, (_, _) => ShowOptions())));
        _menu.Items.Add(MenuOf(t.MenuHelp,
            Item(t.MenuDocumentation, Keys.F1, (_, _) => OpenDocumentation()),
            Item(t.MenuAbout, Keys.None, (_, _) => AboutDialog.Show(this))));

        // Text buttons: they are translated and render with any font (Wine included), unlike symbol glyphs.
        _tools.Items.Add(Button(t.MenuNew, (_, _) => { if (ConfirmDiscard()) NewDocument(); }));
        _tools.Items.Add(Button(t.MenuOpen, (_, _) => OpenDialog()));
        _tools.Items.Add(Button(t.MenuSave, (_, _) => Save(false)));
        _tools.Items.Add(new ToolStripSeparator());
        _tools.Items.Add(Button(t.MenuUndo, (_, _) => _session.Undo()));
        _tools.Items.Add(Button(t.MenuRedo, (_, _) => _session.Redo()));
        _tools.Items.Add(Button(t.MenuDelete, (_, _) => DeleteSelection()));
        _tools.Items.Add(new ToolStripSeparator());
        _tools.Items.Add(Button("−", (_, _) => SetZoom(_surface.Zoom / 1.25f), t.MenuZoomOut));
        _tools.Items.Add(Button("1:1", (_, _) => SetZoom(1f), t.MenuZoom100));
        _tools.Items.Add(Button("+", (_, _) => SetZoom(_surface.Zoom * 1.25f), t.MenuZoomIn));
        _tools.Items.Add(Button(t.ToolFit, (_, _) => SetZoom(_surface.FitZoom()), t.MenuZoomFit));
        _tools.Items.Add(new ToolStripSeparator());
        _tools.Items.Add(Button(t.MenuPreview, (_, _) => TogglePreview()));
        _tools.Items.Add(Button(t.MenuGenerateCode, (_, _) => GenerateCode()));

        _toolboxTitle.Text = t.PanelToolbox;
        _outlineTitle.Text = t.PanelOutline;
        _propertiesPage.Text = t.PanelProperties;
        _designPage.Text = t.PanelDesign;
        _parametersPage.Text = t.PanelParameters;
        _messagesPage.Text = t.PanelMessages;
        string[] headers = [t.ColumnName, t.ColumnKind, t.ColumnMin, t.ColumnMax, t.ColumnDefault, t.ColumnUnit, t.ColumnDecimals, t.ColumnChoices];
        for (int i = 0; i < headers.Length; i++) _parameters.Columns[i].HeaderText = headers[i];
        FillToolbox();
        RefreshAll();
    }

    private static ToolStripMenuItem MenuOf(string text, params ToolStripItem[] items)
    {
        var menu = new ToolStripMenuItem(text);
        menu.DropDownItems.AddRange(items);
        return menu;
    }

    private static ToolStripMenuItem Item(string text, Keys keys, EventHandler click, string? display = null)
    {
        var item = new ToolStripMenuItem(text, null, click);
        if (keys != Keys.None && keys != (Keys.Alt | Keys.F4)) item.ShortcutKeys = keys;
        if (display is not null) item.ShortcutKeyDisplayString = display;
        else if (keys == (Keys.Alt | Keys.F4)) item.ShortcutKeyDisplayString = "Alt+F4";
        return item;
    }

    private static ToolStripButton Button(string text, EventHandler click, string? tip = null)
    {
        static string Plain(string s) => s.Replace("&", string.Empty, StringComparison.Ordinal).Replace("…", string.Empty, StringComparison.Ordinal)
            .Replace("(.Designer.cs)", string.Empty, StringComparison.Ordinal).Trim();
        return new ToolStripButton(Plain(text), null, click) { ToolTipText = Plain(tip ?? text), DisplayStyle = ToolStripItemDisplayStyle.Text };
    }

    private void FillToolbox()
    {
        _toolbox.BeginUpdate();
        _toolbox.Nodes.Clear();
        foreach (var category in BuiltInDesignTypes.Categories)
        {
            var group = _toolbox.Nodes.Add(EditorStrings.Current.Toolbox(category));
            foreach (var type in _session.Registry.Types.Where(t => t.ToolboxCategory == category))
            {
                group.Nodes.Add(new TreeNode(type.Name) { Tag = type.Name, ToolTipText = XmlDocs.TypeSummary(type.Type) });
            }
            group.Expand();
        }
        foreach (var custom in _session.Registry.Types.Where(t => t.ToolboxCategory is not null && !BuiltInDesignTypes.Categories.Contains(t.ToolboxCategory)).GroupBy(t => t.ToolboxCategory!))
        {
            var group = _toolbox.Nodes.Add(custom.Key);
            foreach (var type in custom) group.Nodes.Add(new TreeNode(type.Name) { Tag = type.Name });
            group.Expand();
        }
        _toolbox.ShowNodeToolTips = true;
        _toolbox.EndUpdate();
    }

    // =====================================================================================
    // Synchronization
    // =====================================================================================

    private void WireEvents()
    {
        _session.Changed += change =>
        {
            switch (change)
            {
                case DesignChange.Selection:
                    SelectInOutline();
                    ShowProperties();
                    break;
                case DesignChange.Layout:
                    _properties.Refresh();
                    UpdateTitle();
                    break;
                case DesignChange.Property:
                    _properties.Refresh();
                    RefreshOutline();
                    UpdateTitle();
                    break;
                default:
                    ScheduleRefresh();
                    break;
            }
        };
        _surface.Rebuilt += () =>
        {
            _statusCount.Text = string.Format(Inv, EditorStrings.Current.ElementsFormat, _session.Document.AllNodes().Count());
            _statusZoom.Text = string.Format(Inv, EditorStrings.Current.ZoomFormat, (int)MathF.Round(_surface.Zoom * 100f));
            _buildMessages.Clear();
            _buildMessages.AddRange(_surface.Warnings);
            RenderMessages();
        };

        _toolbox.ItemDrag += (_, e) =>
        {
            if (e.Item is TreeNode { Tag: string type }) DoDragDrop(new DataObject(DesignSurface.ToolboxFormat, type), DragDropEffects.Copy);
        };
        _toolbox.NodeMouseDoubleClick += (_, e) =>
        {
            if (e.Node?.Tag is string type) _surface.Drop(type, null);
        };

        _outline.AfterSelect += (_, e) =>
        {
            if (!_refreshingOutline && e.Node?.Tag is DesignNode node) _session.Selected = node;
        };
        _outline.BeforeLabelEdit += (_, e) =>
        {
            if (e.Node?.Tag is DesignNode node) e.Node.Text = node.Name ?? string.Empty;
        };
        _outline.AfterLabelEdit += (_, e) =>
        {
            if (e.Node?.Tag is DesignNode node && e.Label is not null) BeginInvoke(() => { if (!_session.Rename(node, e.Label)) SetStatus(EditorStrings.Current.InvalidElementName); });
            e.CancelEdit = true;
            BeginInvoke(RefreshOutline);
        };
        _outline.KeyDown += (_, e) =>
        {
            if (e.KeyCode == Keys.F2 && _outline.SelectedNode is { } selected)
            {
                selected.BeginEdit();
                e.Handled = true;
            }
        };
        _outline.ItemDrag += (_, e) =>
        {
            if (e.Item is TreeNode { Tag: DesignNode node } && !ReferenceEquals(node, _session.Document.Root)) DoDragDrop(node, DragDropEffects.Move);
        };
        _outline.DragOver += (_, e) => e.Effect = e.Data?.GetDataPresent(typeof(DesignNode)) == true ? DragDropEffects.Move : DragDropEffects.None;
        _outline.DragDrop += (_, e) =>
        {
            if (e.Data?.GetData(typeof(DesignNode)) is not DesignNode moved) return;
            var target = _outline.GetNodeAt(_outline.PointToClient(new System.Drawing.Point(e.X, e.Y)))?.Tag as DesignNode;
            if (target is null || ReferenceEquals(target, moved)) return;
            bool done = _session.CanAccept(target)
                ? _session.Reparent(moved, target, -1)
                : _session.ParentOf(target) is { } parent && _session.Reparent(moved, parent, parent.Children.IndexOf(target));
            if (!done) SetStatus(string.Format(Inv, EditorStrings.Current.CannotHoldChildren, target));
        };

        _properties.PropertyValueChanged += (_, _) => UpdateTitle();
        _parameters.CellEndEdit += (_, e) => CommitParameterRow(e.RowIndex);
        _parameters.UserDeletingRow += (_, e) =>
        {
            if (e.Row?.Tag is not DesignParameter parameter) return; // the new-row placeholder
            // The table is rebuilt from the document after the change: let the grid finish its own work first.
            e.Cancel = true;
            BeginInvoke(() => _session.EditSettings(() => _session.Document.Parameters.Remove(parameter)));
        };
        _parameters.DataError += (_, e) => e.ThrowException = false;

        FormClosing += (_, e) =>
        {
            if (!ConfirmDiscard()) e.Cancel = true;
            else _settings.Save();
        };
    }

    private bool _refreshPending;

    // Changes often come from a property grid or the parameter table while they are still committing a value:
    // replacing their content right away is unsafe, so the panels are refreshed once the current message is done.
    private void ScheduleRefresh()
    {
        if (!IsHandleCreated)
        {
            RefreshAll();
            return;
        }
        if (_refreshPending) return;
        _refreshPending = true;
        BeginInvoke(() =>
        {
            _refreshPending = false;
            RefreshAll();
        });
    }

    private void RefreshAll()
    {
        RefreshOutline();
        ShowProperties();
        _design.SelectedObject = DesignSettingsAdapter.Create(_session);
        RefreshParameters();
        UpdateTitle();
    }

    private void UpdateTitle()
    {
        Text = $"PluginUIEditor — {_session.DisplayName}{(_session.IsDirty ? " *" : string.Empty)}";
        if (string.IsNullOrEmpty(_statusText.Text)) _statusText.Text = EditorStrings.Current.Ready;
    }

    private void ShowProperties()
    {
        _properties.SelectedObject = _session.Selected is { } node ? NodeAdapter.Create(_session, node) : NodeAdapter.Create(_session, _session.Document.Root);
    }

    private void RefreshOutline()
    {
        _refreshingOutline = true;
        try
        {
            _outline.BeginUpdate();
            _outline.Nodes.Clear();
            _outline.Nodes.Add(OutlineNode(_session.Document.Root));
            _outline.ExpandAll();
            _outline.EndUpdate();
            SelectInOutline();
        }
        finally
        {
            _refreshingOutline = false;
        }
    }

    private static TreeNode OutlineNode(DesignNode node)
    {
        string text = node.ToString() + (node.Slot is null ? string.Empty : $"  [{node.Slot}]");
        var tree = new TreeNode(text) { Tag = node };
        foreach (var child in node.Children) tree.Nodes.Add(OutlineNode(child));
        return tree;
    }

    private void SelectInOutline()
    {
        bool previous = _refreshingOutline;
        _refreshingOutline = true;
        try
        {
            var target = _session.Selected ?? _session.Document.Root;
            _outline.SelectedNode = Find(_outline.Nodes, target);
        }
        finally
        {
            _refreshingOutline = previous;
        }
    }

    private static TreeNode? Find(TreeNodeCollection nodes, DesignNode target)
    {
        foreach (TreeNode n in nodes)
        {
            if (ReferenceEquals(n.Tag, target)) return n;
            if (Find(n.Nodes, target) is { } found) return found;
        }
        return null;
    }

    private void RefreshParameters()
    {
        if (_parameters.IsCurrentCellInEditMode) return;
        _refreshingParameters = true;
        try
        {
            _parameters.Rows.Clear();
            foreach (var p in _session.Document.Parameters)
            {
                int row = _parameters.Rows.Add(p.Name, p.Kind.ToString(), p.Min.ToString("R", Inv), p.Max.ToString("R", Inv), p.Default.ToString("R", Inv),
                    p.Unit, p.Decimals.ToString(Inv), string.Join(", ", p.Choices));
                _parameters.Rows[row].Tag = p;
            }
        }
        finally
        {
            _refreshingParameters = false;
        }
    }

    private void CommitParameterRow(int row)
    {
        if (_refreshingParameters || row < 0 || row >= _parameters.Rows.Count || _parameters.Rows[row].IsNewRow) return;
        var gridRow = _parameters.Rows[row];
        var cells = gridRow.Cells;
        string name = (cells[0].Value as string ?? string.Empty).Trim();
        if (name.Length == 0) return;
        var target = gridRow.Tag as DesignParameter;
        if (_session.Document.Parameters.Any(p => !ReferenceEquals(p, target) && p.Name == name))
        {
            SetStatus(string.Format(Inv, EditorStrings.Current.DuplicateParameterFormat, name));
            BeginInvoke(RefreshParameters);
            return;
        }
        // Values are read now: the table may be rebuilt before the change runs.
        string? kindText = cells[1].Value as string, minText = cells[2].Value as string, maxText = cells[3].Value as string;
        string? defaultText = cells[4].Value as string, unitText = cells[5].Value as string, decimalsText = cells[6].Value as string;
        string choicesText = cells[7].Value as string ?? string.Empty;
        BeginInvoke(() => _session.EditSettings(() =>
        {
            var list = _session.Document.Parameters;
            var p = target ?? new DesignParameter();
            if (target is null)
            {
                list.Add(p);
                // Until the table is rebuilt, further edits of this row update the same parameter.
                if (gridRow.DataGridView is not null) gridRow.Tag = p;
            }
            else if (!list.Contains(target)) return; // the document changed meanwhile (undo)
            string oldName = p.Name;
            p.Name = name;
            p.Kind = Enum.TryParse<DesignParameterKind>(kindText, out var kind) ? kind : DesignParameterKind.Linear;
            p.Min = InvariantNumberParse(minText, p.Min);
            p.Max = InvariantNumberParse(maxText, p.Max);
            p.Default = InvariantNumberParse(defaultText, p.Default);
            p.Unit = (unitText ?? string.Empty).Trim();
            p.Decimals = (int)Math.Clamp(InvariantNumberParse(decimalsText, p.Decimals), 0, 9);
            p.Choices.Clear();
            p.Choices.AddRange(choicesText.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
            if (target is not null && oldName != name)
            {
                // Renaming a parameter keeps the controls bound to it.
                foreach (var node in _session.Document.AllNodes())
                {
                    foreach (var key in node.Bindings.Where(b => b.Value == oldName).Select(b => b.Key).ToList()) node.Bindings[key] = name;
                }
            }
        }));
    }

    private static double InvariantNumberParse(string? text, double fallback)
        => text is not null && PluginUI.InvariantNumber.TryParse(text.Trim(), out var v) ? v : fallback;

    // Messages: extension loading (whole session), file and command messages (until the next New / Open) and
    // the warnings of the last build of the design surface.
    private readonly List<string> _startupMessages = new();
    private readonly List<string> _documentMessages = new();
    private readonly List<string> _buildMessages = new();

    private void ShowMessages(IEnumerable<string> messages)
    {
        int before = _documentMessages.Count;
        _documentMessages.AddRange(messages);
        RenderMessages();
        if (_documentMessages.Count > before) _tabs.SelectedTab = _messagesPage;
    }

    private void RenderMessages()
    {
        _messages.BeginUpdate();
        _messages.Items.Clear();
        foreach (var m in _startupMessages.Concat(_documentMessages).Concat(_buildMessages)) _messages.Items.Add(m);
        _messages.EndUpdate();
        _messagesPage.Text = EditorStrings.Current.PanelMessages + (_messages.Items.Count > 0 ? $" ({_messages.Items.Count.ToString(Inv)})" : string.Empty);
    }

    private void NewDocument()
    {
        _documentMessages.Clear();
        _session.New();
    }

    private void SetStatus(string text) => _statusText.Text = text;

    // =====================================================================================
    // Keyboard: edit shortcuts act on the design only when it has the focus
    // =====================================================================================

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        // In interactive preview the controls of the design own the keyboard (text boxes, number boxes).
        bool designFocused = _outline.Focused || (!_surface.PreviewMode && _surface.ContainsFocus);
        if (designFocused)
        {
            switch (keyData)
            {
                case Keys.Control | Keys.Z: _session.Undo(); return true;
                case Keys.Control | Keys.Y:
                case Keys.Control | Keys.Shift | Keys.Z: _session.Redo(); return true;
                case Keys.Control | Keys.X: Cut(); return true;
                case Keys.Control | Keys.C: CopySelection(); return true;
                case Keys.Control | Keys.V: Paste(); return true;
                case Keys.Control | Keys.D: Duplicate(); return true;
                case Keys.Delete: DeleteSelection(); return true;
                case Keys.Alt | Keys.Up: SelectParent(); return true;
                case Keys.Control | Keys.Shift | Keys.OemCloseBrackets: if (_session.Selected is { } f) _session.MoveInParent(f, true); return true;
                case Keys.Control | Keys.Shift | Keys.OemOpenBrackets: if (_session.Selected is { } b) _session.MoveInParent(b, false); return true;
            }
        }
        return base.ProcessCmdKey(ref msg, keyData);
    }

    // =====================================================================================
    // Snapshot mode (documentation)
    // =====================================================================================

    private bool _forceClose;

    internal void SelectFirstNamedElement()
    {
        var node = _session.Document.AllNodes().Skip(1).FirstOrDefault(n => n.Name is not null);
        if (node is not null) _session.Selected = node;
    }

    internal void ForceClose()
    {
        _forceClose = true;
        Close();
    }

    // =====================================================================================
    // Commands
    // =====================================================================================

    private bool ConfirmDiscard()
    {
        if (_forceClose || !_session.IsDirty) return true;
        var answer = MessageBox.Show(this, string.Format(Inv, EditorStrings.Current.SaveChangesFormat, _session.DisplayName), Application.ProductName,
            MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question);
        return answer switch
        {
            DialogResult.Yes => Save(false),
            DialogResult.No => true,
            _ => false,
        };
    }

    private void OpenDialog()
    {
        if (!ConfirmDiscard()) return;
        using var dialog = new OpenFileDialog { Filter = EditorStrings.Current.DesignFilter };
        if (dialog.ShowDialog(this) == DialogResult.OK) Open(dialog.FileName);
    }

    private void Open(string path)
    {
        try
        {
            path = Path.GetFullPath(path);
            var warnings = _session.Open(path);
            _documentMessages.Clear();
            _settings.AddRecent(Path.GetFullPath(path));
            _settings.Save();
            ApplyTexts();
            ShowMessages(warnings);
            if (IsHandleCreated) BeginInvoke(FitIfNeeded);
        }
        catch (Exception e) when (e is IOException or FormatException or UnauthorizedAccessException)
        {
            MessageBox.Show(this, string.Format(Inv, EditorStrings.Current.OpenFailedFormat, path, e.Message), Application.ProductName, MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private bool Save(bool saveAs)
    {
        string? path = _session.FilePath;
        if (saveAs || path is null)
        {
            using var dialog = new SaveFileDialog { Filter = EditorStrings.Current.DesignFilter, DefaultExt = "pluginui", FileName = _session.Document.Name + DesignDocument.FileExtension };
            if (dialog.ShowDialog(this) != DialogResult.OK) return false;
            path = dialog.FileName;
        }
        try
        {
            _session.Save(path);
            _settings.AddRecent(path);
            _settings.Save();
            SetStatus(path);
            UpdateTitle();
            return true;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            MessageBox.Show(this, string.Format(Inv, EditorStrings.Current.SaveFailedFormat, path, e.Message), Application.ProductName, MessageBoxButtons.OK, MessageBoxIcon.Error);
            return false;
        }
    }

    private void ImportTheme()
    {
        using var dialog = new OpenFileDialog { Filter = EditorStrings.Current.ThemeFilter };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        try
        {
            var theme = ThemeJson.Import(File.ReadAllText(dialog.FileName));
            _session.EditSettings(() => _session.Document.Theme = theme);
        }
        catch (Exception e) when (e is IOException or FormatException)
        {
            MessageBox.Show(this, string.Format(Inv, EditorStrings.Current.OpenFailedFormat, dialog.FileName, e.Message), Application.ProductName, MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void ExportPng(float scale)
    {
        using var dialog = new SaveFileDialog { Filter = EditorStrings.Current.PngFilter, DefaultExt = "png", FileName = _session.Document.Name + (scale > 1f ? "@2x" : string.Empty) + ".png" };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        if (TryRun(() => _surface.ExportPng(dialog.FileName, scale), dialog.FileName)) SetStatus(string.Format(Inv, EditorStrings.Current.ExportDoneFormat, dialog.FileName));
    }

    private void ExportTheme()
    {
        using var dialog = new SaveFileDialog { Filter = EditorStrings.Current.ThemeFilter, DefaultExt = "json", FileName = _session.Document.Name + ".theme.json" };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        if (TryRun(() => File.WriteAllText(dialog.FileName, ThemeJson.Export(_session.Document.Theme.Create())), dialog.FileName))
        {
            SetStatus(string.Format(Inv, EditorStrings.Current.ExportDoneFormat, dialog.FileName));
        }
    }

    private void ExportAssets()
    {
        using var dialog = new FolderBrowserDialog { Description = EditorStrings.Current.ChooseFolder, UseDescriptionForTitle = true };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        var missing = new List<string>();
        bool ok = TryRun(() =>
        {
            foreach (var asset in _session.Document.AssetPaths())
            {
                // Paths leaving the design folder ("../x.png") would be written outside the chosen folder.
                string source = Path.Combine(_session.AssetFolder, asset);
                if (!ProjectScaffolder.IsLocalAssetPath(asset) || !File.Exists(source)) { missing.Add(asset); continue; }
                string target = Path.Combine(dialog.SelectedPath, DesignAsset.Normalize(asset));
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                File.Copy(source, target, overwrite: true);
            }
        }, dialog.SelectedPath);
        if (missing.Count > 0) ShowMessages([string.Format(Inv, EditorStrings.Current.MissingAssetsFormat, string.Join(", ", missing))]);
        if (ok) SetStatus(string.Format(Inv, EditorStrings.Current.ExportDoneFormat, dialog.SelectedPath));
    }

    private void GenerateCode()
    {
        var doc = _session.Document;
        string folder = _session.FilePath is null ? Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments) : Path.GetDirectoryName(_session.FilePath)!;
        using var dialog = new SaveFileDialog { Filter = EditorStrings.Current.CSharpFilter, DefaultExt = "cs", InitialDirectory = folder, FileName = doc.Codegen.ClassName + ".Designer.cs" };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        var generator = new CSharpDesignGenerator(_session.Registry)
        {
            SourceName = _session.FilePath is null ? doc.Name + DesignDocument.FileExtension : Path.GetFileName(_session.FilePath),
        };
        if (!TryRun(() => File.WriteAllText(dialog.FileName, generator.Generate(doc), new System.Text.UTF8Encoding(false)), dialog.FileName)) return;
        ShowMessages(generator.Warnings);
        SetStatus(string.Format(Inv, EditorStrings.Current.CodeGeneratedFormat, dialog.FileName));
    }

    /// <summary>Runs a file operation; reports a failure in a message box instead of crashing.</summary>
    private bool TryRun(Action action, string target)
    {
        try
        {
            action();
            return true;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidOperationException or ArgumentException or NotSupportedException
            or System.Runtime.InteropServices.ExternalException)
        {
            MessageBox.Show(this, string.Format(Inv, EditorStrings.Current.SaveFailedFormat, target, e.Message), Application.ProductName, MessageBoxButtons.OK, MessageBoxIcon.Error);
            return false;
        }
    }

    private void NewProject()
    {
        var result = NewProjectDialog.Show(this, _settings, _session.Document);
        if (result is not { } r) return;
        try
        {
            var files = ProjectScaffolder.Create(_session.Document, r.Options);
            var missing = ProjectScaffolder.Write(files, r.Folder, _session.Document, _session.FilePath is null ? null : Path.GetDirectoryName(_session.FilePath));
            if (missing.Count > 0) ShowMessages([string.Format(Inv, EditorStrings.Current.MissingAssetsFormat, string.Join(", ", missing))]);
            SetStatus(string.Format(Inv, EditorStrings.Current.ProjectCreatedFormat, r.Folder));
            Process.Start(new ProcessStartInfo { FileName = r.Folder, UseShellExecute = true });
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException)
        {
            MessageBox.Show(this, e.Message, EditorStrings.Current.NewProjectTitle, MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void CopySelection()
    {
        if (_session.Selected is { } node && !ReferenceEquals(node, _session.Document.Root)) TryRun(() => Clipboard.SetText(_session.Copy(node)), "Clipboard");
    }

    private void Cut()
    {
        if (_session.Selected is not { } node || ReferenceEquals(node, _session.Document.Root)) return;
        if (TryRun(() => Clipboard.SetText(_session.Copy(node)), "Clipboard")) _session.Delete(node);
    }

    private void Paste()
    {
        string? text = null;
        TryRun(() => text = Clipboard.ContainsText() ? Clipboard.GetText() : null, "Clipboard");
        if (text is null || !_session.Paste(text)) SetStatus(EditorStrings.Current.UnsupportedPaste);
    }

    private void Duplicate()
    {
        if (_session.Selected is not { } node || ReferenceEquals(node, _session.Document.Root)) return;
        var parent = _session.ParentOf(node);
        _session.Selected = parent;
        _session.Paste(_session.Copy(node));
    }

    private void DeleteSelection()
    {
        if (_session.Selected is { } node) _session.Delete(node);
    }

    private void SelectParent()
    {
        if (_session.Selected is { } node) _session.Selected = _session.ParentOf(node);
    }

    /// <summary>Zooms out when the design does not fit the visible area.</summary>
    private void FitIfNeeded()
    {
        float fit = _surface.FitZoom();
        if (fit < _surface.Zoom) SetZoom(fit);
    }

    private void SetZoom(float zoom)
    {
        _surface.Zoom = Math.Clamp(zoom, 0.25f, 4f);
        _statusZoom.Text = string.Format(Inv, EditorStrings.Current.ZoomFormat, (int)MathF.Round(_surface.Zoom * 100f));
    }

    private void TogglePreview()
    {
        _surface.PreviewMode = !_surface.PreviewMode;
        SetStatus(_surface.PreviewMode ? EditorStrings.Current.PreviewStatus : EditorStrings.Current.Ready);
        ApplyTexts();
    }

    private void ShowOptions()
    {
        if (!OptionsDialog.Show(this, _settings)) return;
        EditorStrings.Select(_settings.Language);
        _session.GridSize = _settings.GridSize;
        _surface.GridSize = _settings.GridSize;
        ApplyTexts();
        _surface.Rebuild();
    }

    private void OpenDocumentation()
    {
        string page = EditorStrings.Current.Language == "fr" ? "EDITOR_FR.md" : "EDITOR.md";
        string local = Path.Combine(AppContext.BaseDirectory, "docs", page);
        string target = File.Exists(local) ? local : "https://github.com/asm8086/PluginUI/blob/main/docs/" + page;
        Process.Start(new ProcessStartInfo { FileName = target, UseShellExecute = true });
    }
}
