using System.Reflection;
using System.Security.Cryptography;
using BepInEx;
using BepInEx.Logging;
using BepInEx.Unity.IL2CPP;
using HarmonyLib;
using Unity.Netcode;

namespace ChainedBeasts.Rerolls;

[BepInPlugin(Guid, Name, Version)]
public sealed class Plugin : BasePlugin
{
    public const string Guid = "local.chainedbeasts.rerolls", Name = "Chained Beasts Rerolls", Version = "0.1.0";
    internal const string NativeHash = "ba3ef02fa87da4c41bb27d7610539c9578132204f0fb5713833e2ef93415ce29";
    internal static ManualLogSource Logger;
    internal static Service Current;
    internal static string Stamp;
    private Harmony _harmony;
    public override void Load()
    {
        Logger = Log;
        if (!Config.Bind("General", "Enabled", true, "Enable party rerolls on the next launch. Multiplayer requires matching installs on every machine.").Value) return;
        var native = Path.Combine(BepInEx.Paths.GameRootPath, "GameAssembly.dll");
        if (Hash(native) != NativeHash) { Log.LogError("Rerolls disabled: unsupported game build. This preview targets 1.0.2 / 26231777."); return; }
        Stamp = Core.Wire.Hash(Hash(Assembly.GetExecutingAssembly().Location) + Hash(typeof(Core.PartyLedger).Assembly.Location) + NativeHash);
        Current = new Service(); _harmony = new Harmony(Guid);
        try
        {
            Patch(typeof(RunDataManager), "StartNewRunRpc", null, nameof(NewRun), typeof(int), typeof(int), typeof(AscensionLevel), typeof(DifficultyConfig), typeof(int));
            Patch(typeof(Player), nameof(Player.OnDamageAccounted), null, nameof(Damage), typeof(int));
            Patch(typeof(Player), nameof(Player.OnHealthChanged), null, nameof(HealthChanged), typeof(int), typeof(int));
            Patch(typeof(GameLevel), nameof(GameLevel.OnLevelCompleteForPeer), nameof(Completed), null);
            foreach (var type in new[] { typeof(UIGameLobbyTrainerRpc), typeof(UIGameLobbyDoctorRpc), typeof(UIGameLobbyShopRpc) })
            {
                Patch(type, "HandleSubmit", nameof(LocalConfirm), null, typeof(ClientIdentifier));
                Patch(type, "HandleCancel", nameof(LocalCancel), null, typeof(ClientIdentifier));
                Patch(type, "HandleNavigation", nameof(BlockInput), null, typeof(ClientIdentifier), typeof(eNavigation));
                Patch(type, "HandleSubmitRpc", nameof(BlockInput), null, typeof(ClientIdentifier));
                Patch(type, "HandleCancelRpc", nameof(BlockInput), null, typeof(ClientIdentifier));
                Patch(type, "HandleNavigationRpc", nameof(BlockInput), null, typeof(ClientIdentifier), typeof(int));
            }
            Patch(typeof(UIGameLobbyTrainerRpc), "HandleUpgradeButtonSelectedRpc", nameof(BlockInput), null, typeof(ClientIdentifier), typeof(int));
            Patch(typeof(UIGameLobbyTrainerRpc), "HandleSoloUpgradeButtonSelectedRpc", nameof(BlockInput), null, typeof(ClientIdentifier), typeof(int), typeof(int));
            Patch(typeof(UIGameLobbyDoctorRpc), "HandleAutoSelectCompleteRpc", nameof(BlockInput), null, typeof(ClientIdentifier));
            Patch(typeof(UIGameLobbyDoctorRpc), "TryExecuteSelections", nameof(BlockInput), null);
            Patch(typeof(UIGameLobbyShopRpc), "HandlePurchaseSelectionRpc", nameof(BlockInput), null, typeof(ClientIdentifier), typeof(int));
            Patch(typeof(UIGameLobbyShopRpc), "HandlePlayerReadyRpc", nameof(BlockInput), null, typeof(ClientIdentifier));
            Patch(typeof(UIGameLobbyShopRpc), "Execute", nameof(BlockInput), null);
            AddComponent<RerollController>();
            Log.LogInfo("Party rerolls loaded: one shared starting charge, unanimous player confirmation, host-controlled offers.");
        }
        catch (Exception e) { _harmony.UnpatchSelf(); Current.Dispose(); Current = null; Log.LogError("Rerolls installation failed; native behavior preserved: " + e); }
    }
    private static string Hash(string path) { using var stream = File.OpenRead(path); using var sha = SHA256.Create(); return Convert.ToHexString(sha.ComputeHash(stream)).ToLowerInvariant(); }
    private void Patch(Type type, string name, string before, string after, params Type[] arguments)
    {
        var target = AccessTools.DeclaredMethod(type, name, arguments) ?? throw new MissingMethodException(type.FullName, name);
        _harmony.Patch(target, before == null ? null : new HarmonyMethod(typeof(Plugin), before), after == null ? null : new HarmonyMethod(typeof(Plugin), after));
    }
    private static void Observe(Action action) { try { action(); } catch (Exception e) { Logger.LogError("Rerolls observation failed: " + e); } }
    private static void NewRun(RunDataManager __instance, int seed, int runStartTime) => Observe(() => Current?.Fresh(__instance, seed, runStartTime));
    private static void Damage(Player __instance, int damageTaken) => Observe(() => Current?.Damage(__instance, damageTaken));
    private static void HealthChanged(Player __instance, int oldHealth, int newHealth) => Observe(() => Current?.Damage(__instance, Math.Max(0, oldHealth - newHealth)));
    private static void Completed(GameLevel __instance) => Observe(() => Current?.Completed(__instance));
    private static bool BlockInput(UIRpc __instance) => Current?.Busy != true || Current.Visible?.Pointer != __instance.Pointer;
    private static bool LocalConfirm(UIRpc __instance, ClientIdentifier clientID)
    {
        if (BlockInput(__instance)) return true;
        if (clientID != null && NetworkManager.Singleton != null && clientID.NetworkID == NetworkManager.Singleton.LocalClientId)
            Current.QueueVote((int)clientID.PlayerIndex);
        return false;
    }
    private static bool LocalCancel(UIRpc __instance, ClientIdentifier clientID)
    {
        if (BlockInput(__instance)) return true;
        if (clientID != null && NetworkManager.Singleton != null && clientID.NetworkID == NetworkManager.Singleton.LocalClientId)
            Current.QueueVote((int)clientID.PlayerIndex, true);
        return false;
    }
    public override bool Unload() { Current?.Dispose(); Current = null; _harmony?.UnpatchSelf(); return true; }
}
