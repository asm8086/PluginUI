---
# 2026/10/18 15:48:00 UTC - PluginUI
uid: PluginUI.Dsp
summary: *content
---
Signal processing used by the meters and analyzers, usable from a plug-in processor: FFT (<xref:PluginUI.Dsp.Fft>),
lock-free audio → UI transfer (<xref:PluginUI.Dsp.AudioRingBuffer>, <xref:PluginUI.Dsp.PeakValue>), meter ballistics,
EBU R128 loudness (<xref:PluginUI.Dsp.LoudnessAnalyzer>), biquad design, stereo analysis, WAV reading and waveform
peaks. The real-time paths allocate nothing and use SIMD (AVX2 / AVX-512 / SSE) when available.
