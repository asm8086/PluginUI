// 2026/10/18 17:31:00 UTC - PluginUI
// SPDX-FileCopyrightText: 2026 asm8086
// SPDX-License-Identifier: MIT

using System.Globalization;
using System.Runtime.Versioning;
using NPlug;

namespace PluginUI.NPlug;

/// <summary>
/// <see cref="IParameter"/> view of an NPlug <see cref="AudioParameter"/>: bind any PluginUI control to a VST3
/// parameter. Obtain instances from <see cref="NPlugParameterSet"/> (one per editor), which raises
/// <see cref="Changed"/> for host automation, preset loads and edits.
/// </summary>
[SupportedOSPlatform("windows10.0.17763")]
public sealed class NPlugParameter : IParameter
{
    private readonly IParameterEditSink _sink;
    private int _localEditDepth;

    internal NPlugParameter(AudioParameter parameter, IParameterEditSink sink)
    {
        Parameter = parameter;
        _sink = sink;
    }

    /// <summary>Wrapped NPlug parameter.</summary>
    public AudioParameter Parameter { get; }

    /// <summary>VST3 parameter id.</summary>
    public AudioParameterId Id => Parameter.Id;

    /// <inheritdoc />
    public string Name => Parameter.Title;

    /// <inheritdoc />
    public string Units => Parameter.Units ?? string.Empty;

    /// <inheritdoc />
    public int StepCount => Parameter.StepCount;

    /// <inheritdoc />
    public double DefaultNormalizedValue => Parameter.DefaultNormalizedValue;

    /// <inheritdoc />
    public double NormalizedValue => Parameter.NormalizedValue;

    /// <inheritdoc />
    public bool IsEditing => _localEditDepth > 0 || _sink.IsEditing(Parameter);

    /// <inheritdoc />
    public event Action<IParameter>? Changed;

    internal void RaiseChanged() => Changed?.Invoke(this);

    /// <summary>Closes a gesture left open (editor closed in the middle of an edit).</summary>
    internal void ForceEndEdit()
    {
        if (_localEditDepth == 0) return;
        _localEditDepth = 0;
        _sink.EndEdit(Parameter);
    }

    /// <inheritdoc />
    public string ToDisplayString(double normalized) => Parameter.ToString(Math.Clamp(normalized, 0.0, 1.0));

    /// <inheritdoc />
    public bool TryParse(string text, out double normalized)
    {
        normalized = 0.0;
        if (string.IsNullOrWhiteSpace(text)) return false;
        var s = text.Trim();
        if (!string.IsNullOrEmpty(Parameter.Units) && s.EndsWith(Parameter.Units, StringComparison.OrdinalIgnoreCase))
        {
            s = s[..^Parameter.Units.Length].Trim();
        }
        try
        {
            // Discrete parameters: accept the display text of a state.
            if (StepCount > 0)
            {
                for (int i = 0; i <= StepCount; i++)
                {
                    double n = (double)i / StepCount;
                    if (string.Equals(ToDisplayString(n), s, StringComparison.OrdinalIgnoreCase))
                    {
                        normalized = n;
                        return true;
                    }
                }
            }
            // Numbers always use the decimal point (InvariantNumber): a decimal comma is rejected.
            if (s.Contains(',')) return false;
            double value = Parameter.FromString(s);
            if (double.IsNaN(value)) return false;
            if (value == 0.0 && !double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out _) && StepCount != 1) return false;
            normalized = Math.Clamp(value, 0.0, 1.0);
            return true;
        }
        catch (FormatException)
        {
            return false;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    /// <inheritdoc />
    public double ToPlain(double normalized) => Parameter.ToPlain(normalized);

    /// <inheritdoc />
    public double ToNormalized(double plain) => Parameter.ToNormalized(plain);

    /// <inheritdoc />
    public void BeginEdit()
    {
        if (_localEditDepth++ == 0) _sink.BeginEdit(Parameter);
    }

    /// <inheritdoc />
    public void PerformEdit(double normalized) => _sink.PerformEdit(Parameter, Math.Clamp(normalized, 0.0, 1.0));

    /// <inheritdoc />
    public void EndEdit()
    {
        if (_localEditDepth == 0) return;
        if (--_localEditDepth == 0) _sink.EndEdit(Parameter);
    }

    /// <inheritdoc />
    public override string ToString() => $"{Name} = {this.ToDisplayStringWithUnits(NormalizedValue)}";
}

/// <summary>
/// The NPlug parameters of one editor as <see cref="IParameter"/>s. Listens to the model while the editor is
/// open and forwards value changes (automation, presets, edits) to the bound controls.
/// </summary>
[SupportedOSPlatform("windows10.0.17763")]
public sealed class NPlugParameterSet
{
    private readonly IParameterEditSink _sink;
    private readonly Dictionary<AudioParameter, NPlugParameter> _parameters = new(ReferenceEqualityComparer.Instance);
    private bool _listening;

    /// <summary>Creates the set for a controller.</summary>
    public NPlugParameterSet(IParameterEditSink sink)
    {
        ArgumentNullException.ThrowIfNull(sink);
        _sink = sink;
    }

    /// <summary>Edit sink (the controller).</summary>
    public IParameterEditSink Sink => _sink;

    /// <summary>Wrapper of <paramref name="parameter"/> (created on first use, then cached).</summary>
    public NPlugParameter this[AudioParameter parameter] => Get(parameter);

    /// <summary>Wrapper of <paramref name="parameter"/> (created on first use, then cached).</summary>
    public NPlugParameter Get(AudioParameter parameter)
    {
        ArgumentNullException.ThrowIfNull(parameter);
        if (!_parameters.TryGetValue(parameter, out var wrapper))
        {
            wrapper = new NPlugParameter(parameter, _sink);
            _parameters.Add(parameter, wrapper);
        }
        return wrapper;
    }

    /// <summary>
    /// Wrappers of the model's user-editable parameters, in declaration order: what an A/B comparison
    /// (<see cref="ABCompareBar.Parameters"/>), an undo point or a randomizer should capture. Read-only, hidden and
    /// program-change parameters are left out, as is the bypass unless <paramref name="includeBypass"/> is set.
    /// </summary>
    /// <param name="includeBypass">Includes the bypass parameter.</param>
    /// <returns>The wrappers.</returns>
    public IReadOnlyList<NPlugParameter> GetSnapshotParameters(bool includeBypass = false)
    {
        var model = _sink.Model;
        var list = new List<NPlugParameter>(model.ParameterCount);
        for (int i = 0; i < model.ParameterCount; i++)
        {
            var parameter = model.GetParameterByIndex(i);
            var flags = parameter.Flags;
            if ((flags & (AudioParameterFlags.IsReadOnly | AudioParameterFlags.IsHidden | AudioParameterFlags.IsProgramChange)) != 0) continue;
            if (!includeBypass && (flags & AudioParameterFlags.IsBypass) != 0) continue;
            list.Add(Get(parameter));
        }
        return list;
    }

    /// <summary>Wrapper of the parameter with this id, or null.</summary>
    public NPlugParameter? Find(AudioParameterId id)
    {
        foreach (var p in _parameters.Values)
        {
            if (p.Id == id) return p;
        }
        return null;
    }

    /// <summary>Starts forwarding model events (editor opened).</summary>
    public void StartListening()
    {
        if (_listening) return;
        _listening = true;
        _sink.Model.ParameterValueChanged += OnParameterValueChanged;
        _sink.Model.StateLoaded += OnStateLoaded;
        OnStateLoaded(); // values may have changed while the editor was closed
    }

    /// <summary>Stops forwarding model events (editor closed).</summary>
    public void StopListening()
    {
        // Never leave a DAW gesture open when the editor closes.
        foreach (var wrapper in _parameters.Values) wrapper.ForceEndEdit();
        if (!_listening) return;
        _listening = false;
        _sink.Model.ParameterValueChanged -= OnParameterValueChanged;
        _sink.Model.StateLoaded -= OnStateLoaded;
    }

    private void OnParameterValueChanged(AudioParameter parameter)
    {
        if (_parameters.TryGetValue(parameter, out var wrapper)) wrapper.RaiseChanged();
    }

    private void OnStateLoaded()
    {
        foreach (var wrapper in _parameters.Values) wrapper.RaiseChanged();
    }
}
