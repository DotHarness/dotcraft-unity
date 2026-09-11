#include <windows.h>
#include <string>
#include <atomic>

struct DomainSearch { void* found; const char* (*name)(void*); };
static void FindDomain(void* domain, void* state)
{
    auto search = static_cast<DomainSearch*>(state);
    if (std::string(search->name(domain)) == "Unity Child Domain") search->found = domain;
}

static std::string Utf8(const std::wstring& value)
{
    int size = WideCharToMultiByte(CP_UTF8, 0, value.c_str(), -1, nullptr, 0, nullptr, nullptr);
    std::string result(size, '\0');
    WideCharToMultiByte(CP_UTF8, 0, value.c_str(), -1, &result[0], size, nullptr, nullptr);
    return result;
}

static DWORD Invoke(void* argument, void* expectedDomain = nullptr)
{
    HMODULE mono = GetModuleHandleW(L"mono-2.0-bdwgc.dll");
    if (!mono) mono = GetModuleHandleW(L"mono-2.0-sgen.dll");
    if (!mono) return 1;
#define API(name, type) auto name = reinterpret_cast<type>(GetProcAddress(mono, #name)); if (!name) return 2
    API(mono_get_root_domain, void* (*)());
    API(mono_thread_attach, void* (*)(void*));
    API(mono_thread_detach, void (*)(void*));
    API(mono_domain_foreach, void (*)(void (*)(void*, void*), void*));
    API(mono_domain_set, int (*)(void*, int));
    API(mono_domain_assembly_open, void* (*)(void*, const char*));
    API(mono_assembly_get_image, void* (*)(void*));
    API(mono_class_from_name, void* (*)(void*, const char*, const char*));
    API(mono_class_get_method_from_name, void* (*)(void*, const char*, int));
    API(mono_string_new, void* (*)(void*, const char*));
    API(mono_runtime_invoke, void* (*)(void*, void*, void**, void**));
    DomainSearch search { nullptr, reinterpret_cast<const char* (*)(void*)>(GetProcAddress(mono, "mono_domain_get_friendly_name")) };
    if (!search.name) return 2;
    std::wstring input(static_cast<wchar_t*>(argument));
    auto separator = input.find(L'\n');
    if (separator == std::wstring::npos) return 3;
    auto path = Utf8(input.substr(0, separator));
    auto config = Utf8(input.substr(separator + 1));
    void* thread = mono_thread_attach(mono_get_root_domain());
    if (!thread) return 8;
    mono_domain_foreach(FindDomain, &search);
    void* scriptDomain = search.found;
    if (expectedDomain && expectedDomain != scriptDomain) { mono_thread_detach(thread); return 12; }
    DWORD status = 4;
    if (scriptDomain && mono_domain_set(scriptDomain, 0))
    {
        auto assembly = mono_domain_assembly_open(scriptDomain, path.c_str());
        status = 5;
        if (assembly)
        {
            auto klass = mono_class_from_name(mono_assembly_get_image(assembly), "DotCraft.Unity", "Entry");
            auto method = klass ? mono_class_get_method_from_name(klass, "Initialize", 1) : nullptr;
            status = 6;
            if (method)
            {
                void* args[] = { mono_string_new(scriptDomain, config.c_str()) };
                void* exception = nullptr;
                mono_runtime_invoke(method, nullptr, args, &exception);
                status = exception ? 7 : 0;
                if (exception)
                {
                    auto stringify = reinterpret_cast<void* (*)(void*, void**)>(GetProcAddress(mono, "mono_object_to_string"));
                    auto utf8 = reinterpret_cast<char* (*)(void*)>(GetProcAddress(mono, "mono_string_to_utf8"));
                    auto release = reinterpret_cast<void (*)(void*)>(GetProcAddress(mono, "mono_free"));
                    void* formattingError = nullptr;
                    if (stringify && utf8 && release)
                    {
                        auto textObject = stringify(exception, &formattingError);
                        if (textObject && !formattingError)
                        {
                            auto text = utf8(textObject);
                            auto errorPath = input.substr(separator + 1) + L".error";
                            auto file = CreateFileW(errorPath.c_str(), GENERIC_WRITE, 0, nullptr, CREATE_ALWAYS, FILE_ATTRIBUTE_NORMAL, nullptr);
                            if (file != INVALID_HANDLE_VALUE) { DWORD written; WriteFile(file, text, static_cast<DWORD>(strlen(text)), &written, nullptr); CloseHandle(file); }
                            release(text);
                        }
                    }
                }
            }
        }
    }
    mono_thread_detach(thread);
    return status;
}

static std::atomic<void*> observedDomain{nullptr};
static std::atomic<unsigned long long> epoch{0};
static std::atomic<bool> shuttingDown{false};
static HANDLE changed = nullptr;
static std::atomic<bool> editorLoaded{false}, engineLoaded{false}, recoveryEnabled{true};
static void* (*getDomain)() = nullptr;
static void* (*getAssemblyName)(void*) = nullptr;
static const char* (*getName)(void*) = nullptr;

extern "C" __declspec(dllexport) unsigned long long GetDomainEpoch() { return epoch.load(); }
static void Evidence(DWORD stage);
extern "C" __declspec(dllexport) void SetRecoveryEnabled(int enabled) { recoveryEnabled.store(enabled != 0); Evidence(enabled ? 0 : 200); }

static void AssemblyLoaded(void*, void* assembly)
{
    // Read native identity metadata only; no managed invocation occurs in this callback.
    if (!observedDomain.load() || getDomain() != observedDomain.load()) return;
    auto name = getName(getAssemblyName(assembly));
    if (!name) return;
    if (strcmp(name, "UnityEditor.CoreModule") == 0) editorLoaded.store(true);
    if (strcmp(name, "UnityEngine.CoreModule") == 0) engineLoaded.store(true);
    if (editorLoaded.load() && engineLoaded.load()) SetEvent(changed);
}
static std::wstring bootstrapArgument;
static SRWLOCK bootstrapLock = SRWLOCK_INIT;
static bool installed = false;

static void DomainName(void*, void* domain, const char* name)
{
    if (!name || strcmp(name, "Unity Child Domain") != 0) return;
    if (observedDomain.exchange(domain) == domain) return;
    editorLoaded.store(false);
    engineLoaded.store(false);
    epoch.fetch_add(1);
    SetEvent(changed);
}

static void DomainUnloading(void*, void* domain)
{
    void* expected = domain;
    observedDomain.compare_exchange_strong(expected, nullptr);
}

static void Shutdown(void*)
{
    shuttingDown.store(true);
    SetEvent(changed);
}

static void Evidence(DWORD stage)
{
    auto separator = bootstrapArgument.find(L'\n');
    if (separator == std::wstring::npos) return;
    auto path = bootstrapArgument.substr(separator + 1) + L".lifecycle";
    auto temporary = path + L".tmp";
    auto json = std::string("{\"observer\":true,\"epoch\":") + std::to_string(epoch.load()) + ",\"stage\":" + std::to_string(stage) + "}";
    auto file = CreateFileW(temporary.c_str(), GENERIC_WRITE, 0, nullptr, CREATE_ALWAYS, FILE_ATTRIBUTE_NORMAL, nullptr);
    if (file == INVALID_HANDLE_VALUE) return;
    DWORD written;
    WriteFile(file, json.data(), static_cast<DWORD>(json.size()), &written, nullptr);
    CloseHandle(file);
    MoveFileExW(temporary.c_str(), path.c_str(), MOVEFILE_REPLACE_EXISTING | MOVEFILE_WRITE_THROUGH);
}
static DWORD WINAPI Observe(void*)
{
    unsigned long long processed = 0;
    while (!shuttingDown.load())
    {
        WaitForSingleObject(changed, INFINITE);
        if (shuttingDown.load()) break;
        auto current = epoch.load();
        if (current == processed || !observedDomain.load() || !editorLoaded.load() || !engineLoaded.load() || !recoveryEnabled.load()) continue;
        processed = current;
        // One dispatch per domain-name event. A failed/unknown dispatch is never replayed.
        Evidence(100);
        Evidence(Invoke(const_cast<wchar_t*>(bootstrapArgument.c_str()), observedDomain.load()));
    }
    return 0;
}

extern "C" __declspec(dllexport) DWORD WINAPI Bootstrap(void* argument)
{
    AcquireSRWLockExclusive(&bootstrapLock);
    if (!installed)
    {
        auto mono = GetModuleHandleW(L"mono-2.0-bdwgc.dll");
        if (!mono) mono = GetModuleHandleW(L"mono-2.0-sgen.dll");
        if (!mono) { ReleaseSRWLockExclusive(&bootstrapLock); return 1; }
        auto create = reinterpret_cast<void* (*)(void*)>(GetProcAddress(mono, "mono_profiler_create"));
        auto named = reinterpret_cast<void (*)(void*, void (*)(void*, void*, const char*))>(GetProcAddress(mono, "mono_profiler_set_domain_name_callback"));
        auto unloading = reinterpret_cast<void (*)(void*, void (*)(void*, void*))>(GetProcAddress(mono, "mono_profiler_set_domain_unloading_callback"));
        auto shutdown = reinterpret_cast<void (*)(void*, void (*)(void*))>(GetProcAddress(mono, "mono_profiler_set_runtime_shutdown_begin_callback"));
        auto assemblies = reinterpret_cast<void (*)(void*, void (*)(void*, void*))>(GetProcAddress(mono, "mono_profiler_set_assembly_loaded_callback"));
        getDomain = reinterpret_cast<void* (*)()>(GetProcAddress(mono, "mono_domain_get"));
        getAssemblyName = reinterpret_cast<void* (*)(void*)>(GetProcAddress(mono, "mono_assembly_get_name"));
        getName = reinterpret_cast<const char* (*)(void*)>(GetProcAddress(mono, "mono_assembly_name_get_name"));
        if (!create || !named || !unloading || !shutdown || !assemblies || !getDomain || !getAssemblyName || !getName) { ReleaseSRWLockExclusive(&bootstrapLock); return 9; }
        bootstrapArgument = static_cast<wchar_t*>(argument);
        changed = CreateEventW(nullptr, FALSE, FALSE, nullptr);
        if (!changed) { ReleaseSRWLockExclusive(&bootstrapLock); return 10; }
        auto profiler = create(nullptr);
        named(profiler, DomainName);
        assemblies(profiler, AssemblyLoaded);
        unloading(profiler, DomainUnloading);
        shutdown(profiler, Shutdown);
        auto thread = CreateThread(nullptr, 0, Observe, nullptr, 0, nullptr);
        if (!thread) { ReleaseSRWLockExclusive(&bootstrapLock); return 10; }
        CloseHandle(thread);
        installed = true;
    }
    else if (bootstrapArgument != static_cast<wchar_t*>(argument))
    {
        ReleaseSRWLockExclusive(&bootstrapLock);
        return 11;
    }
    recoveryEnabled.store(true);
    Evidence(100);
    auto result = Invoke(argument);
    Evidence(result);
    ReleaseSRWLockExclusive(&bootstrapLock);
    return result;
}
BOOL WINAPI DllMain(HINSTANCE instance, DWORD reason, LPVOID)
{
    if (reason == DLL_PROCESS_ATTACH) DisableThreadLibraryCalls(instance);
    return TRUE;
}
