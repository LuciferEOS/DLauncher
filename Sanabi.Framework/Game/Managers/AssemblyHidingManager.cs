using System.Diagnostics;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.Loader;
using HarmonyLib;
using Sanabi.Framework.Patching;

namespace Sanabi.Framework.Game.Managers;

/// <summary>
///     Manages hiding assemblies from the 999999 different
///         places that list every assembly.
/// </summary>
public static class AssemblyHidingManager
{
    /// <summary>
    ///     Assembly names hidden from view.
    ///         Bool is whether to match exact name (false) or any string that contains the name (true).
    /// </summary>
    private static readonly Dictionary<string, bool> _hiddenAssemblies = new();

    /*
    See: https://github.com/space-wizards/RobustToolbox/blob/9e8f7092ea32a2653776292703d20320f3f34cf5/Robust.Shared/ContentPack/Sandbox.yml#L15

    ```
    # EVERYTHING in these namespaces is allowed.
    # Note that, due to a historical bug in the sandbox, any namespace _prefixed_ with one of these
    # is also allowed. (For instance, RobustBats.X, or ContentFarm.Y)
    WhitelistedNamespaces:
    - Robust
    - Content
    - OpenDreamShared
    ```
    */
    private static readonly string[] _contentNamespaces = ["Robust", "Content", "OpenDreamShared"];

    public static void HideBasicAssemblies()
    {
        HideAssembly("Harmony", exact: false);
        HideAssembly("Sanabi", exact: false);

        HideAssembly("MonoMod", exact: false);

        HideAssembly("System.Reflection", exact: true);
        HideAssembly("System.Reflection.Emit", exact: true);
    }

    /// <summary>
    ///     Hides the assemblies whose <see cref="Assembly.FullName"/>
    ///         matches the given string.
    /// </summary>
    /// <param name="exact">If false, then only hides assemblies whose name is exactly this id. Otherwise, hides those whose name contains this id.</param>
    public static void HideAssembly(string identifier, bool exact = false)
    {
        if (_hiddenAssemblies.ContainsKey(identifier))
            return;

        _hiddenAssemblies[identifier] = exact;
    }

    /// <summary>
    ///     Hides an assembly.
    /// </summary>
    public static void HideAssembly(Assembly assembly, bool exact = false)
    {
        HideAssembly(assembly.GetName().FullName, exact: exact);
    }

    public static void PatchDetectionVectors()
    {
        MethodInfo?[] methods = [
            typeof(AppDomain).GetMethod(nameof(AppDomain.GetAssemblies)),
            typeof(AssemblyLoadContext).GetProperty("Assemblies")?.GetGetMethod(),
            typeof(AssemblyLoadContext).GetProperty("All")?.GetGetMethod(),
            typeof(Assembly).GetMethod(nameof(Assembly.GetTypes)),
            Assembly.GetExecutingAssembly().GetType().GetMethod(nameof(Assembly.GetReferencedAssemblies))
        ];

        var patchMethod = PatchHelpers.GetMethod(typeof(AssemblyHidingManager), "DetectionVectorPatcher");
        foreach (var method in methods)
            PatchHelpers.PatchMethod(
                targetMethod: method,
                patchMethod: patchMethod,
                HarmonyPatchType.Postfix
            );
    }

    private static bool ShouldHideAssembly(string ourAssemblyName)
    {
        foreach (var (hiddenAssemblyName, exact) in _hiddenAssemblies)
        {
            if (exact)
            {
                if (ourAssemblyName == hiddenAssemblyName)
                    return true;
            }
            else
            {
                if (ourAssemblyName.Contains(hiddenAssemblyName))
                    return true;
            }
        }

        return false;
    }

    private static AssemblyName[] HideHiddenAssemblyNames(AssemblyName[] names)
        => [.. names.Where(assemblyName => !ShouldHideAssembly(assemblyName.FullName))];

    private static IEnumerable<Type> HideHiddenTypes(Type[] unhiddenTypes)
    {
        foreach (var type in unhiddenTypes)
        {
            if (ShouldHideAssembly(type.Assembly.GetName().FullName))
                continue;

            yield return type;
        }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void DetectionVectorPatcher(ref object __result)
    {
        // If called from framework or whatever then let it actually use the function
        if (IsCallsiteInHiddenAssembly())
            return;

        // Don't let content see
        /*
            !! `IsCallsiteFromGame` isn't used here because it stops the game from actually initialising
            any external mods. On the other hand `IsCallsiteInHiddenAssembly` is a bit overkill but it works here with less effort
            than it would take to fix `IsCallsiteFromGame` not working with external mods.
        */
        // if (!IsCallsiteFromGame())
        //     return;

        // i hate ts
        switch (__result)
        {
            case AssemblyName[] assemblyNames:
                __result = HideHiddenAssemblyNames(assemblyNames);
                break;
            case Assembly[] originalAssemblies:
                __result = originalAssemblies.Where(assembly => !ShouldHideAssembly(assembly.GetName().FullName)).ToArray();
                break;
            case Type[] types:
                __result = HideHiddenTypes(types).ToArray();
                break;
            case IEnumerable<Assembly> assemblyEnumerable:
                __result = assemblyEnumerable.Where(assembly => !ShouldHideAssembly(assembly.GetName().FullName));
                break;
            case IEnumerable<AssemblyLoadContext> assemblyLoadContextEnumerable:
                __result = assemblyLoadContextEnumerable.Where(context => context.Name != "Assembly.Load(byte[], ...)");
                break;
            default:
                throw new InvalidOperationException($"Bad type: {__result.GetType()}");
        }
    }

    /// <returns>Whether the call-site of this method is in a currently hidden assembly.</returns>
    public static bool IsCallsiteInHiddenAssembly()
    {
        var stackTrace = new StackTrace();
        var frames = stackTrace.GetFrames();

        foreach (var frame in frames)
        {
            var method = frame.GetMethod();
            if (method == null ||
                method.DeclaringType == null)
                continue;

            if (!ShouldHideAssembly(method.DeclaringType.Assembly.GetName().FullName))
                return false;
        }

        return true;
    }

    /// <returns>Whether the stack-trace of this method's call-site was ever in any Robust/Content/OpenDreamShared namespace.</returns>
    public static bool IsCallsiteFromGame()
    {
        var stackTrace = new StackTrace();
        var frames = stackTrace.GetFrames();

        foreach (var frame in frames)
        {
            var method = frame.GetMethod();
            if (method == null ||
                method.DeclaringType?.Namespace is not { } methodNamespace ||
                methodNamespace.Length == 0)
                continue;

            foreach (var badNamespace in _contentNamespaces)
            {
                if (methodNamespace.StartsWith(badNamespace))
                    return true;
            }
        }

        return false;
    }
}
