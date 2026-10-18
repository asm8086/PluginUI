// 2026/10/18 16:20:00 UTC - PluginUI
// SPDX-FileCopyrightText: 2026 asm8086
// SPDX-License-Identifier: MIT

namespace PluginUI;

/// <summary>
/// Easing curves: map linear progress t ∈ [0, 1] to eased progress.
/// </summary>
public static class Easing
{
    /// <summary>No easing.</summary>
    public static float Linear(float t) => t;

    /// <summary>Quadratic acceleration.</summary>
    public static float QuadIn(float t) => t * t;

    /// <summary>Quadratic deceleration.</summary>
    public static float QuadOut(float t) => t * (2f - t);

    /// <summary>Quadratic acceleration then deceleration.</summary>
    public static float QuadInOut(float t) => t < 0.5f ? 2f * t * t : -1f + (4f - 2f * t) * t;

    /// <summary>Cubic acceleration.</summary>
    public static float CubicIn(float t) => t * t * t;

    /// <summary>Cubic deceleration.</summary>
    public static float CubicOut(float t) { float u = t - 1f; return u * u * u + 1f; }

    /// <summary>Cubic acceleration then deceleration.</summary>
    public static float CubicInOut(float t) => t < 0.5f ? 4f * t * t * t : (t - 1f) * (2f * t - 2f) * (2f * t - 2f) + 1f;

    /// <summary>Slight overshoot at the end.</summary>
    public static float BackOut(float t) { const float s = 1.70158f; float u = t - 1f; return u * u * ((s + 1f) * u + s) + 1f; }

    /// <summary>Bounce at the end.</summary>
    public static float BounceOut(float t)
    {
        const float n = 7.5625f, d = 2.75f;
        if (t < 1f / d) return n * t * t;
        if (t < 2f / d) { t -= 1.5f / d; return n * t * t + 0.75f; }
        if (t < 2.5f / d) { t -= 2.25f / d; return n * t * t + 0.9375f; }
        t -= 2.625f / d;
        return n * t * t + 0.984375f;
    }

    /// <summary>Elastic spring at the end.</summary>
    public static float ElasticOut(float t)
    {
        if (t <= 0f || t >= 1f) return t;
        return MathF.Pow(2f, -10f * t) * MathF.Sin((t * 10f - 0.75f) * (2f * MathF.PI / 3f)) + 1f;
    }
}

/// <summary>
/// Receives a callback on every animation frame while registered with <see cref="UIHost.AddTicker"/>.
/// </summary>
public interface ITickable
{
    /// <summary>Called on each frame. Return <c>false</c> to unregister.</summary>
    /// <param name="elapsedSeconds">Time since the previous frame.</param>
    bool Tick(double elapsedSeconds);
}

/// <summary>
/// A running animation; <see cref="Cancel"/> stops it without calling the completion callback.
/// </summary>
public sealed class Animation
{
    private readonly Action<float> _apply;
    private readonly Func<float, float> _easing;
    private readonly Action? _completed;
    private double _elapsed;

    internal Animation(double durationSeconds, Action<float> apply, Func<float, float>? easing, Action? completed, int repeatCount, bool autoReverse)
    {
        Duration = Math.Max(1e-3, durationSeconds);
        _apply = apply;
        _easing = easing ?? Easing.CubicOut;
        _completed = completed;
        RepeatCount = repeatCount;
        AutoReverse = autoReverse;
    }

    /// <summary>Duration of one cycle in seconds.</summary>
    public double Duration { get; }

    /// <summary>Additional cycles (−1 = forever).</summary>
    public int RepeatCount { get; private set; }

    /// <summary>Plays backwards on every other cycle.</summary>
    public bool AutoReverse { get; }

    /// <summary><c>true</c> once finished or cancelled.</summary>
    public bool IsFinished { get; private set; }

    private bool _reversed;

    /// <summary>Stops the animation (no completion callback).</summary>
    public void Cancel() => IsFinished = true;

    internal void Step(double dt)
    {
        if (IsFinished) return;
        _elapsed += dt;
        float t = (float)Math.Clamp(_elapsed / Duration, 0.0, 1.0);
        float eased = _easing(_reversed ? 1f - t : t);
        _apply(eased);
        if (t < 1f) return;

        if (RepeatCount != 0)
        {
            if (RepeatCount > 0) RepeatCount--;
            _elapsed = 0.0;
            if (AutoReverse) _reversed = !_reversed;
            return;
        }
        IsFinished = true;
        _completed?.Invoke();
    }
}

/// <summary>
/// Drives animations from the host frame timer. Obtain it through <see cref="UIHost.Animator"/>.
/// </summary>
public sealed class Animator
{
    private readonly List<Animation> _animations = new();
    private readonly UIHost _host;

    internal Animator(UIHost host) => _host = host;

    /// <summary><c>true</c> if at least one animation is running.</summary>
    public bool IsRunning => _animations.Count > 0;

    /// <summary>
    /// Starts an animation calling <paramref name="apply"/> with the eased progress (0 → 1) on each frame.
    /// </summary>
    public Animation Start(double durationSeconds, Action<float> apply, Func<float, float>? easing = null, Action? completed = null, int repeatCount = 0, bool autoReverse = false)
    {
        var animation = new Animation(durationSeconds, apply, easing, completed, repeatCount, autoReverse);
        _animations.Add(animation);
        _host.EnsureFrameTimer();
        return animation;
    }

    /// <summary>Animates a float from <paramref name="from"/> to <paramref name="to"/>.</summary>
    public Animation Animate(float from, float to, double durationSeconds, Action<float> setter, Func<float, float>? easing = null, Action? completed = null)
        => Start(durationSeconds, t => setter(from + (to - from) * t), easing, completed);

    /// <summary>Animates a color.</summary>
    public Animation Animate(Color from, Color to, double durationSeconds, Action<Color> setter, Func<float, float>? easing = null, Action? completed = null)
        => Start(durationSeconds, t => setter(Color.Lerp(from, to, t)), easing, completed);

    /// <summary>Fades an element's opacity.</summary>
    public Animation Fade(UIElement element, float to, double durationSeconds = 0.2, Action? completed = null)
        => Animate(element.Opacity, to, durationSeconds, v => element.Opacity = v, Easing.QuadOut, completed);

    /// <summary>Cancels every running animation.</summary>
    public void CancelAll()
    {
        foreach (var a in _animations) a.Cancel();
        _animations.Clear();
    }

    internal bool Step(double dt)
    {
        if (_animations.Count == 0) return false;
        var snapshot = _animations.ToArray();
        foreach (var animation in snapshot) animation.Step(dt);
        _animations.RemoveAll(a => a.IsFinished);
        return true;
    }
}
