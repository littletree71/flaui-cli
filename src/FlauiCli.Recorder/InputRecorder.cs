using System.Collections.Concurrent;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Text;
using FlauiCli.Core;
using FlauiCli.Core.Abstractions;
using FlauiCli.Core.Engine;
using FlauiCli.Core.Protocol;
using FlauiCli.Core.Targeting;

namespace FlauiCli.Recorder;

/// <summary>
/// Captures real mouse and keyboard input and turns it into script steps (similar to playwright codegen).
/// <list type="bullet">
/// <item>The low-level hooks are only installed while capturing and are removed when it stops.</item>
/// <item>The hook thread only queues events (hook callbacks must return quickly).</item>
/// <item>The processing thread uses its own driver instance for FromPoint / selector generation; the steps
/// are reported to flaui-cli through the <see cref="StepWriter"/>.</item>
/// <item>Injected input (FlaUI's SendInput) is ignored, so CLI commands and real input can be recorded together.</item>
/// </list>
/// </summary>
internal sealed class InputRecorder : IDisposable
{
    private const ushort VkQ = 0x51;

    private readonly Func<IUiDriver> _driverFactory;
    private readonly StepWriter _recorder;
    private readonly nint _rootHwnd;
    private readonly int _rootPid;
    private readonly HashSet<int> _pids;
    private readonly BlockingCollection<RawInput> _queue = new();

    private Thread? _hookThread;
    private Thread? _worker;
    private uint _hookThreadId;
    private nint _mouseHook;
    private nint _keyboardHook;
    private HookNativeMethods.HookProc? _mouseProc;
    private HookNativeMethods.HookProc? _keyboardProc;
    private volatile bool _running;

    // State owned by the processing thread
    private IUiDriver? _driver;
    private IUiElement? _root;
    private IUiElement? _textElement;
    private readonly StringBuilder _typed = new();
    private (string Selector, uint Time)? _lastClick;

    public InputRecorder(Func<IUiDriver> driverFactory, StepWriter recorder, nint rootHwnd, IEnumerable<int> pids)
    {
        _driverFactory = driverFactory;
        _recorder = recorder;
        _rootHwnd = rootHwnd;
        _pids = [.. pids.Where(p => p != 0)];
        if (rootHwnd != 0)
        {
            HookNativeMethods.GetWindowThreadProcessId(rootHwnd, out var pid);
            _rootPid = (int)pid;
        }
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
        if (_hookThreadId != 0) HookNativeMethods.PostThreadMessageW(_hookThreadId, HookNativeMethods.WM_QUIT, 0, 0);
        _hookThread?.Join(TimeSpan.FromSeconds(2));
        _running = false;
    }

    /// <summary>Blocks until the capture has ended (stop hotkey, <see cref="Stop"/> or a fatal error).</summary>
    public void WaitForExit()
    {
        _worker?.Join();
        _hookThread?.Join(TimeSpan.FromSeconds(2));
    }

    public void Dispose() => Stop();

    // ───────────────────────── Hook thread ─────────────────────────

    private void HookLoop(ManualResetEventSlim ready)
    {
        _hookThreadId = HookNativeMethods.GetCurrentThreadId();
        _mouseProc = MouseProc;
        _keyboardProc = KeyboardProc;
        var module = HookNativeMethods.GetModuleHandleW(null);
        _mouseHook = HookNativeMethods.SetWindowsHookExW(HookNativeMethods.WH_MOUSE_LL, _mouseProc, module, 0);
        _keyboardHook = HookNativeMethods.SetWindowsHookExW(HookNativeMethods.WH_KEYBOARD_LL, _keyboardProc, module, 0);
        if (_mouseHook == 0 || _keyboardHook == 0) LastError = $"Win32 error {Marshal.GetLastWin32Error()}";
        ready.Set();

        if (LastError is null)
        {
            while (HookNativeMethods.GetMessageW(out _, 0, 0, 0) > 0)
            {
                // Low-level hooks need a message loop
            }
        }

        if (_mouseHook != 0) HookNativeMethods.UnhookWindowsHookEx(_mouseHook);
        if (_keyboardHook != 0) HookNativeMethods.UnhookWindowsHookEx(_keyboardHook);
    }

    private nint MouseProc(int nCode, nint wParam, nint lParam)
    {
        if (nCode >= 0 && _running)
        {
            var msg = (int)wParam;
            if (msg is HookNativeMethods.WM_LBUTTONDOWN or HookNativeMethods.WM_RBUTTONDOWN or HookNativeMethods.WM_MBUTTONDOWN)
            {
                var info = Marshal.PtrToStructure<HookNativeMethods.MSLLHOOKSTRUCT>(lParam);
                if ((info.flags & HookNativeMethods.LLMHF_INJECTED) == 0)
                {
                    var button = msg switch
                    {
                        HookNativeMethods.WM_RBUTTONDOWN => MouseButtonKind.Right,
                        HookNativeMethods.WM_MBUTTONDOWN => MouseButtonKind.Middle,
                        _ => MouseButtonKind.Left,
                    };
                    Enqueue(RawInput.Mouse(new Point(info.pt.X, info.pt.Y), button, info.time));
                }
            }
        }
        return HookNativeMethods.CallNextHookEx(0, nCode, wParam, lParam);
    }

    private nint KeyboardProc(int nCode, nint wParam, nint lParam)
    {
        if (nCode >= 0 && _running && (wParam == HookNativeMethods.WM_KEYDOWN || wParam == HookNativeMethods.WM_SYSKEYDOWN))
        {
            var info = Marshal.PtrToStructure<HookNativeMethods.KBDLLHOOKSTRUCT>(lParam);
            if ((info.flags & HookNativeMethods.LLKHF_INJECTED) == 0)
            {
                var ctrl = Down(HookNativeMethods.VK_CONTROL);
                var shift = Down(HookNativeMethods.VK_SHIFT);
                var alt = Down(HookNativeMethods.VK_MENU);
                var win = Down(HookNativeMethods.VK_LWIN) || Down(HookNativeMethods.VK_RWIN);
                if (ctrl && shift && info.vkCode == VkQ)
                {
                    Enqueue(RawInput.StopSignal);
                    return 1; // Swallow the stop hotkey so the application does not receive it
                }
                var caps = (HookNativeMethods.GetKeyState(HookNativeMethods.VK_CAPITAL) & 1) != 0;
                // Remember which window received the key so that typing in other applications is not recorded
                var foreground = HookNativeMethods.GetForegroundWindow();
                Enqueue(RawInput.Key((ushort)info.vkCode, info.scanCode, ctrl, shift, alt, win, caps, info.time, foreground));
            }
        }
        return HookNativeMethods.CallNextHookEx(0, nCode, wParam, lParam);
    }

    private static bool Down(int vk) => (HookNativeMethods.GetAsyncKeyState(vk) & 0x8000) != 0;

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
            // DPI awareness (Per-Monitor V2) comes from app.manifest
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
                    _recorder.Error(ex.Message);
                }
            }

            FlushText();
        }
        catch (Exception ex)
        {
            LastError = ex.Message;
            _recorder.Error(ex.Message);
        }
        finally
        {
            _running = false;
            try { _driver?.Dispose(); } catch { /* ignore */ }
            if (_hookThreadId != 0) HookNativeMethods.PostThreadMessageW(_hookThreadId, HookNativeMethods.WM_QUIT, 0, 0);
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

    private bool InScope(IUiElement el) => _pids.Contains(el.ProcessId) || el.ProcessId == _rootPid;

    /// <summary>Whether a key went to the captured application (keys typed in other windows are ignored).</summary>
    private bool InScope(nint foregroundHwnd)
    {
        if (foregroundHwnd == 0) return false;
        if (foregroundHwnd == _rootHwnd) return true;
        HookNativeMethods.GetWindowThreadProcessId(foregroundHwnd, out var pid);
        return _pids.Contains((int)pid) || (int)pid == _rootPid;
    }

    private void OnMouse(RawInput ev)
    {
        FlushText();
        var hit = _driver!.FromPoint(ev.Point);
        if (hit is null || !InScope(hit, ev.Point)) return;
        var el = ElementHelpers.PromoteToInteractive(hit);

        var selector = SelectorGenerator.Generate(el, Roots());
        if (ev.Button == MouseButtonKind.Left && _lastClick is { } last && last.Selector == selector
            && ev.Time - last.Time <= HookNativeMethods.GetDoubleClickTime())
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
        if (!InScope(ev.Hwnd))
        {
            FlushText(); // the user switched away; close the pending text step but do not record foreign input
            return;
        }

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
        if (focused is not null && !InScope(focused)) focused = null; // focus already moved to another application
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
                .Set("text", CommandCall.Masked)
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
        if (ev.Shift) state[HookNativeMethods.VK_SHIFT] = 0x80;
        if (ev.Caps) state[HookNativeMethods.VK_CAPITAL] = 0x01;
        var threadId = HookNativeMethods.GetWindowThreadProcessId(HookNativeMethods.GetForegroundWindow(), out _);
        var layout = HookNativeMethods.GetKeyboardLayout(threadId);
        var sb = new StringBuilder(8);
        // wFlags = 4: do not change the keyboard state (keeps dead keys working)
        var n = HookNativeMethods.ToUnicodeEx(ev.Vk, ev.Scan, state, sb, sb.Capacity, 4, layout);
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
        bool Ctrl, bool Shift, bool Alt, bool Win, bool Caps, uint Time, nint Hwnd)
    {
        public static readonly RawInput StopSignal = new(RawInputKind.Stop, default, default, 0, 0, false, false, false, false, false, 0, 0);

        public static RawInput Mouse(Point p, MouseButtonKind b, uint time) =>
            new(RawInputKind.Mouse, p, b, 0, 0, false, false, false, false, false, time, 0);

        public static RawInput Key(ushort vk, uint scan, bool ctrl, bool shift, bool alt, bool win, bool caps, uint time, nint hwnd) =>
            new(RawInputKind.Key, default, default, vk, scan, ctrl, shift, alt, win, caps, time, hwnd);
    }
}
