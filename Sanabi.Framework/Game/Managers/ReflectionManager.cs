using System.Diagnostics.CodeAnalysis;
using System.Runtime.InteropServices;
using HarmonyLib;

namespace Sanabi.Framework.Game.Managers;

/// <summary>
///     Used to reflect on what you've done.
/// </summary>
public static class ReflectionManager
{
    /// <summary>
    ///     Cache of types by their fully qualified type name
    ///         and actual existing type.
    /// </summary>
    private static Dictionary<string, Type> _cachedTypes = new();

    /// <summary>
    ///     Tries to get a type from it's fully qualified type-name.
    ///         Caches type for future calls if found.
    /// </summary>
    /// <param name="qualifiedTypeName">Fully qualified type-name of the specified type; includes it's assembly and namespace.</param>
    /// <returns>True if the type was found.</returns>
    public static bool TryGetTypeByQualifiedName(string qualifiedTypeName, [MaybeNullWhen(false)] out Type type)
    {
        if (_cachedTypes.TryGetValue(qualifiedTypeName, out type))
            return true;

        if (AssemblyManager.TryGetAssembly(ExtractTypePrefix(qualifiedTypeName), out var assembly) &&
            assembly.GetType(qualifiedTypeName) is { } foundType)
        {
            _cachedTypes[qualifiedTypeName] = type = foundType;
            return true;
        }

        if (AccessTools.TypeByName(qualifiedTypeName) is { } accessFoundType)
        {
            _cachedTypes[qualifiedTypeName] = type = accessFoundType;
            return true;
        }

        type = null;
        return false;
    }

    /// <summary>
    ///     Returns the type from it's fully qualified type-name.
    ///         Throws if not possible. Caches type for future calls if found.
    /// </summary>
    /// <param name="except">Throw an exception if type doesnt exist.</param>
    /// <exception cref="InvalidOperationException">Thrown if the type didn't exist in the cache and could not be found via <see cref="AssemblyManager"/>.</exception>
    /// <inheritdoc cref="TryGetTypeByQualifiedName(string, out Type)"/> // inherit param
    public static Type GetTypeByQualifiedName(string qualifiedTypeName, bool except = false)
    {
        ref var cachedTypeRef = ref CollectionsMarshal.GetValueRefOrAddDefault(_cachedTypes, qualifiedTypeName, out var exists);

        if (!exists)
        {
            if (AssemblyManager.TryGetAssembly(ExtractTypePrefix(qualifiedTypeName), out var assembly))
                cachedTypeRef = assembly.GetType(qualifiedTypeName);
            else if (AccessTools.TypeByName(qualifiedTypeName) is { } foundType)
                cachedTypeRef = foundType;
            else
                throw new InvalidOperationException($"Couldn't locate qualified type \"{qualifiedTypeName}\"!");
        }

        if (except &&
            cachedTypeRef is not { })
            throw new InvalidOperationException($"Couldn't resolve {qualifiedTypeName}!");

        return cachedTypeRef!;
    }

    /// <summary>
    ///     Given something like `Content.Client.Admin`,
    ///         this returns `Content.Client`.
    ///
    ///     Purely for easier searching with SS14 based assemblies,
    ///         where first 2 words of the namespace are usually the
    ///         assembly name too
    /// </summary>
    private static string ExtractTypePrefix(string path)
    {
        const char separator = '.';

        var split = path.Split(separator);
        return split[0] + separator + split[1];
    }
}
