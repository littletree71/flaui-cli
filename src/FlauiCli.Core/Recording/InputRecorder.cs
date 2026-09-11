using System.Collections.Concurrent;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Text;
using FlauiCli.Core.Abstractions;
using FlauiCli.Core.Engine;
using FlauiCli.Core.Native;
using FlauiCli.Core.Protocol;
using FlauiCli.Core.Targeting;

namespace FlauiCli.Core.Recording;

/// <summary>
/// Captures real mouse and keyboard input and turns it into script steps (similar to playwright codegen).
/// <list type="bullet">
/// <item>The low-level hooks are only installed while capturing and are removed when it stops.</item>
/// <item>The hook thread only queues events (hook callbacks must return quickly).</item>
/// <item>The processing thread uses its own driver instance for FromPoint / selector generation and never
/// shares UIA objects with the daemon's main thread.</item>
/// <item>Injected input (FlaUI's SendInput) is ignored, so CLI commands and real input can be recorded together.</item>
/// </list>
/// </summary>
public sealed class InputRecorder : IDisposable
{
    private const ushort VkQ = 0x51;

    private readonly Func<IUiDriver> _driverFactory;
    private readonly ScriptRecorder _recorder;
    private readonly nint _rootHwnd;
    private readonly HashSet<int> _pids;
    private readonly BlockingCollection<RawInput> _queue = new();

    private Thread? _hookThread;
    private Thread? _worker;
    private uint _hookThreadId;
    private nint _mouseHook;
    private nint _keyboardHook;
    private NativeMethods.HookProc? _mouseProc;
    private NativeMethods.HookProc? _keyboardProc;
    private volatile bool _running;

    // State owned by the processing thread
    private IUiDriver? _driver;
    private IUiElement? _root;
    private IUiElement? _textElement;
    private readonly StringBuilder _typed = new();
    private (string Selector, uint Time)? _lastClick;

    public InputRecorder(Func<IUiDriver> driverFactory, ScriptRecorder recorder, nint rootHwnd, IEnumerable<int> pids)
    {
        _driverFactory = driverFactory;
        _recorder = recorder;
        _rootHwnd = rootHwnd;
        _pids = [.. pids.Where(p => p != 0)];
    }

    public bool IsRunning => _running;

    public string? LastError { get; private set; }

    public void Start()
    {
        if (_rootHwnd == 0) throw new CliException("The current window has no window handle (HWND), so input cannot be captured");
        _running = true;
        _worker = new Thread(WorkerLoop) { IsBackground = true, Name = "flaui-cli-input-worker" };
        _worker.Start();

        using var ready = new ManualResetEventSlim();
        _hookThread = new Thread(() => HookLoop(ready)) { IsBackground = true, Name = "flaui-cli-hooks" };
        _hookThread.Start();
        if (!ready.Wait(TimeSpan.FromSeconds(5)) || LastError is not null)
        {
            var error = LastError ?? "timed out";
            Stop();
            throw new CliException("Cannot install the keyboard/mouse hooks: " + error);
        }
    }

    public void Stop()
    {
        if (!_queue.IsAddingCompleted)
        {
            try { _queue.Add(RawInput.StopSignal); } catch (InvalidOperationException) { /* already completed */ }
            _queue.CompleteAdding();
        }
        _worker?.Join(TimeSpan.FromSeconds(5));
        if (_hookThreadId != 0) NativeMethods.PostThreadMessageW(_hookThreadId, NativeMethods.WM_QUIT, 0, 0);
        _hookThread?.Join(TimeSpan.FromSeconds(2));
        _running = false;
    }

    public void Dispose() => Stop();

    // ───────────────────────── Hook thread ─────────────────────────

    private void HookLoop(ManualResetEventSlim ready)
    {
        _hookThreadId = NativeMethods.GetCurrentThreadId();
        _mouseProc = MouseProc;
        _keyboardProc = KeyboardProc;
        var module = NativeMethods.GetModuleHandleW(null);
        _mouseHook = NativeMethods.SetWindowsHookExW(NativeMethods.WH_MOUSE_LL, _mouseProc, module, 0);
        _keyboardHook = NativeMethods.SetWindowsHookExW(NativeMethods.WH_KEYBOARD_LL, _keyboardProc, module, 0);
        if (_mouseHook == 0 || _keyboardHook == 0) LastError = $"Win32 error {Marshal.GetLastWin32Error()}";
        ready.Set();

        if (LastError is null)
        {
            while (NativeMethods.GetMessageW(out _, 0, 0, 0) > 0)
            {
                // Low-level hooks need a message loop
            }
        }

        if (_mouseHook != 0) NativeMethods.UnhookWindowsHookEx(_mouseHook);
        if (_keyboardHook != 0) NativeMethods.UnhookWindowsHookEx(_keyboardHook);
    }

    private nint MouseProc(int nCode, nint wParam, nint lParam)
    {
        if (nCode >= 0 && _running)
        {
            var msg = (int)wParam;
            if (msg is NativeMethods.WM_LBUTTONDOWN or NativeMethods.WM_RBUTTONDOWN or NativeMethods.WM_MBUTTONDOWN)
            {
                var info = Marshal.PtrToStructure<NativeMethods.MSLLHOOKSTRUCT>(lParam);
                if ((info.flags & NativeMethods.LLMHF_INJECTED) == 0)
                {
                    var button = msg switch
                    {
                        NativeMethods.WM_RBUTTONDOWN => MouseButtonKind.Right,
                        NativeMethods.WM_MBUTTONDOWN => MouseButtonKind.Middle,
                        _ => MouseButtonKind.Left,
                    };
                    Enqueue(RawInput.Mouse(new Point(info.pt.X, info.pt.Y), button, info.time));
                }
            }
        }
        return NativeMethods.CallNextHookEx(0, nCode, wParam, lParam);
    }

    private nint KeyboardProc(int nCode, nint wParam, nint lParam)
    {
        if (nCode >= 0 && _running && (wParam == NativeMethods.WM_KEYDOWN || wParam == NativeMethods.WM_SYSKEYDOWN))
        {
            var info = Marshal.PtrToStructure<NativeMethods.KBDLLHOOKSTRUCT>(lParam);
            if ((info.flags & NativeMethods.LLKHF_INJECTED) == 0)
            {
                var ctrl = Down(NativeMethods.VK_CONTROL);
                var shift = Down(NativeMethods.VK_SHIFT);
                var alt = Down(NativeMethods.VK_MENU);
                var win = Down(NativeMethods.VK_LWIN) || Down(NativeMethods.VK_RWIN);
                if (ctrl && shift && info.vkCode == VkQ)
                {
                    Enqueue(RawInput.StopSignal);
                    return 1; // Swallow the stop hotkey so the application does not receive it
                }
                var caps = (NativeMethods.GetKeyState(NativeMethods.VK_CAPITAL) & 1) != 0;
                Enqueue(RawInput.Key((ushort)info.vkCode, info.scanCode, ctrl, shift, alt, win, caps, info.time));
            }
        }
        return NativeMethods.CallNextHookEx(0, nCode, wParam, lParam);
    }

    private static bool Down(int vk) => (NativeMethods.GetAsyncKeyState(vk) & 0x8000) != 0;

    private void Enqueue(RawInput input)
    {
        try { _queue.TryAdd(input); }
        catch (InvalidOperationException) { /* stopped */ }
    }

    // ───────────────────────── Processing thread ─────────────────────────

    private void WorkerLoop()
    {
        try
        {
            NativeMethods.EnsureDpiAware();
            _driver = _driverFactory();
            _root = _driver.FromHandle(_rootHwnd) ?? throw new CliException("The window to capture was not found");

            foreach (var ev in _queue.GetConsumingEnumerable())
            {
                if (ev.Kind == RawInputKind.Stop) break;
                try
                {
                    if (ev.Kind == RawInputKind.Mouse) OnMouse(ev);
                    else OnKey(ev);
                }
                catch (Exception ex)
                {
                    LastError = ex.Message;
                }
            }

            FlushText();
        }
        catch (Exception ex)
        {
            LastError = ex.Message;
        }
        finally
        {
            _running = false;
            try { _driver?.Dispose(); } catch { /* ignore */ }
            if (_hookThreadId != 0) NativeMethods.PostThreadMessageW(_hookThreadId, NativeMethods.WM_QUIT, 0, 0);
        }
    }

    private IReadOnlyList<IUiElement> Roots()
    {
        var roots = new List<IUiElement> { _root! };
        foreach (var pid in _pids)
        {
            foreach (var w in _driver!.GetTopLevelWindows(pid))
                if (!w.Equals(_root)) roots.Add(w);
        }
        return roots;
    }

    private bool InScope(IUiElement el, Point p) => _pids.Contains(el.ProcessId) || _root!.Bounds.Contains(p);

    private void OnMouse(RawInput ev)
    {
        FlushText();
        var hit = _driver!.FromPoint(ev.Point);
        if (hit is null || !InScope(hit, ev.Point)) return;
        var el = ElementHelpers.PromoteToInteractive(hit);

        var selector = SelectorGenerator.Generate(el, Roots());
        if (ev.Button == MouseButtonKind.Left && _lastClick is { } last && last.Selector == selector
            && ev.Time - last.Time <= NativeMethods.GetDoubleClickTime())
        {
            _recorder.ReplaceLast(new CommandCall("dblclick").Set("target", selector));
            _lastClick = null;
            return;
        }

        var call = new CommandCall("click").Set("target", selector);
        if (ev.Button == MouseButtonKind.Right) call.Set("button", "right");
        if (ev.Button == MouseButtonKind.Middle) call.Set("button", "middle");
        _recorder.Add(call);
        _lastClick = ev.Button == MouseButtonKind.Left ? (selector, ev.Time) : null;
    }

    private void OnKey(RawInput ev)
    {
        var vk = ev.Vk;
        if (KeyParser.IsModifier(vk)) return;

        if (ev.Ctrl || ev.Alt || ev.Win)
        {
            FlushText();
            var keys = new List<ushort>();
            if (ev.Ctrl) keys.Add(0x11);
            if (ev.Shift) keys.Add(0x10);
            if (ev.Alt) keys.Add(0x12);
            if (ev.Win) keys.Add(0x5B);
            keys.Add(vk);
            _recorder.Add(new CommandCall("press").Set("keys", KeyParser.Format(keys)));
            return;
        }

        var ch = ToChar(ev);
        // Caret movement / deletion while editing; the final value is read from ValuePattern when flushing
        var editingKey = vk is 0x08 or 0x2E or 0x25 or 0x27 or 0x24 or 0x23;
        if (ch is null && !(editingKey && _textElement is not null))
        {
            FlushText();
            var keys = ev.Shift ? new List<ushort> { 0x10, vk } : [vk];
            _recorder.Add(new CommandCall("press").Set("keys", KeyParser.Format(keys)));
            return;
        }

        var focused = _driver!.GetFocusedElement();
        if (_textElement is null || focused is null || !focused.Equals(_textElement))
        {
            FlushText();
            _textElement = focused;
        }

        if (ch is not null) _typed.Append(ch.Value);
        else if (vk == 0x08 && _typed.Length > 0) _typed.Length--;
    }

    private void FlushText()
    {
        if (_textElement is null) return;
        var el = _textElement;
        var typed = _typed.ToString();
        _textElement = null;
        _typed.Clear();

        if (el.IsPassword)
        {
            _recorder.Add(new CommandCall("fill")
                .Set("target", SelectorGenerator.Generate(el, Roots()))
                .Set("text", "********")
                .Set("note", "Password field: the value is not recorded; replace it with the real value"));
            return;
        }

        string? value = null;
        try { if (el.IsReadOnly == false) value = el.Value; }
        catch { /* fall back to type when the value cannot be read */ }

        if (value is not null)
            _recorder.Add(new CommandCall("fill").Set("target", SelectorGenerator.Generate(el, Roots())).Set("text", value));
        else if (typed.Length > 0)
            _recorder.Add(new CommandCall("type").Set("text", typed));
    }

    private static char? ToChar(RawInput ev)
    {
        var state = new byte[256];
        if (ev.Shift) state[NativeMethods.VK_SHIFT] = 0x80;
        if (ev.Caps) state[NativeMethods.VK_CAPITAL] = 0x01;
        var threadId = NativeMethods.GetWindowThreadProcessId(NativeMethods.GetForegroundWindow(), out _);
        var layout = NativeMethods.GetKeyboardLayout(threadId);
        var sb = new StringBuilder(8);
        // wFlags = 4: do not change the keyboard state (keeps dead keys working)
        var n = NativeMethods.ToUnicodeEx(ev.Vk, ev.Scan, state, sb, sb.Capacity, 4, layout);
        if (n != 1) return null;
        var c = sb[0];
        return c < 0x20 ? null : c;
    }

    private enum RawInputKind
    {
        Mouse,
        Key,
        Stop,
    }

    private sealed record RawInput(
        RawInputKind Kind, Point Point, MouseButtonKind Button, ushort Vk, uint Scan,
        bool Ctrl, bool Shift, bool Alt, bool Win, bool Caps, uint Time)
    {
        public static readonly RawInput StopSignal = new(RawInputKind.Stop, default, default, 0, 0, false, false, false, false, false, 0);

        public static RawInput Mouse(Point p, MouseButtonKind b, uint time) =>
            new(RawInputKind.Mouse, p, b, 0, 0, false, false, false, false, false, time);

        public static RawInput Key(ushort vk, uint scan, bool ctrl, bool shift, bool alt, bool win, bool caps, uint time) =>
            new(RawInputKind.Key, default, default, vk, scan, ctrl, shift, alt, win, caps, time);
    }
}
