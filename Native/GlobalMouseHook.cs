using System.Diagnostics;
using System.Runtime.InteropServices;

namespace SnapTranslate.Native;

public sealed class GlobalMouseHook : IDisposable
{
    private readonly Win32Interop.LowLevelMouseProc _hookProc;
    private IntPtr _hookHandle;
    private bool _swallowNextRightUp;
    private bool _disposed;

    public bool IsPaused { get; set; }
    public bool IsInstalled => _hookHandle != IntPtr.Zero;
    public Func<int, int, bool>? ShouldIgnoreAtPoint { get; set; }

    public event Action<int, int>? CtrlRightClickTriggered;

    public GlobalMouseHook()
    {
        _hookProc = HookCallback;
    }

    public void Install()
    {
        if (_hookHandle != IntPtr.Zero)
        {
            return;
        }

        using Process currentProcess = Process.GetCurrentProcess();
        using ProcessModule? mainModule = currentProcess.MainModule;
        IntPtr moduleHandle = mainModule is not null
            ? Win32Interop.GetModuleHandle(mainModule.ModuleName)
            : Win32Interop.GetModuleHandle(null);

        _hookHandle = Win32Interop.SetWindowsHookEx(Win32Interop.WH_MOUSE_LL, _hookProc, moduleHandle, 0);
        if (_hookHandle == IntPtr.Zero)
        {
            int err = Marshal.GetLastWin32Error();
            throw new InvalidOperationException($"ไม่สามารถติดตั้ง Global Mouse Hook ได้ (Win32 Error: {err})");
        }
    }

    private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0)
        {
            int msg = wParam.ToInt32();
            if (msg == Win32Interop.WM_RBUTTONDOWN)
            {
                var hookStruct = Marshal.PtrToStructure<Win32Interop.MSLLHOOKSTRUCT>(lParam);
                int x = hookStruct.pt.x;
                int y = hookStruct.pt.y;

                bool ignore = ShouldIgnoreAtPoint?.Invoke(x, y) ?? false;
                if (!IsPaused && Win32Interop.IsCtrlDown() && !Win32Interop.IsAltDown() && !ignore)
                {
                    _swallowNextRightUp = true;
                    return (IntPtr)1;
                }
            }
            else if (msg == Win32Interop.WM_RBUTTONUP)
            {
                if (_swallowNextRightUp)
                {
                    _swallowNextRightUp = false;
                    var hookStruct = Marshal.PtrToStructure<Win32Interop.MSLLHOOKSTRUCT>(lParam);
                    int x = hookStruct.pt.x;
                    int y = hookStruct.pt.y;

                    // Dispatch asynchronously so the low-level mouse hook returns immediately.
                    _ = Task.Run(() => CtrlRightClickTriggered?.Invoke(x, y));
                    return (IntPtr)1;
                }
            }
        }

        return Win32Interop.CallNextHookEx(_hookHandle, nCode, wParam, lParam);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        if (_hookHandle != IntPtr.Zero)
        {
            Win32Interop.UnhookWindowsHookEx(_hookHandle);
            _hookHandle = IntPtr.Zero;
        }

        GC.SuppressFinalize(this);
    }
}
