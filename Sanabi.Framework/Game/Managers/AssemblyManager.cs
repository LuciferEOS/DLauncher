using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Runtime.InteropServices;

namespace Sanabi.Framework.Game.Managers;

/// <summary>
///     Static container for a <see cref="HarmonyContainer"/>, which
///         itself is just a wrapper for a <see cref="Harmony"/>.
/// </summary>
public static class AssemblyManager
{
    /// <summary>
    ///     Assemblies we need to reference in the future.
    /// </summary>
    private static readonly string[] _necessaryAssemblyNames =
    {
        "Robust.Client",
        "Robust.Shared",
        "Content.Client",
        "Content.Shared"
    };

    private static bool _fulfilled = false;

    /// <summary>
    ///     All assemblies ever found.
    /// </summary>
    public static readonly Dictionary<string, Assembly> AssembliesIncludingUnnecessary = [];

    /// <summary>
    ///     Located necessary assemblies.
    /// </summary>
    public static readonly Dictionary<string, Assembly> Assemblies = [];

    /// <summary>
    ///     Assembly names and list of actions that should be invoked when they
    ///         get loaded.
    /// </summary>
    public static readonly Dictionary<string, List<Action<Assembly>>> SpecificAssemblyCallbacks = new();

    /// <summary>
    ///     Called once when every necessary assembly has
    ///         been resolved.
    /// </summary>
#pragma warning disable CA2211 // Non-constant fields should not be visible
    public static Action? OnAssembliesFulfilled;
#pragma warning restore CA2211

    /// <summary>
    ///     Tries to retrieve an assembly from cache. Can switch between <see cref="Assemblies"/> and
    ///         <see cref="AssembliesIncludingUnnecessary"/>.
    /// </summary>
    public static bool TryGetAssembly(string assemblyName, [MaybeNullWhen(false)] out Assembly assembly, bool includeUnnecessary = false)
        => (includeUnnecessary ? AssembliesIncludingUnnecessary : Assemblies).TryGetValue(assemblyName, out assembly);

    /// <summary>
    ///     Runs a callback once ever when the given assembly gets loaded.
    ///         The callback is run immediately if already loaded.
    /// </summary>
    public static void SubscribeSpecificAssemblyOnce(string assemblyName, Action<Assembly> callback)
    {
        if (TryGetAssembly(assemblyName, out var existingAssembly, includeUnnecessary: true))
        {
            callback.Invoke(existingAssembly);
            return;
        }

        ref var reference = ref CollectionsMarshal.GetValueRefOrAddDefault(SpecificAssemblyCallbacks, assemblyName, out var callbacksExist);
        if (!callbacksExist)
            reference = [];

        reference!.Add(callback);
    }

    /// <summary>
    ///     Subscribes to assembly loads to look
    ///         for the ones we need.
    /// </summary>
    public static void Subscribe()
    {
        AppDomain.CurrentDomain.AssemblyLoad += OnAssemblyLoad;
    }

    /// <summary>
    ///     Unsubscribes from assembly loads.
    /// </summary>
    public static void Unsubscribe()
    {
        AppDomain.CurrentDomain.AssemblyLoad -= OnAssemblyLoad;
    }

    private static void CheckFulfillment()
    {
        if (_fulfilled)
            return;

        var fulfilledCount = 0;
        foreach (var (assemblyName, _) in Assemblies)
        {
            if (_necessaryAssemblyNames.Contains(assemblyName))
                fulfilledCount++;
        }

        Debug.Assert(fulfilledCount <= _necessaryAssemblyNames.Length, "fulfilledCount was higher than #_necessaryAssemblyNames");
        if (fulfilledCount == _necessaryAssemblyNames.Length)
        {
            _fulfilled = true;
            OnAssembliesFulfilled?.Invoke();
            Console.WriteLine($"Assembly-Mng-Fulfilled: {fulfilledCount}");

            return;
        }
    }

    /// <summary>
    ///     Looks at all existing assemblies in the current
    ///         <see cref="AppDomain"/> and saves the ones
    ///         that are necessary.
    /// </summary>
    public static void QueryAssemblies()
    {
        foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            if (assembly.GetName().Name is not { } name)
                continue;

            AssembliesIncludingUnnecessary[name] = assembly;

            foreach (var necessaryAssemblyName in _necessaryAssemblyNames)
            {
                if (name.Contains(necessaryAssemblyName) == true)
                {
                    Assemblies[necessaryAssemblyName] = assembly;
                    Console.WriteLine($"Assembly-Mng-BruteForce-Found: {necessaryAssemblyName}");
                }
            }
        }

        foreach (var (assemblyCallbackKey, assemblyCallbacks) in SpecificAssemblyCallbacks)
        {
            if (!AssembliesIncludingUnnecessary.TryGetValue(assemblyCallbackKey, out var callbackAssembly))
                continue;

            foreach (var assemblyCallback in assemblyCallbacks)
                assemblyCallback.Invoke(callbackAssembly);
        }

        CheckFulfillment();
    }

    private static void OnAssemblyLoad(object? sender, AssemblyLoadEventArgs args)
    {
        var loadedAssembly = args.LoadedAssembly;
        if (loadedAssembly.GetName().Name is not { } assemblyName)
            return;

        AssembliesIncludingUnnecessary[assemblyName] = loadedAssembly;

        foreach (var necessaryAssemblyName in _necessaryAssemblyNames)
        {
            if (assemblyName.Contains(necessaryAssemblyName) == true)
            {
                Assemblies[necessaryAssemblyName] = loadedAssembly;
                Console.WriteLine($"Assembly-Mng-Loaded-Found: {necessaryAssemblyName}");
            }
        }

        if (SpecificAssemblyCallbacks.TryGetValue(assemblyName, out var callbacks))
        {
            foreach (var callback in callbacks)
                callback.Invoke(loadedAssembly);

            SpecificAssemblyCallbacks.Remove(assemblyName);
        }

        CheckFulfillment();
    }
}
