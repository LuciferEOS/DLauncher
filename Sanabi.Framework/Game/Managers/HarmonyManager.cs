using HarmonyLib;

namespace Sanabi.Framework.Game.Managers;

/// <summary>
///     Container for a <see cref="HarmonyLib.Harmony"/> instance.
/// </summary>
public static class HarmonyManager
{
    private static Harmony _harmony = default!;

    /// <summary>
    ///     Null if the manager hasn't been initialised
    ///         yet.
    /// </summary>
    public static Harmony Harmony => _harmony;

    /// <summary>
    ///     O algo
    /// </summary>
    public static void Initialise()
    {
        Console.WriteLine($"Inited harmony");
        _harmony ??= new(new Guid().ToString());
    }
}
