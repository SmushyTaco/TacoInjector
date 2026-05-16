using System.Runtime.InteropServices;

namespace TacoInjector.Services.Windows;

[Flags]
internal enum ProcessAccessRights : uint
{
    CreateThread = 0x0002,
    QueryInformation = 0x0400,
    VirtualMemoryOperation = 0x0008,
    VirtualMemoryWrite = 0x0020,
    VirtualMemoryRead = 0x0010
}

[Flags]
internal enum AllocationType : uint
{
    Commit = 0x1000,
    Reserve = 0x2000
}

[Flags]
internal enum MemoryProtection : uint
{
    ReadWrite = 0x04
}

[Flags]
internal enum FreeType : uint
{
    Release = 0x8000
}

internal static unsafe partial class NativeMethods
{
    internal const uint Infinite = 0xFFFFFFFF;
    internal const uint WaitObject0 = 0x00000000;

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool CloseHandle(nint handle);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    internal static partial nint OpenProcess(
        ProcessAccessRights desiredAccess,
        [MarshalAs(UnmanagedType.Bool)] bool inheritHandle,
        uint processId);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    internal static partial nint VirtualAllocEx(
        SafeWin32Handle processHandle,
        nint address,
        nuint size,
        AllocationType allocationType,
        MemoryProtection protect);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool VirtualFreeEx(
        SafeWin32Handle processHandle,
        nint address,
        nuint size,
        FreeType freeType);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool WriteProcessMemory(
        SafeWin32Handle processHandle,
        nint baseAddress,
        byte* buffer,
        nuint size,
        out nuint numberOfBytesWritten);

    [LibraryImport("kernel32.dll", EntryPoint = "GetModuleHandleW", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    internal static partial nint GetModuleHandle(string moduleName);

    [LibraryImport("kernel32.dll", EntryPoint = "GetProcAddress", SetLastError = true, StringMarshalling = StringMarshalling.Utf8)]
    internal static partial nint GetProcAddress(
        nint moduleHandle,
        string procName);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    internal static partial nint CreateRemoteThread(
        SafeWin32Handle processHandle,
        nint threadAttributes,
        nuint stackSize,
        nint startAddress,
        nint parameter,
        uint creationFlags,
        out uint threadId);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    internal static partial uint WaitForSingleObject(
        SafeWin32Handle handle,
        uint milliseconds);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool GetExitCodeThread(
        SafeWin32Handle threadHandle,
        out uint exitCode);
}