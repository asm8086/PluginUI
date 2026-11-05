// 2026/11/05 14:06:00 UTC - PluginUI
// SPDX-FileCopyrightText: 2026 asm8086
// SPDX-License-Identifier: MIT

namespace PluginUI.Desktop;

/// <summary>
/// Undo / redo history of a document, with merging of continuous edits (a knob drag is one step), a saved point for
/// the "modified" state, and ready-made <see cref="UndoCommand"/> (Ctrl+Z) and <see cref="RedoCommand"/> (Ctrl+Y,
/// Ctrl+Shift+Z) whose menu texts name the action.
/// </summary>
/// <example>
/// <code>
/// double old = model.Gain;
/// undo.Do("Gain", redo: () => model.Gain = 0.5, undo: () => model.Gain = old, mergeKey: "gain");
/// // ... at the end of the drag:
/// undo.EndMerge();
/// </code>
/// </example>
public sealed class UndoStack
{
    private readonly List<Entry> _undo = new();
    private readonly List<Entry> _redo = new();
    private long _nextId = 1;
    private long _savedId;
    private bool _mergeOpen;
    private UIStrings _strings = UIStrings.English;

    /// <summary>Creates an empty history.</summary>
    public UndoStack()
    {
        UndoCommand = new Command(_strings.FormatUndo(null), () => Undo(), KeyGesture.Parse("Ctrl+Z"))
        {
            CanExecuteWhen = () => CanUndo,
            AllowsRepeat = true,
        };
        RedoCommand = new Command(_strings.FormatRedo(null), () => Redo(), KeyGesture.Parse("Ctrl+Y"))
        {
            CanExecuteWhen = () => CanRedo,
            AllowsRepeat = true,
        };
        RedoCommand.AlternateGestures.Add(KeyGesture.Parse("Ctrl+Shift+Z"));
    }

    /// <summary>Maximum number of steps kept (oldest dropped first). Default 200.</summary>
    public int Limit { get; set; } = 200;

    /// <summary>Texts of <see cref="UndoCommand"/> and <see cref="RedoCommand"/> ("Undo Gain" / "Annuler Gain").</summary>
    public UIStrings Strings
    {
        get => _strings;
        set
        {
            _strings = value ?? UIStrings.English;
            UpdateCommands();
        }
    }

    /// <summary>Undo command (Ctrl+Z), enabled when <see cref="CanUndo"/>.</summary>
    public Command UndoCommand { get; }

    /// <summary>Redo command (Ctrl+Y and Ctrl+Shift+Z), enabled when <see cref="CanRedo"/>.</summary>
    public Command RedoCommand { get; }

    /// <summary><c>true</c> when a step can be undone.</summary>
    public bool CanUndo => _undo.Count > 0;

    /// <summary><c>true</c> when a step can be redone.</summary>
    public bool CanRedo => _redo.Count > 0;

    /// <summary>Name of the step <see cref="Undo"/> would revert, or null.</summary>
    public string? UndoName => _undo.Count > 0 ? _undo[^1].Name : null;

    /// <summary>Name of the step <see cref="Redo"/> would apply again, or null.</summary>
    public string? RedoName => _redo.Count > 0 ? _redo[^1].Name : null;

    /// <summary>Number of steps that can be undone.</summary>
    public int UndoCount => _undo.Count;

    /// <summary><c>true</c> while an undo or redo action runs: model change handlers must not record a new step then.</summary>
    public bool IsApplying { get; private set; }

    /// <summary><c>true</c> when the document differs from its last <see cref="MarkSaved"/> point.</summary>
    public bool IsDirty => TopId != _savedId;

    /// <summary>Raised after every change of the history (step added, undone, redone, cleared, saved point).</summary>
    public event Action<UndoStack>? Changed;

    private long TopId => _undo.Count > 0 ? _undo[^1].Id : 0;

    /// <summary>Runs <paramref name="redo"/> now and records the step.</summary>
    /// <param name="name">Step name shown in the Edit menu ("Gain", "Delete band").</param>
    /// <param name="redo">Applies the change.</param>
    /// <param name="undo">Reverts the change.</param>
    /// <param name="mergeKey">Consecutive steps with the same key merge into one until <see cref="EndMerge"/>.</param>
    public void Do(string name, Action redo, Action undo, object? mergeKey = null)
    {
        ArgumentNullException.ThrowIfNull(redo);
        ArgumentNullException.ThrowIfNull(undo);
        IsApplying = true;
        try
        {
            redo();
        }
        finally
        {
            IsApplying = false;
        }
        Push(name, redo, undo, mergeKey);
    }

    /// <summary>Records a step whose change was already applied.</summary>
    /// <param name="name">Step name.</param>
    /// <param name="redo">Applies the change again.</param>
    /// <param name="undo">Reverts the change.</param>
    /// <param name="mergeKey">Consecutive steps with the same key merge into one until <see cref="EndMerge"/>.</param>
    public void Push(string name, Action redo, Action undo, object? mergeKey = null)
    {
        ArgumentNullException.ThrowIfNull(redo);
        ArgumentNullException.ThrowIfNull(undo);
        if (IsApplying) return;
        _redo.Clear();
        if (mergeKey is not null && _mergeOpen && _undo.Count > 0 && Equals(_undo[^1].MergeKey, mergeKey))
        {
            // Keep the first undo (state before the gesture), take the latest redo; a new id marks the change.
            var top = _undo[^1];
            _undo[^1] = top with { Name = name, Redo = redo, Id = _nextId++ };
        }
        else
        {
            _undo.Add(new Entry(name, redo, undo, mergeKey, _nextId++));
            if (Limit > 0 && _undo.Count > Limit) _undo.RemoveRange(0, _undo.Count - Limit);
        }
        _mergeOpen = mergeKey is not null;
        OnChanged();
    }

    /// <summary>
    /// Records the user edits of a parameter: one step per gesture (a knob drag, a node drag, a wheel burst), named
    /// after the parameter. Changes made by undo / redo or while <see cref="IsApplying"/> are not recorded; set the
    /// value with <c>NormalizedValue</c> from your code inside <see cref="Do"/> when it must be undoable.
    /// </summary>
    /// <param name="parameter">Parameter to watch.</param>
    /// <param name="name">Step name (default: the parameter name).</param>
    /// <returns>Dispose it to stop recording.</returns>
    public IDisposable Track(LocalParameter parameter, string? name = null)
    {
        ArgumentNullException.ThrowIfNull(parameter);
        return new ParameterTracker(this, parameter, name ?? parameter.Name);
    }

    /// <summary>Ends the current merge: the next step starts a new entry even with the same key.</summary>
    public void EndMerge() => _mergeOpen = false;

    /// <summary>Reverts the last step. Returns <c>false</c> if there is none.</summary>
    public bool Undo()
    {
        if (_undo.Count == 0) return false;
        var entry = _undo[^1];
        _undo.RemoveAt(_undo.Count - 1);
        Apply(entry.Undo);
        _redo.Add(entry);
        _mergeOpen = false;
        OnChanged();
        return true;
    }

    /// <summary>Applies the last undone step again. Returns <c>false</c> if there is none.</summary>
    public bool Redo()
    {
        if (_redo.Count == 0) return false;
        var entry = _redo[^1];
        _redo.RemoveAt(_redo.Count - 1);
        Apply(entry.Redo);
        _undo.Add(entry);
        _mergeOpen = false;
        OnChanged();
        return true;
    }

    /// <summary>Forgets every step (after New or Open) and marks the document as saved.</summary>
    public void Clear()
    {
        _undo.Clear();
        _redo.Clear();
        _mergeOpen = false;
        _savedId = 0;
        OnChanged();
    }

    /// <summary>Marks the current state as saved (<see cref="IsDirty"/> becomes <c>false</c>).</summary>
    public void MarkSaved()
    {
        _savedId = TopId;
        _mergeOpen = false;
        OnChanged();
    }

    private void Apply(Action action)
    {
        IsApplying = true;
        try
        {
            action();
        }
        finally
        {
            IsApplying = false;
        }
    }

    private void OnChanged()
    {
        UpdateCommands();
        Changed?.Invoke(this);
    }

    private void UpdateCommands()
    {
        UndoCommand.Text = _strings.FormatUndo(UndoName);
        RedoCommand.Text = _strings.FormatRedo(RedoName);
    }

    private sealed record Entry(string Name, Action Redo, Action Undo, object? MergeKey, long Id);

    private sealed class ParameterTracker : IDisposable
    {
        private readonly UndoStack _stack;
        private readonly LocalParameter _parameter;
        private readonly string _name;
        private double _last;
        private double _gestureStart;
        private bool _disposed;

        public ParameterTracker(UndoStack stack, LocalParameter parameter, string name)
        {
            _stack = stack;
            _parameter = parameter;
            _name = name;
            _last = parameter.NormalizedValue;
            parameter.EditStarted += OnEditStarted;
            parameter.EditEnded += OnEditEnded;
            parameter.Changed += OnChanged;
        }

        private void OnEditStarted(LocalParameter p)
        {
            // Nested gestures (BeginEdit twice) keep the outermost start.
            if (p.IsEditing && _gestureStartDepth++ == 0) _gestureStart = p.NormalizedValue;
        }

        private int _gestureStartDepth;

        private void OnEditEnded(LocalParameter p)
        {
            if (_gestureStartDepth == 0 || --_gestureStartDepth > 0) return;
            Record(_gestureStart, p.NormalizedValue);
        }

        private void OnChanged(IParameter p)
        {
            double value = p.NormalizedValue;
            // Outside a gesture (programmatic or one-shot edits): one step per change.
            if (!_parameter.IsEditing && _gestureStartDepth == 0 && !_stack.IsApplying) Record(_last, value);
            _last = value;
        }

        private void Record(double before, double after)
        {
            _last = after;
            if (_stack.IsApplying || before == after) return;
            var parameter = _parameter;
            _stack.Push(_name, () => parameter.NormalizedValue = after, () => parameter.NormalizedValue = before);
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _parameter.EditStarted -= OnEditStarted;
            _parameter.EditEnded -= OnEditEnded;
            _parameter.Changed -= OnChanged;
        }
    }
}
