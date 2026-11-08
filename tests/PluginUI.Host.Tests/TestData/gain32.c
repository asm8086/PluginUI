/* 2026/11/08 18:01:00 UTC - PluginUI */
/* SPDX-FileCopyrightText: 2026 asm8086 */
/* SPDX-License-Identifier: MIT */
/*
 * Gain32.dll: a minimal 32-bit VST 2 effect for the tests of the 32-bit bridge (gain, chunk state, MIDI echo an octave
 * up, latency 16, an editor of 200 x 100 that asks for 240 x 120 at its first idle time). No C runtime and no Steinberg
 * header: the interface is declared below from its public description. Built with clang and lld (any platform), with
 * an import library of user32.dll (CreateWindowExA@48, DestroyWindow@4) made by llvm-dlltool -k -m i386 from a .def file:
 *   clang --target=i686-pc-windows-msvc -O2 -nostdlib -ffreestanding -fno-builtin -fuse-ld=lld -shared
 *         -Wl,/entry:_DllMainCRTStartup gain32.c user32.lib -o Gain32.dll
 */
typedef long long i64;
typedef int (__cdecl *HostCb)(void*, int, int, int, void*, float); /* 32-bit: intptr = int */
struct AEffect;
typedef int (__cdecl *Disp)(struct AEffect*, int, int, int, void*, float);
typedef void (__cdecl *Proc)(struct AEffect*, float**, float**, int);
typedef void (__cdecl *SetP)(struct AEffect*, int, float);
typedef float (__cdecl *GetP)(struct AEffect*, int);
struct AEffect { int magic; Disp dispatcher; Proc process; SetP setParameter; GetP getParameter;
  int numPrograms, numParams, numInputs, numOutputs, flags; int resvd1, resvd2; int initialDelay, realQualities, offQualities; float ioRatio;
  void* object; void* user; int uniqueID, version; Proc processReplacing; void* processDoubleReplacing; char future[56]; };
struct VstMidiEvent { int type, byteSize, deltaFrames, flags, noteLength, noteOffset; unsigned char midiData[4]; char detune, noteOffVelocity, r1, r2; };
struct VstEvents { int numEvents; int reserved; void* events[2]; };
struct ERect { short top, left, bottom, right; };
__declspec(dllimport) void* __stdcall CreateWindowExA(unsigned, const char*, const char*, unsigned, int, int, int, int, void*, void*, void*, void*);
__declspec(dllimport) int __stdcall DestroyWindow(void*);
int _fltused = 0;
void* __cdecl memset(void* d, int c, unsigned n) { unsigned char* p = d; while (n--) *p++ = (unsigned char)c; return d; }
void* __cdecl memcpy(void* d, const void* s, unsigned n) { unsigned char* p = d; const unsigned char* q = s; while (n--) *p++ = *q++; return d; }
static struct AEffect fx; static HostCb host; static float gain = 0.5f; static float chunk[1];
static int pending; static struct VstMidiEvent echo[16]; static struct VstMidiEvent recv[16];
static struct ERect rect = { 0, 0, 100, 200 }; static void* editor; static int resized;
static void copy(char* d, const char* s, int max) { int i = 0; for (; s[i] && i < max - 1; i++) d[i] = s[i]; d[i] = 0; }
static int eq(const char* a, const char* b) { while (*a && *a == *b) { a++; b++; } return *a == *b; }
static void fmt(char* d, float v) { /* value with one decimal */ int neg = v < 0; if (neg) v = -v; int t = (int)(v * 10 + 0.5f); char buf[16]; int n = 0;
  do { buf[n++] = (char)('0' + t % 10); t /= 10; if (n == 1) buf[n++] = '.'; } while (t > 0 || n < 3); int i = 0; if (neg) d[i++] = '-'; while (n) d[i++] = buf[--n]; d[i] = 0; }
static int __cdecl dispatch(struct AEffect* e, int op, int index, int value, void* ptr, float opt) {
  switch (op) {
    case 6: copy(ptr, "x", 8); return 0;                              /* effGetParamLabel */
    case 13: *(struct ERect**)ptr = &rect; return 1;                   /* effEditGetRect */
    case 14: editor = CreateWindowExA(0, "STATIC", "Gain32", 0x50000000 /* WS_CHILD | WS_VISIBLE */, 0, 0, rect.right, rect.bottom, ptr, 0, 0, 0);
             return editor != 0;                                       /* effEditOpen */
    case 15: if (editor) DestroyWindow(editor); editor = 0; return 0;  /* effEditClose */
    case 19: if (editor && !resized) { resized = 1; rect.right = 240; rect.bottom = 120; host(e, 15, 240, 120, 0, 0); } return 0; /* effEditIdle */
    case 7: fmt(ptr, gain * 2.0f); return 0;                           /* effGetParamDisplay */
    case 8: copy(ptr, "Gain", 8); return 0;                            /* effGetParamName */
    case 23: chunk[0] = gain; *(void**)ptr = chunk; return 4;           /* effGetChunk */
    case 24: if (value >= 4) gain = *(float*)ptr; return 1;            /* effSetChunk */
    case 25: { struct VstEvents* ev = ptr; for (int i = 0; i < ev->numEvents && pending < 16; i++) recv[pending++] = *(struct VstMidiEvent*)ev->events[i]; return 1; }
    case 35: return 1;                                                 /* effGetPlugCategory: effect */
    case 45: copy(ptr, "Gain32", 32); return 1;
    case 47: copy(ptr, "Bridge Tests", 64); return 1;
    case 48: copy(ptr, "Gain32", 64); return 1;
    case 49: return 32;
    case 51: return (eq(ptr, "receiveVstMidiEvent") || eq(ptr, "sendVstMidiEvent")) ? 1 : -1;
    case 58: return 2400;
    default: return 0;
  }
}
static void __cdecl setp(struct AEffect* e, int i, float v) { if (i == 0) gain = v; }
static float __cdecl getp(struct AEffect* e, int i) { return i == 0 ? gain : 0; }
static void __cdecl replacing(struct AEffect* e, float** in, float** out, int n) {
  float g = gain * 2.0f;
  for (int c = 0; c < 2; c++) for (int i = 0; i < n; i++) out[c][i] = in[c][i] * g;
  if (pending) {
    struct VstEvents list; list.numEvents = pending; list.reserved = 0;
    for (int i = 0; i < pending && i < 2; i++) { echo[i] = recv[i]; echo[i].midiData[1] = (unsigned char)(recv[i].midiData[1] + 12); list.events[i] = &echo[i]; }
    if (list.numEvents > 2) list.numEvents = 2;
    host(e, 8, 0, 0, &list, 0);                                         /* audioMasterProcessEvents */
    pending = 0;
  }
}
__declspec(dllexport) struct AEffect* __cdecl VSTPluginMain(HostCb cb) {
  host = cb; fx.magic = ('V' << 24) | ('s' << 16) | ('t' << 8) | 'P'; fx.dispatcher = dispatch; fx.setParameter = setp; fx.getParameter = getp;
  fx.numPrograms = 1; fx.numParams = 1; fx.numInputs = 2; fx.numOutputs = 2; fx.flags = 1 | 16 | 32; /* hasEditor | canReplacing | programChunks */
  fx.initialDelay = 16; fx.uniqueID = ('G' << 24) | ('n' << 16) | ('3' << 8) | '2'; fx.version = 1; fx.processReplacing = replacing; fx.ioRatio = 1.0f;
  return &fx;
}
int __stdcall _DllMainCRTStartup(void* h, unsigned r, void* p) { return 1; }
