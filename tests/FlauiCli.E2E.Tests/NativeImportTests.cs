using System.Reflection;
using System.Runtime.InteropServices;
using FlauiCli.Core.Abstractions;
using FlauiCli.Drivers;

namespace FlauiCli.E2E.Tests;

/// <summary>
/// flaui-cli.exe must not import APIs that endpoint protection associates with code injection or input capture. Opening
/// another process's memory (even only to read it) has had the daemon terminated silently; hooks belong to the separate
/// recorder executable. The APIs must not be hidden behind dynamic loading either.
/// Needs no desktop: it only reads the P/Invoke declarations of the assemblies that make up flaui-cli.exe.
/// </summary>
public sealed class NativeImportTests
{
    private static readonly string[] Banned =
    [
        // Cross-process memory and threads
        "OpenProcess", "VirtualAllocEx", "VirtualFreeEx", "VirtualProtectEx", "ReadProcessMemory", "WriteProcessMemory",
        "CreateRemoteThread", "NtWriteVirtualMemory", "NtReadVirtualMemory", "QueueUserAPC",
        // Hooks and keyboard state (FlauiCli.Recorder only)
        "SetWindowsHookEx", "UnhookWindowsHookEx", "CallNextHookEx", "GetAsyncKeyState", "GetKeyState",
        "GetKeyboardState", "RegisterRawInputDevices",
        // Dynamic loading
        "GetProcAddress", "LoadLibrary",
    ];

    public static TheoryData<string> Assemblies() =>
    [
        typeof(IUiElement).Assembly.GetName().Name!,
        typeof(FlaUIDriver).Assembly.GetName().Name!,
        "flaui-cli",
    ];

    [Theory]
    [MemberData(nameof(Assemblies))]
    public void DoesNotImportInjectionOrHookApis(string assemblyName)
    {
        const BindingFlags all = BindingFlags.Static | BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;

        var imports = Assembly.Load(assemblyName).GetTypes()
            .SelectMany(t => t.GetMethods(all))
            .Where(m => m.Attributes.HasFlag(MethodAttributes.PinvokeImpl))
            .Select(m => m.GetCustomAttribute<DllImportAttribute>()?.EntryPoint ?? m.Name)
            .ToList();

        Assert.NotEmpty(imports); // the scan really sees the P/Invoke declarations
        Assert.DoesNotContain(imports, name => Banned.Any(b => name.StartsWith(b, StringComparison.OrdinalIgnoreCase)));
    }
}
