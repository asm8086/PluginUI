// 2026/11/05 22:59:00 UTC - PluginUI
// SPDX-FileCopyrightText: 2026 asm8086
// SPDX-License-Identifier: MIT

using NPlug;
using NPlug.IO;
using PluginUI.NPlug;

namespace PluginUI.NPlug.Tests;

/// <summary>
/// <see cref="ModelState"/>: states of an older version (fewer parameters), of a newer one (more), corrupted values,
/// empty state.
/// </summary>
public class ModelStateTests
{
    private sealed class V1 : AudioProcessorModel
    {
        public V1() : base("Test")
        {
            Gain = AddParameter(new AudioRangeParameter("Gain", minValue: -24.0, maxValue: 24.0, defaultPlainValue: 0.0));
            Mode = AddParameter(new AudioStringListParameter("Mode", ["A", "B", "C"], selectedItem: 0));
            Initialize();
        }

        public AudioRangeParameter Gain { get; }
        public AudioStringListParameter Mode { get; }
    }

    private sealed class V2 : AudioProcessorModel
    {
        public V2() : base("Test")
        {
            Gain = AddParameter(new AudioRangeParameter("Gain", minValue: -24.0, maxValue: 24.0, defaultPlainValue: 0.0));
            Mode = AddParameter(new AudioStringListParameter("Mode", ["A", "B", "C"], selectedItem: 0));
            Added = AddParameter(new AudioRangeParameter("Added", minValue: 0.0, maxValue: 10.0, defaultPlainValue: 7.0));
            Initialize();
        }

        public AudioRangeParameter Gain { get; }
        public AudioStringListParameter Mode { get; }
        public AudioRangeParameter Added { get; }
    }

    private static MemoryStream Save(AudioProcessorModel model)
    {
        var stream = new MemoryStream();
        model.Save(new PortableBinaryWriter(stream, false), AudioProcessorModelStorageMode.Default);
        stream.Position = 0;
        return stream;
    }

    [Test]
    public void AnOlderStateSetsItsParametersAndLeavesTheNewOnesAtTheirDefault()
    {
        var old = new V1();
        old.Gain.Value = 6.0;
        old.Mode.SelectedItem = 2;
        var current = new V2();
        current.Added.Value = 3.0;   // not the default: the load must reset it
        int stored = ModelState.Load(current, new PortableBinaryReader(Save(old), false));
        Assert.Multiple(() =>
        {
            Assert.That(stored, Is.EqualTo(2));
            Assert.That(current.Gain.Value, Is.EqualTo(6.0).Within(1e-12));
            Assert.That(current.Mode.SelectedItem, Is.EqualTo(2));
            Assert.That(current.Added.Value, Is.EqualTo(7.0).Within(1e-12));
        });
    }

    [Test]
    public void NPlugAloneRefusesTheOlderState()
    {
        // The reason for ModelState: the stock loader reads one value per parameter.
        var current = new V2();
        Assert.That(() => current.Load(new PortableBinaryReader(Save(new V1()), false), AudioProcessorModelStorageMode.Default),
            Throws.InstanceOf<EndOfStreamException>());
    }

    [Test]
    public void ANewerStateSetsTheKnownParameters()
    {
        var newer = new V2();
        newer.Gain.Value = -12.0;
        newer.Mode.SelectedItem = 1;
        var current = new V1();
        Assert.That(ModelState.Load(current, new PortableBinaryReader(Save(newer), false)), Is.EqualTo(2));
        Assert.That((current.Gain.Value, current.Mode.SelectedItem), Is.EqualTo((-12.0, 1)));
    }

    [Test]
    public void CorruptedValuesAreRepairedAndAnEmptyStateChangesNothing()
    {
        var bytes = new byte[3 * sizeof(double)];
        BitConverter.TryWriteBytes(bytes.AsSpan(0), double.NaN);
        BitConverter.TryWriteBytes(bytes.AsSpan(8), 7.5);
        BitConverter.TryWriteBytes(bytes.AsSpan(16), -1.0);
        var current = new V2();
        current.Gain.Value = 5.0;
        Assert.That(ModelState.Load(current, new PortableBinaryReader(new MemoryStream(bytes), false)), Is.EqualTo(3));
        Assert.Multiple(() =>
        {
            Assert.That(current.Gain.Value, Is.EqualTo(0.0).Within(1e-12), "NaN → default");
            Assert.That(current.Mode.NormalizedValue, Is.EqualTo(1.0), "clamped");
            Assert.That(current.Added.NormalizedValue, Is.EqualTo(0.0), "clamped");
        });
        current.Gain.Value = 5.0;
        Assert.That(ModelState.Load(current, new PortableBinaryReader(new MemoryStream(), false)), Is.Zero);
        Assert.That(current.Gain.Value, Is.EqualTo(5.0).Within(1e-12));
    }
}
