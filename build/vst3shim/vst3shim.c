// 2026/10/18 16:20:00 UTC - PluginUI
// SPDX-FileCopyrightText: 2026 asm8086
// SPDX-License-Identifier: MIT

// VST3 loader for x64 plug-ins shipped in two NativeAOT builds:
//
//   Contents/x86_64-win/<Name>.vst3              this shim (renamed), the module the host loads
//   Contents/x86_64-win/<Name>.x86-64-v3.dll     build for AVX2 / FMA / BMI processors (Haswell, Zen and later)
//   Contents/x86_64-win/<Name>.x86-64-v2.dll     build for any x86-64-v2 processor (SSE4.2, POPCNT)
//
// On the first call, the shim checks the processor (CPUID and the AVX state enabled by the OS), loads the best build
// present next to it and forwards GetPluginFactory, InitDll and ExitDll to it. The environment variable
// PLUGINUI_X64_LEVEL=v2 forces the x86-64-v2 build (troubleshooting, tests); v3 is honored on a v3 processor only.
//
// No CRT and no Windows SDK headers: the few kernel32 functions used are declared below, so the same file builds with
// MSVC (cl /O2 /LD vst3shim.c kernel32.lib) and with clang + lld-link (see build/Build-Release.ps1).

#if defined(__clang__) || defined(__GNUC__)
// clang / gcc: inline assembly (their intrin.h needs the C runtime headers).
static void CpuId(int r[4], int leaf, int subleaf)
{
    __asm__ volatile("cpuid" : "=a"(r[0]), "=b"(r[1]), "=c"(r[2]), "=d"(r[3]) : "a"(leaf), "c"(subleaf));
}
static unsigned long long XGetBv0(void)
{
    unsigned int low, high;
    __asm__ volatile("xgetbv" : "=a"(low), "=d"(high) : "c"(0));
    return ((unsigned long long)high << 32) | low;
}
#define CompareExchange(target, value, comparand) __sync_val_compare_and_swap((target), (comparand), (value))
#define AtomicStore(target, value) __atomic_store_n((target), (value), __ATOMIC_SEQ_CST)
#else
#include <intrin.h>
static void CpuId(int r[4], int leaf, int subleaf) { __cpuidex(r, leaf, subleaf); }
static unsigned long long XGetBv0(void) { return _xgetbv(0); }
#define CompareExchange(target, value, comparand) _InterlockedCompareExchange((target), (value), (comparand))
#define AtomicStore(target, value) _InterlockedExchange((target), (value))
#endif

typedef int BOOL;
typedef unsigned long DWORD;
typedef void* HMODULE;
typedef void* HANDLE;
typedef unsigned short WCHAR; // wchar_t on Windows (L"" literals)
typedef long long (*FARPROC)(void);

#define KERNEL32 __declspec(dllimport)
KERNEL32 BOOL GetModuleHandleExW(DWORD flags, const WCHAR* name, HMODULE* module);
KERNEL32 DWORD GetModuleFileNameW(HMODULE module, WCHAR* buffer, DWORD size);
KERNEL32 HMODULE LoadLibraryExW(const WCHAR* path, HANDLE reserved, DWORD flags);
KERNEL32 FARPROC GetProcAddress(HMODULE module, const char* name);
KERNEL32 DWORD GetEnvironmentVariableW(const WCHAR* name, WCHAR* buffer, DWORD size);
KERNEL32 void OutputDebugStringW(const WCHAR* text);
KERNEL32 void Sleep(DWORD milliseconds);

#define GET_MODULE_HANDLE_EX_FLAG_UNCHANGED_REFCOUNT 0x2
#define GET_MODULE_HANDLE_EX_FLAG_FROM_ADDRESS 0x4
#define LOAD_WITH_ALTERED_SEARCH_PATH 0x8
#define PATH_CAPACITY 32768

#ifdef PLUGINUI_SHIM_NO_CRT
// Built without the C runtime (/NODEFAULTLIB): the compiler may still turn copy loops into these calls.
void* memcpy(void* destination, const void* source, unsigned long long count)
{
    unsigned char* d = (unsigned char*)destination;
    const unsigned char* s = (const unsigned char*)source;
    while (count-- != 0) *d++ = *s++;
    return destination;
}

void* memset(void* destination, int value, unsigned long long count)
{
    unsigned char* d = (unsigned char*)destination;
    while (count-- != 0) *d++ = (unsigned char)value;
    return destination;
}
#endif

typedef void* (*GetPluginFactoryProc)(void);
typedef BOOL (*ModuleProc)(void);

static WCHAR g_path[PATH_CAPACITY];
static HMODULE g_impl;
static volatile long g_state; // 0: not loaded, 1: loading, 2: done (g_impl may be null)

// x86-64-v3: AVX, AVX2, BMI1, BMI2, F16C, FMA, LZCNT, MOVBE, with the YMM state enabled by the OS (XCR0 bits 1-2).
static int SupportsX64V3(void)
{
    int r[4];
    CpuId(r, 0, 0);
    if (r[0] < 7) return 0;
    CpuId(r, 1, 0);
    const int ecx1 = r[2];
    const int need1 = (1 << 12) | (1 << 22) | (1 << 27) | (1 << 28) | (1 << 29); // FMA, MOVBE, OSXSAVE, AVX, F16C
    if ((ecx1 & need1) != need1) return 0;
    if ((XGetBv0() & 6) != 6) return 0;
    CpuId(r, 7, 0);
    const int need7 = (1 << 3) | (1 << 5) | (1 << 8); // BMI1, AVX2, BMI2
    if ((r[1] & need7) != need7) return 0;
    CpuId(r, (int)0x80000000, 0);
    if ((unsigned)r[0] < 0x80000001u) return 0;
    CpuId(r, (int)0x80000001, 0);
    return (r[2] & (1 << 5)) != 0; // LZCNT
}

static int Length(const WCHAR* s)
{
    int n = 0;
    while (s[n] != 0) n++;
    return n;
}

static void Log(const WCHAR* message, const WCHAR* detail)
{
    static WCHAR line[1024];
    const WCHAR* prefix = L"[PluginUI VST3 loader] ";
    int n = 0;
    for (const WCHAR* p = prefix; *p != 0 && n < 1000; p++) line[n++] = *p;
    for (const WCHAR* p = message; *p != 0 && n < 1000; p++) line[n++] = *p;
    if (detail != 0) for (const WCHAR* p = detail; *p != 0 && n < 1020; p++) line[n++] = *p;
    line[n++] = L'\n';
    line[n] = 0;
    OutputDebugStringW(line);
}

// Builds "<folder>\<module name without extension>.x86-64-v<level>.dll" in g_path; returns 0 when too long.
static int BuildPath(const WCHAR* modulePath, int level)
{
    int length = Length(modulePath);
    int dot = length;
    for (int i = length - 1; i >= 0 && modulePath[i] != L'\\' && modulePath[i] != L'/'; i--)
    {
        if (modulePath[i] == L'.') { dot = i; break; }
    }
    const WCHAR* suffix = level == 3 ? L".x86-64-v3.dll" : L".x86-64-v2.dll";
    int suffixLength = Length(suffix);
    if (dot + suffixLength + 1 > PATH_CAPACITY) return 0;
    for (int i = 0; i < dot; i++) g_path[i] = modulePath[i];
    for (int i = 0; i <= suffixLength; i++) g_path[dot + i] = suffix[i];
    return 1;
}

static HMODULE TryLoad(const WCHAR* modulePath, int level)
{
    if (!BuildPath(modulePath, level)) return 0;
    HMODULE module = LoadLibraryExW(g_path, 0, LOAD_WITH_ALTERED_SEARCH_PATH);
    if (module != 0) Log(L"loaded ", g_path);
    return module;
}

static HMODULE LoadImplementation(void)
{
    static WCHAR self[PATH_CAPACITY];
    HMODULE shim = 0;
    if (!GetModuleHandleExW(GET_MODULE_HANDLE_EX_FLAG_FROM_ADDRESS | GET_MODULE_HANDLE_EX_FLAG_UNCHANGED_REFCOUNT, (const WCHAR*)(void*)&LoadImplementation, &shim))
    {
        Log(L"cannot find the loader module", 0);
        return 0;
    }
    DWORD length = GetModuleFileNameW(shim, self, PATH_CAPACITY);
    if (length == 0 || length >= PATH_CAPACITY)
    {
        Log(L"cannot read the loader path", 0);
        return 0;
    }

    const int v3 = SupportsX64V3();
    int level = v3 ? 3 : 2;
    WCHAR forced[8];
    DWORD forcedLength = GetEnvironmentVariableW(L"PLUGINUI_X64_LEVEL", forced, 8);
    if (forcedLength == 2 && (forced[0] == L'v' || forced[0] == L'V'))
    {
        // v2 can always be forced; v3 only on a processor that runs it (the NativeAOT runtime would end the host).
        if (forced[1] == L'2') level = 2;
        else if (forced[1] == L'3' && v3) level = 3;
    }

    HMODULE module = TryLoad(self, level);
    // v3 build missing (or failing to load): the v2 build runs everywhere. v2 build missing on a v3 processor: v3.
    if (module == 0 && level == 3) module = TryLoad(self, 2);
    else if (module == 0 && v3) module = TryLoad(self, 3);
    if (module == 0) Log(L"no usable build found next to ", self);
    return module;
}

static HMODULE Implementation(void)
{
    for (;;)
    {
        long state = CompareExchange(&g_state, 1, 0);
        if (state == 2) return g_impl;
        if (state == 0)
        {
            g_impl = LoadImplementation();
            AtomicStore(&g_state, 2);
            return g_impl;
        }
        Sleep(0); // another thread is loading
    }
}

__declspec(dllexport) void* GetPluginFactory(void)
{
    HMODULE module = Implementation();
    if (module == 0) return 0;
    GetPluginFactoryProc proc = (GetPluginFactoryProc)(void*)GetProcAddress(module, "GetPluginFactory");
    return proc != 0 ? proc() : 0;
}

__declspec(dllexport) BOOL InitDll(void)
{
    HMODULE module = Implementation();
    if (module == 0) return 0;
    ModuleProc proc = (ModuleProc)(void*)GetProcAddress(module, "InitDll");
    return proc != 0 ? proc() : 1;
}

__declspec(dllexport) BOOL ExitDll(void)
{
    // The implementation stays loaded: a NativeAOT module cannot be unloaded.
    if (g_state != 2 || g_impl == 0) return 1;
    ModuleProc proc = (ModuleProc)(void*)GetProcAddress(g_impl, "ExitDll");
    return proc != 0 ? proc() : 1;
}

BOOL __stdcall DllMain(HMODULE module, DWORD reason, void* reserved)
{
    (void)module; (void)reason; (void)reserved;
    return 1;
}
