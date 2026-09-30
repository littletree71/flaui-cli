using System.Reflection;
using System.Runtime.InteropServices;
using FlauiCli.Core.Protocol;
using FlauiCli.Core.Recording;

namespace FlauiCli.Core.Tests;

public class InputCaptureTests
{
    [Fact]
    public void RecorderMessagesBecomeScriptSteps()
    {
        var recorder = new ScriptRecorder("demo", null);
        using var capture = new InputCapture(recorder, 1, [1]);

        capture.HandleLine(CaptureProtocol.Add + ProtocolJson.Serialize(new CommandCall("click").Set("target", "id=ok")));
        capture.HandleLine(CaptureProtocol.Replace + ProtocolJson.Serialize(new CommandCall("dblclick").Set("target", "id=ok")));
        capture.HandleLine(CaptureProtocol.Add + ProtocolJson.Serialize(new CommandCall("type").Set("text", "héllo 世界")));

        var steps = recorder.ToDocument().Steps;
        Assert.Equal(2, steps.Count);
        Assert.Equal("dblclick", steps[0].Command);
        Assert.Equal("id=ok", steps[0].Get("target"));
        Assert.Equal("héllo 世界", steps[1].Get("text"));
        Assert.Null(capture.LastError);
    }

    [Fact]
    public void RecorderErrorsAndBadMessagesAreReported()
    {
        var recorder = new ScriptRecorder("demo", null);
        using var capture = new InputCapture(recorder, 1, [1]);

        capture.HandleLine(CaptureProtocol.Error + "The window to capture was not found");
        Assert.Equal("The window to capture was not found", capture.LastError);

        capture.HandleLine(CaptureProtocol.Add + "{not json");
        Assert.StartsWith("Invalid message from the recorder", capture.LastError);
        Assert.Equal(0, recorder.Count);
    }

    /// <summary>
    /// The engine (and therefore flaui-cli.exe) must not import hook or keyboard-state APIs; they belong
    /// only to the separate recorder executable. They must not be hidden behind dynamic loading either.
    /// </summary>
    [Fact]
    public void CoreDoesNotImportHookApis()
    {
        string[] banned =
        [
            "SetWindowsHookEx", "UnhookWindowsHookEx", "CallNextHookEx", "GetAsyncKeyState", "GetKeyState",
            "GetKeyboardState", "RegisterRawInputDevices", "GetProcAddress", "LoadLibrary",
        ];
        const BindingFlags all = BindingFlags.Static | BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;

        var imports = typeof(InputCapture).Assembly.GetTypes()
            .SelectMany(t => t.GetMethods(all))
            .Where(m => m.Attributes.HasFlag(MethodAttributes.PinvokeImpl))
            .Select(m => m.GetCustomAttribute<DllImportAttribute>()?.EntryPoint ?? m.Name)
            .ToList();

        Assert.NotEmpty(imports); // the scan really sees the P/Invoke declarations
        Assert.DoesNotContain(imports, name => banned.Any(b => name.StartsWith(b, StringComparison.OrdinalIgnoreCase)));
    }
}
