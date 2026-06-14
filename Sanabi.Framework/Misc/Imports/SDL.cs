using System.Reflection;
using System.Runtime.InteropServices;

namespace Sanabi.Framework.Misc.Imports;

/// <summary>
///     This bs is done so that we can use SDL independent of the version its on.
///         Obviously dont touch any signatures here unless you REALLY KNOW WHAT YOU'RE DOING!
/// </summary>
internal static partial class SDL
{
    /// <summary>
    ///     Should be called when you think (are guessing) that SDL3.dll is loaded in the process.
    /// </summary>
    /// <exception cref="InvalidOperationException">Thrown when SDL3.dll isn't present.</exception>
    internal static void BindSdl()
    {
        var handle = GetSdlHandle();

        SDL_GetDisplayBounds =
            Marshal.GetDelegateForFunctionPointer<SDL_GetDisplayBoundsDelegate>(
                NativeLibrary.GetExport(handle, "SDL_GetDisplayBounds"));

        SDL_GetDisplays =
            Marshal.GetDelegateForFunctionPointer<SDL_GetDisplaysDelegate>(
                NativeLibrary.GetExport(handle, "SDL_GetDisplays"));

        SDL_SetWindowBordered =
            Marshal.GetDelegateForFunctionPointer<SDL_SetWindowBorderedDelegate>(
                NativeLibrary.GetExport(handle, "SDL_SetWindowBordered"));
    }

    internal static nint GetSdlHandle()
    {
        // DLauncher start - linux support
        if (NativeLibrary.TryLoad("SDL3", out var handle))
            return handle;

        if (OperatingSystem.IsLinux())
        {
            if (NativeLibrary.TryLoad("libSDL3.so.0", out handle)) return handle;
            if (NativeLibrary.TryLoad("libSDL3.so", out handle)) return handle;
        }
        // DLauncher end - linux support

        throw new PlatformNotSupportedException("Your platform is too inferior for me to consider even trying to make it work here");
    }

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    public delegate int SDL_GetDisplayBoundsDelegate(
        uint displayIndex,
        out SDL_Rect rect);
    public static SDL_GetDisplayBoundsDelegate SDL_GetDisplayBounds = null!;

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    public delegate int SDL_GetDisplaysDelegate(
        out int count);
    public static SDL_GetDisplaysDelegate SDL_GetDisplays = null!;

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    public delegate int SDL_SetWindowBorderedDelegate(
        nint window,
        SDLBool bordered);
    public static SDL_SetWindowBorderedDelegate SDL_SetWindowBordered = null!;



    [StructLayout(LayoutKind.Sequential)]
    public struct SDL_Rect
    {
        public int x;
        public int y;
        public int w;
        public int h;
    }

    public readonly record struct SDLBool
    {
        private readonly byte value;

        internal const byte FALSE_VALUE = 0;
        internal const byte TRUE_VALUE = 1;

        internal SDLBool(byte value)
        {
            this.value = value;
        }

        public static implicit operator bool(SDLBool b)
        {
            return b.value != FALSE_VALUE;
        }

        public static implicit operator SDLBool(bool b)
        {
            return new SDLBool(b ? TRUE_VALUE : FALSE_VALUE);
        }

        public bool Equals(SDLBool other)
        {
            return other.value == value;
        }

        public override int GetHashCode()
        {
            return value.GetHashCode();
        }
    }
}

/// <summary>
///     Used to
/// </summary>
internal static class SdlModuleTracker
{
    private static IntPtr _sdlHandle;
    private static readonly object _lock = new();

    public static void Init()
    {
        NativeLibrary.SetDllImportResolver(typeof(SdlModuleTracker).Assembly, Resolve);
    }

    private static IntPtr Resolve(string libraryName, Assembly assembly, DllImportSearchPath? searchPath)
    {
        if (IsSdl(libraryName))
        {
            lock (_lock)
            {
                if (_sdlHandle == IntPtr.Zero)
                {
                    // This loads OR attaches to existing load depending on platform
                    NativeLibrary.TryLoad(libraryName, assembly, searchPath, out _sdlHandle);
                }
            }

            return _sdlHandle;
        }

        return IntPtr.Zero;
    }

    private static bool IsSdl(string name) =>
        name.Contains("SDL3", StringComparison.OrdinalIgnoreCase);
}
