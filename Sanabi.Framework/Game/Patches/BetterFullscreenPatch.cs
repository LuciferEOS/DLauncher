using Sanabi.Framework.Game.Managers;
using HarmonyLib;
using Sanabi.Framework.Patching;
using Sanabi.Framework.Data;
using System.Reflection;
using Sanabi.Framework.Misc.Imports;

namespace Sanabi.Framework.Game.Patches;

/// <summary>
///     Gives you borderless windowed on SDL3
/// </summary>
// Be careful...
public static class BetterFullscreenPatch
{
    public static bool Enabled => SanabiConfig.ProcessConfig.RunBetterFullscreenPatch;
    private static dynamic _clyde = null!;

    private static readonly Type WindowingImplType = ReflectionManager.GetTypeByQualifiedName("Robust.Client.Graphics.Clyde.Clyde+Sdl3WindowingImpl", except: true);
    private static readonly MethodInfo SendCmdMethod = PatchHelpers.GetMethod(WindowingImplType, "SendCmd")!;
    private static readonly Type _clydeType = ReflectionManager.GetTypeByQualifiedName("Robust.Client.Graphics.Clyde.Clyde", except: true);
    private static readonly Type CmdWinSetWindowed = ReflectionManager.GetTypeByQualifiedName("Robust.Client.Graphics.Clyde.Clyde+Sdl3WindowingImpl+CmdWinSetWindowed", except: true);
    private static readonly Type Sdl3WindowReg = ReflectionManager.GetTypeByQualifiedName("Robust.Client.Graphics.Clyde.Clyde+Sdl3WindowingImpl+Sdl3WindowReg", except: true);
    private static readonly Type WindowRegType = ReflectionManager.GetTypeByQualifiedName("Robust.Client.Graphics.Clyde.Clyde+WindowReg", except: true);


    [PatchEntry(PatchRunLevel.Engine)]
    public static void Patch()
    {
        if (!Enabled)
            return;

        PatchHelpers.PatchMethod(
            WindowingImplType,
            "UpdateMainWindowMode",
            PrefixUpdateMainWindowMode,
            HarmonyPatchType.Prefix
        );

        PatchHelpers.PatchMethod(
            _clydeType,
            "SharedWindowCreate",
            SharedWindowCreatePrefix,
            HarmonyPatchType.Prefix
        );

        PatchHelpers.PatchMethod(
            _clydeType,
            "SharedWindowCreate",
            SharedWindowCreatePostfix,
            HarmonyPatchType.Postfix
        );
    }

    private static void SharedWindowCreatePrefix(ref object __instance) => _clyde = __instance;

    // So that fullscreen status updates after mainwindow is possibly set, yes very ironic
    private static void SharedWindowCreatePostfix(ref object __instance) => PrefixUpdateMainWindowMode(ref __instance);

    private static readonly FieldInfo _clyde_mainWindow = _clydeType.GetField("_mainWindow", BindingFlags.Instance | BindingFlags.NonPublic)!;
    private static dynamic? GetMainWindow() => _clyde_mainWindow.GetValue(_clyde);

    private static readonly FieldInfo _clyde_windowMode = _clydeType.GetField("_windowMode", BindingFlags.Instance | BindingFlags.NonPublic)!;
    private static TruncatedClydeWindowMode? GetWindowMode() => (TruncatedClydeWindowMode)_clyde_windowMode.GetValue(_clyde); // Directly casted

    private static object ConstructCmdWinSetWindowed(nint window, int w, int h, int x, int y)
    {
        var cmd = Activator.CreateInstance(CmdWinSetWindowed);

        AccessTools.Field(CmdWinSetWindowed, "Window").SetValue(cmd, window);
        AccessTools.Field(CmdWinSetWindowed, "Width").SetValue(cmd, w);
        AccessTools.Field(CmdWinSetWindowed, "Height").SetValue(cmd, h);
        AccessTools.Field(CmdWinSetWindowed, "PosX").SetValue(cmd, x);
        AccessTools.Field(CmdWinSetWindowed, "PosY").SetValue(cmd, y);

        return cmd!;
    }

    // Fields
    private static readonly FieldInfo sdl3WindowRegSdl3Window = Sdl3WindowReg.GetField("Sdl3Window", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)!;
    private static nint GetSdl3Window(object sdl3WindowReg) => (nint)sdl3WindowRegSdl3Window.GetValue(sdl3WindowReg)!;

    private static readonly FieldInfo windowRegWindowHwnd = Sdl3WindowReg.GetField("WindowsHwnd", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)!;
    private static nint GetWindowHwnd(object windowReg) => (nint)windowRegWindowHwnd.GetValue(windowReg)!;

    private static readonly FieldInfo windowRegWindowSize = WindowRegType.GetField("WindowSize", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)!;
    private static dynamic GetWindowSize(object windowReg) => windowRegWindowSize.GetValue(windowReg)!;
    private static readonly FieldInfo windowRegWindowPos = WindowRegType.GetField("WindowPos", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)!;
    private static dynamic GetWindowPos(object windowReg) => windowRegWindowPos.GetValue(windowReg)!;

    private static readonly FieldInfo windowRegPrevWindowSize = WindowRegType.GetField("PrevWindowSize", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)!;
    private static dynamic GetPrevWindowSize(object windowReg) => windowRegPrevWindowSize.GetValue(windowReg)!;
    private static void SetPrevWindowSize(object windowReg, dynamic val) => windowRegPrevWindowSize.SetValue(windowReg, val);
    private static readonly FieldInfo windowRegPrevWindowPos = WindowRegType.GetField("PrevWindowPos", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)!;
    private static dynamic GetPrevWindowPos(object windowReg) => windowRegPrevWindowPos.GetValue(windowReg)!;
    private static void SetPrevWindowPos(object windowReg, dynamic val) => windowRegPrevWindowPos.SetValue(windowReg, val);

    private static bool WasSdlBound = false;
    private static bool PrefixUpdateMainWindowMode(ref object __instance)
    {
        var mainWindow = GetMainWindow();
        if (mainWindow == null)
            return false;

        if (!WasSdlBound)
        {
            WasSdlBound = true;
            SDL.BindSdl();
        }

        if (GetWindowMode() == TruncatedClydeWindowMode.Fullscreen)
        {
            SetPrevWindowSize(mainWindow, GetWindowSize(mainWindow));
            SetPrevWindowPos(mainWindow, GetWindowPos(mainWindow));

            var displayIndex = (uint)GetBiggestDisplayThatWindowIsIn(mainWindow);
            SDL.SDL_GetDisplayBounds(displayIndex, out var bounds);

            var cmd = ConstructCmdWinSetWindowed(
                GetSdl3Window(mainWindow),
                bounds.w,
                bounds.h,
                bounds.x,
                bounds.y
            );
            SendCmdMethod.Invoke(__instance, [cmd]);
        }
        else
        {
            var prevWindowSize = GetPrevWindowSize(mainWindow);
            var prevWindowPos = GetPrevWindowPos(mainWindow);

            var cmd = ConstructCmdWinSetWindowed(
                GetSdl3Window(mainWindow),
                prevWindowSize.X,
                prevWindowSize.Y,
                prevWindowPos.X,
                prevWindowPos.Y
            );
            SendCmdMethod.Invoke(__instance, [cmd]);
        }

        SDL.SDL_SetWindowBordered(GetWindowHwnd(mainWindow), false);
        return false;
    }

    /// <summary>
    ///     Tries to get the display that the center of the given window is in.
    ///         Throws if it cant.
    /// </summary>
    private static uint GetBiggestDisplayThatWindowIsIn(dynamic windowReg)
    {
        var windowPos = GetWindowPos(windowReg);
        var windowSize = GetWindowSize(windowReg);

        var centerX = windowPos.X + windowSize.X / 2;
        var centerY = windowPos.Y + windowSize.Y / 2;

        SDL.SDL_GetDisplays(out var displayCount);

        // This is 1-indexed for whatever reason
        for (var i = 0u; i <= displayCount; i++)
        {
            SDL.SDL_GetDisplayBounds(i, out var bounds);

            if (centerX >= bounds.x &&
                centerX < bounds.x + bounds.w &&
                centerY >= bounds.y &&
                centerY < bounds.y + bounds.h)
            {
                return i; // this is the monitor index
            }
        }

        throw new InvalidOperationException($"No displays were found that contained the given window! Display count: {displayCount}");
    }
}

public enum TruncatedClydeWindowMode : byte { Windowed = 0, Fullscreen = 1 }

// Purely for ease of use
public record struct Dec_SDL_Rect(int x, int y, int w, int h);
