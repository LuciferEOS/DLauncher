using System.Runtime.InteropServices;

namespace Sanabi.Framework.Misc.Imports;

internal static class NativeWin
{
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    public static extern nint GetModuleHandle(string lpModuleName);

    public static nint? GetModuleHandleOrNullIfZero(string lpModuleName)
    {
        var handle = GetModuleHandle(lpModuleName);
        if (handle == nint.Zero)
            return null;

        return handle;
    }
}

/// <summary>
///     TODO: Test
/// </summary>
internal static class NativeLinux
{
    public const int RTLD_NOLOAD = 4;

    [DllImport("libdl.so")]
    public static extern nint dlopen(string? fileName, int flags);

    public static nint? DlopenOrNullIfZero(string? fileName, int flags)
    {
        var handle = dlopen(fileName, flags);
        if (handle == nint.Zero)
            return null;

        return handle;
    }
}
