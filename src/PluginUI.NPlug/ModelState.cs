// 2026/11/05 22:59:00 UTC - PluginUI
// SPDX-FileCopyrightText: 2026 asm8086
// SPDX-License-Identifier: MIT

using System.Buffers.Binary;
using NPlug;
using NPlug.IO;

namespace PluginUI.NPlug;

/// <summary>
/// Loads the parameter values of a model from a state written by <see cref="AudioProcessorModel.Save"/>, tolerating
/// states written by another version of the plug-in.
/// </summary>
/// <remarks>
/// <para>NPlug's own <see cref="AudioProcessorModel.Load"/> reads exactly one value per parameter and fails on a
/// shorter state: once a version appends parameters, every project and preset saved by the previous versions is
/// refused as a whole (the host shows the plug-in at its current values). Here a shorter state sets the parameters it
/// contains, the parameters appended since take their default value, and the caller learns how many values were
/// stored, to upgrade the meaning of older settings if needed. A longer state (a newer version) sets the known
/// parameters and its extra values are ignored.</para>
/// <para>Parameters must only ever be appended (NPlug gives identifiers in declaration order). The state must hold
/// the model values only (the default <c>SaveState</c> of the processor): data written after them by a derived class
/// cannot be told apart from parameter values when the count changed.</para>
/// </remarks>
public static class ModelState
{
    /// <summary>Loads the parameter values (UI thread; allocates).</summary>
    /// <param name="model">The model to set.</param>
    /// <param name="reader">The state, positioned at the model values.</param>
    /// <returns>
    /// The number of parameter values found in the state (at most <see cref="AudioProcessorModel.ParameterCount"/>);
    /// 0 for an empty state, which leaves the model unchanged.
    /// </returns>
    public static int Load(AudioProcessorModel model, PortableBinaryReader reader)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(reader);
        int count = model.ParameterCount;
        var bytes = new byte[count * sizeof(double)];
        var stream = reader.Stream;
        int read = 0;
        while (read < bytes.Length)
        {
            int n = stream.Read(bytes, read, bytes.Length - read);
            if (n <= 0) break;
            read += n;
        }
        int stored = read / sizeof(double);
        if (stored == 0) return 0;
        for (int i = stored; i < count; i++)
            BinaryPrimitives.WriteDoubleLittleEndian(bytes.AsSpan(i * sizeof(double)), model.GetParameterByIndex(i).DefaultNormalizedValue);
        for (int i = 0; i < stored; i++)
        {
            // A corrupted value must not reach the DSP.
            double value = BinaryPrimitives.ReadDoubleLittleEndian(bytes.AsSpan(i * sizeof(double)));
            if (!double.IsFinite(value) || value < 0.0 || value > 1.0)
                BinaryPrimitives.WriteDoubleLittleEndian(bytes.AsSpan(i * sizeof(double)), double.IsFinite(value) ? Math.Clamp(value, 0.0, 1.0) : model.GetParameterByIndex(i).DefaultNormalizedValue);
        }
        using var complete = new MemoryStream(bytes, writable: false);
        model.Load(new PortableBinaryReader(complete, false), AudioProcessorModelStorageMode.Default);
        return stored;
    }
}
