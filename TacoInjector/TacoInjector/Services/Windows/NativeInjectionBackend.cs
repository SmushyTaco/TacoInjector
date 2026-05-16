using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;
using TacoInjector.Models;

namespace TacoInjector.Services.Windows;

public sealed class NativeInjectionBackend : IInjectionBackend
{
    public unsafe ValueTask<InjectionResult> InjectAsync(
        int processId,
        string dllPath,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var desiredAccess =
            ProcessAccessRights.CreateThread |
            ProcessAccessRights.QueryInformation |
            ProcessAccessRights.VirtualMemoryOperation |
            ProcessAccessRights.VirtualMemoryWrite |
            ProcessAccessRights.VirtualMemoryRead;

        using var processHandle = new SafeWin32Handle(
            NativeMethods.OpenProcess(
                desiredAccess,
                inheritHandle: false,
                processId: checked((uint)processId)));

        if (processHandle.IsInvalid)
        {
            throw new Win32Exception(
                Marshal.GetLastPInvokeError(),
                $"OpenProcess failed for process {processId}.");
        }

        var dllPathBytes = Encoding.Unicode.GetBytes(dllPath + '\0');

        var remoteMemory = NativeMethods.VirtualAllocEx(
            processHandle,
            address: 0,
            size: checked((nuint)dllPathBytes.Length),
            AllocationType.Commit | AllocationType.Reserve,
            MemoryProtection.ReadWrite);

        if (remoteMemory == 0)
        {
            throw new Win32Exception("VirtualAllocEx failed.");
        }

        try
        {
            fixed (byte* dllPathBuffer = dllPathBytes)
            {
                if (!NativeMethods.WriteProcessMemory(
                        processHandle,
                        remoteMemory,
                        dllPathBuffer,
                        checked((nuint)dllPathBytes.Length),
                        out var bytesWritten))
                {
                    throw new Win32Exception("WriteProcessMemory failed.");
                }

                if (bytesWritten != checked((nuint)dllPathBytes.Length))
                {
                    throw new InvalidOperationException(
                        $"WriteProcessMemory wrote {bytesWritten} bytes, expected {dllPathBytes.Length}.");
                }
            }

            var kernel32 = NativeMethods.GetModuleHandle("kernel32.dll");

            if (kernel32 == 0)
            {
                throw new Win32Exception("GetModuleHandle(kernel32.dll) failed.");
            }

            var loadLibraryW = NativeMethods.GetProcAddress(kernel32, "LoadLibraryW");

            if (loadLibraryW == 0)
            {
                throw new Win32Exception("GetProcAddress(LoadLibraryW) failed.");
            }

            using var remoteThread = new SafeWin32Handle(
                NativeMethods.CreateRemoteThread(
                    processHandle,
                    threadAttributes: 0,
                    stackSize: 0,
                    startAddress: loadLibraryW,
                    parameter: remoteMemory,
                    creationFlags: 0,
                    out _));

            if (remoteThread.IsInvalid)
            {
                throw new Win32Exception(
                    Marshal.GetLastPInvokeError(),
                    "CreateRemoteThread failed.");
            }

            var waitResult = NativeMethods.WaitForSingleObject(
                remoteThread,
                NativeMethods.Infinite);

            if (waitResult != NativeMethods.WaitObject0)
            {
                throw new Win32Exception($"WaitForSingleObject failed: {waitResult}.");
            }

            if (!NativeMethods.GetExitCodeThread(remoteThread, out var loadLibraryResult))
            {
                throw new Win32Exception("GetExitCodeThread failed.");
            }

            if (loadLibraryResult == 0)
            {
                return ValueTask.FromResult(
                    InjectionResult.Failure(
                        $"Process found! | {processId} | valid file path | LoadLibraryW failed"));
            }

            return ValueTask.FromResult(
                InjectionResult.Success(
                    $"Process found! | {processId} | valid file path | Injected!"));
        }
        finally
        {
            NativeMethods.VirtualFreeEx(
                processHandle,
                remoteMemory,
                size: 0,
                FreeType.Release);
        }
    }
}