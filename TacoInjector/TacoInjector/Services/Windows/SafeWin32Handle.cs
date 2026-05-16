using Microsoft.Win32.SafeHandles;

namespace TacoInjector.Services.Windows;

internal sealed class SafeWin32Handle : SafeHandleZeroOrMinusOneIsInvalid
{
    public SafeWin32Handle()
        : base(ownsHandle: true)
    {
    }

    public SafeWin32Handle(nint handle)
        : base(ownsHandle: true)
    {
        SetHandle(handle);
    }

    protected override bool ReleaseHandle()
    {
        return NativeMethods.CloseHandle(handle);
    }
}