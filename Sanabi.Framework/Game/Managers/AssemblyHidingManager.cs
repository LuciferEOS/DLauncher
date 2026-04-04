using System.Diagnostics;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.Loader;
using HarmonyLib;
using Sanabi.Framework.Misc;
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
    private static readonly Dictionary<string, bool> _hiddenAssemblies = [];

    /// <summary>
    ///     Callsites that will be treated as if theyre in a hidden assembly.
    /// </summary>
    public static readonly HashSet<MethodInfo> OverridenCallsites = [];

    /// <summary>
    ///     Callsites that will be treated as if theyre in a hidden assembly,
    ///         ONLY if they are never called outside of hidden assemblies/engine.
    /// </summary>
    public static readonly HashSet<MethodInfo> EngineOverridenCallsites = [];

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
    private static readonly string[] _engineNamespaces = ["Robust.Client", "Robust.Shared", "System.Private.CoreLib"];

    public static void HideBasicAssemblies()
    {
        HideAssembly("Harmony", exact: false);
        HideAssembly("Sanabi", exact: false);

        HideAssembly("MonoMod", exact: false);

        HideAssembly("SS14.Common", exact: true);
        HideAssembly("System.Reflection.Emit", exact: true);
    }

    /// <summary>
    ///     Hides the assemblies whose <see cref="Assembly.Name"/>
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
    ///     Unhides the given assembly by ID.
    /// </summary>
    public static void UnhideAssembly(string identifier)
    {
        _hiddenAssemblies.Remove(identifier);
    }

    /// <summary>
    ///     Hides an assembly.
    /// </summary>
    public static void HideAssembly(Assembly assembly, bool exact = false)
    {
        HideAssembly(assembly.GetName().Name ?? "", exact: exact);
    }

    public static void PatchDetectionVectors()
    {
        MethodInfo?[] methods = [
            typeof(AppDomain).GetMethod(nameof(AppDomain.GetAssemblies)), // Assembly[]
            typeof(AssemblyLoadContext).GetProperty("Assemblies")?.GetGetMethod(), // IEnumerable<Assembly>
            typeof(AssemblyLoadContext).GetProperty("All")?.GetGetMethod(), // IEnumerable<AssemblyLoadContext>
            typeof(Assembly).GetMethod(nameof(Assembly.GetTypes)), // Type[]
            typeof(Assembly).GetMethod(nameof(Assembly.GetType), [typeof(string)]), //Type
            typeof(Assembly).GetProperty("DefinedTypes")?.GetGetMethod(), // IEnumerable<TypeInfo>
            Assembly.GetExecutingAssembly().GetType().GetMethod(nameof(Assembly.GetReferencedAssemblies)) // AssemblyName[]
        ];

        var patchMethod = PatchHelpers.GetMethod(DetectionVectorPatcher);
        foreach (var method in methods)
            PatchHelpers.PatchMethod(
                targetMethod: method,
                patchMethod: patchMethod,
                HarmonyPatchType.Postfix
            );

        //Type (case-sens options, throwonerror options, the other other GetType uses this)
        PatchHelpers.PatchMethod(
            targetMethod: typeof(Assembly).GetMethod(nameof(Assembly.GetType), [typeof(string), typeof(bool), typeof(bool)]),
            patchMethod: PatchHelpers.GetMethod(GetTypeCaseSensitiveThrowOnErrorPatch),
            HarmonyPatchType.Postfix
        );
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void GetTypeCaseSensitiveThrowOnErrorPatch(ref Type? __result, ref string name, ref bool throwOnError, ref bool ignoreCase)
    {
        if (__result is not { } ||
            !ShouldHideType(__result) ||
            IsCallsiteThroughHiddenAssembly(2)) // ignore callsites of IsCallsiteThroughHiddenAssembly and this
            return;

        if (throwOnError)
        {
            // Mimic actual throw
            Assembly.GetExecutingAssembly().GetType("", throwOnError, ignoreCase);
            throw new TypeLoadException($"Type {name} could not be found");
        }
        else
            __result = null;
    }

    public static bool ShouldHideAssembly(string ourAssemblyName)
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
        => [.. names.Where(assemblyName => !ShouldHideAssembly(assemblyName.Name ?? ""))];

    private static IEnumerable<Type> HideHiddenTypes(Type[] unhiddenTypes)
    {
        foreach (var type in unhiddenTypes)
        {
            if (ShouldHideType(type))
                continue;

            yield return type;
        }
    }

    private static bool ShouldHideType(Type type) => ShouldHideAssembly(type.Assembly.GetName().Name ?? "");

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void DetectionVectorPatcher(ref object __result)
    {
        // If called from framework or whatever then let it actually use the function
        if (IsCallsiteThroughHiddenAssembly(2)) // ignore callsites of IsCallsiteThroughHiddenAssembly and this
            return;

        if (__result == null)
            return;

        switch (__result)
        {
            case Assembly[] originalAssemblies:
                __result = originalAssemblies.Where(assembly => !ShouldHideAssembly(assembly.GetName().Name ?? "")).ToArray();
                break;
            case IEnumerable<Assembly> assemblyEnumerable:
                __result = assemblyEnumerable.Where(assembly => !ShouldHideAssembly(assembly.GetName().Name ?? ""));
                break;
            case IEnumerable<AssemblyLoadContext> assemblyLoadContextEnumerable:
                __result = assemblyLoadContextEnumerable.Where(context => context.Name != "Assembly.Load(byte[], ...)");
                break;
            case Type[] types:
                __result = HideHiddenTypes(types).ToArray();
                break;
            case Type type:
                if (ShouldHideType(type))
                    __result = null!;

                break;
            case IEnumerable<TypeInfo> assemblyTypeInfos:
                __result = assemblyTypeInfos.Where(typeInfo => !ShouldHideAssembly(typeInfo.Assembly.GetName().Name ?? ""));
                break;
            case AssemblyName[] assemblyNames:
                __result = HideHiddenAssemblyNames(assemblyNames);
                break;
            default:
                throw new InvalidOperationException($"Bad type: {__result.GetType()}");
        }
    }

    /// <returns>Whether any call-site of this stack is in a currently hidden assembly, except for the first <paramref name="ignoredFirst"/> frames.</returns>
    public static bool IsCallsiteThroughHiddenAssembly(int ignoredFirst = 2)
    {
        var stackTrace = new StackTrace();
        var frames = stackTrace.GetFrames();
        var index = 0;

        var onlyInEngine = false;
        var wasOutsideEngine = false;

        foreach (var frame in frames)
        {
            if (++index <= ignoredFirst)
                continue;

            if (frame.GetMethod() is not { } methodInfo ||
                methodInfo.DeclaringType is not { } declaringType)
                continue;

            if (OverridenCallsites.Contains(methodInfo))
                return true;

            if (EngineOverridenCallsites.Contains(methodInfo))
                onlyInEngine = true;

            var assemblyName = declaringType.Assembly.GetName().Name ?? "";
            if (onlyInEngine && !_engineNamespaces.Contains(assemblyName))
            {
                SanabiLogger.LogWarn($"CUR-LEFTUS: {assemblyName}");
                wasOutsideEngine = true;
            }

            if (ShouldHideAssembly(assemblyName))
                return true;
        }

        if (onlyInEngine)
            return !wasOutsideEngine;

        return false;
    }

    /// <returns>Whether any call-site of this stack is in the given assembly.</returns>
    public static bool IsCallsiteThroughAssembly(string assemblyName, bool exact = false)
    {
        var stackTrace = new StackTrace();
        var frames = stackTrace.GetFrames();

        foreach (var frame in frames)
        {
            if (frame.GetMethod() is not { } methodInfo ||
                methodInfo.DeclaringType is not { } declaringType)
                continue;

            var otherAssemblyName = declaringType.Assembly.GetName().Name ?? "";
            if (exact && otherAssemblyName == assemblyName)
                return true;
            else if (!exact && otherAssemblyName.Contains(assemblyName))
                return true;
        }

        return false;
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
