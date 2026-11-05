using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;

namespace OrbusAutoMount;

/// <summary>
/// Read-only game state with very specific aliases matched against the
/// Community Edition and Renewed/Spades IL2CPP dumps. The sole action is
/// the game's existing zero-argument mount summon method.
/// Never write isMounted, player position, network packets or inventory.
/// </summary>
internal static class AutoMountGame
{
    private const BindingFlags All = BindingFlags.Public |
        BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;

    private static Type? _playerType;
    private static float _nextClassSearch;
    private static MethodInfo? _summonMethod;
    private static bool _searchedSummon;

    private static Type? FindGameType(string typeName)
    {
        // Only discover once at plugin load / first check. Never enumerate
        // the entire game's assemblies in a Unity Update loop.
        foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            try
            {
                Type? type = assembly.GetType(typeName, false);
                if (type != null) return type;
            }
            catch { }
        }
        return null;
    }

    internal static Type? PlayerType()
    {
        if (_playerType != null) return _playerType;
        if (Time.realtimeSinceStartup < _nextClassSearch) return null;

        // Unity can load game interop later than the plugin itself. Retry
        // sparingly rather than locking in a permanent null type at boot.
        _nextClassSearch = Time.realtimeSinceStartup + 10f;
        _playerType = FindGameType("MagicalActual.PlayerCharacter") ??
                      FindGameType("ceh");
        return _playerType;
    }

    internal static string DescribeMapping()
    {
        Type? type = PlayerType();
        return type == null ? "not resolved yet"
            : type.FullName == "ceh" ? "Renewed/Spades ceh"
            : "Community PlayerCharacter";
    }

    private static object? ReadMember(object? instance, string name)
    {
        if (instance == null) return null;
        try
        {
            Type type = instance is Type t ? t : instance.GetType();
            object? target = instance is Type ? null : instance;
            PropertyInfo? prop = type.GetProperty(name, All);
            if (prop != null && prop.GetIndexParameters().Length == 0)
                return prop.GetValue(target);
            return type.GetField(name, All)?.GetValue(target);
        }
        catch { return null; }
    }

    private static object? Either(object? instance, string normal,
        string obfuscated) => ReadMember(instance, normal) ??
                              ReadMember(instance, obfuscated);

    private static bool? ReadBool(object? instance, string normal,
        string obfuscated)
    {
        object? raw = Either(instance, normal, obfuscated);
        if (raw is bool state) return state;
        try { return raw == null ? null : Convert.ToBoolean(raw); }
        catch { return null; }
    }

    internal static object? LocalPlayer()
    {
        Type? type = PlayerType();
        return type == null ? null : Either(type, "Instance", "blfr");
    }

    internal static int Identity(object instance)
    {
        try
        {
            return instance is Component component
                ? component.GetInstanceID() : instance.GetHashCode();
        }
        catch { return 0; }
    }

    internal static bool IsActiveLocalPlayer(object player)
    {
        // Singleton should be the local player. Fail closed on a known
        // false flag, a disabled component or a loading-screen object.
        bool? local = ReadBool(player, "isLocallyControlled", "beuf");
        if (local == false) return false;
        if (player is Behaviour behaviour && !behaviour.isActiveAndEnabled)
            return false;
        if (player is Component component &&
            !component.gameObject.activeInHierarchy)
            return false;
        return true;
    }

    internal static object? Avatar(object player) =>
        Either(player, "myPlayerAvatar", "betm");

    private static object? Monster(object player) =>
        Either(player, "myMonster", "betl");

    internal static bool? InCombat(object player) =>
        ReadBool(Monster(player), "inCombat", "bctj");

    internal static bool IsDead(object player) =>
        ReadBool(Monster(player), "isDead", "bcru") == true;

    internal static bool? IsMounted(object avatar) =>
        ReadBool(avatar, "isMounted", "beka");

    internal static bool EquippedMount(object avatar)
    {
        // PlayerAvatar.mountSlot (Community) / cbn.bebp (Spades) is an
        // InventoryItemInstance. A slot object can exist without an item,
        // so also require its non-null baseItem / obfuscated bjer.
        object? mountSlot = Either(avatar, "mountSlot", "bebp");
        return mountSlot != null &&
            Either(mountSlot, "baseItem", "bjer") != null;
    }

    private static MethodInfo? SummonMethod()
    {
        if (_searchedSummon) return _summonMethod;
        _searchedSummon = true;
        Type? type = PlayerType();
        if (type == null) return null;
        string name = type.FullName == "ceh" ? "lew" : "SummonMount";
        _summonMethod = type.GetMethods(All).FirstOrDefault(m =>
            m.Name == name && !m.IsStatic && m.ReturnType == typeof(void) &&
            m.GetParameters().Length == 0);
        return _summonMethod;
    }

    internal static bool TryNormalSummon(object player, out string reason)
    {
        MethodInfo? method = SummonMethod();
        if (method == null)
        {
            reason = "zero-argument SummonMount/ceh.lew method missing";
            return false;
        }
        try
        {
            // This is identical to initiating the game's native summon
            // action; never force PlayerAvatar.SetMount or network state.
            method.Invoke(player, null);
            reason = "native summon invoked (success/eligibility decided by game)";
            return true;
        }
        catch (Exception e)
        {
            Exception actual = e.InnerException ?? e;
            reason = actual.GetType().Name + ": " + actual.Message;
            return false;
        }
    }
}
