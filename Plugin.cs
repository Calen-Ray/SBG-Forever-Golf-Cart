using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using Mirror;
using UnityEngine;

namespace ForeverGolfCart
{
    [BepInPlugin(ModGuid, ModName, ModVersion)]
    public sealed class Plugin : BaseUnityPlugin
    {
        public const string ModGuid = "sbg.forevergolfcart";
        public const string ModName = "ForeverGolfCart";
        public const string ModVersion = "0.3.2";

        internal static ManualLogSource Log;
        internal static Plugin Instance;

        internal ConfigEntry<float> hotkeyHoldDurationConfig;
        internal ConfigEntry<KeyCode> summonKeyConfig;
        internal ConfigEntry<bool> pastelTintEnabledConfig;
        internal ConfigEntry<bool> verboseLoggingConfig;

        // Reflected at startup so the server-side summon path can directly seat the requesting
        // player without round-tripping through the rate-limited Cmd RPC.
        private static MethodInfo serverEnterMethod;

        // Server-only registry of carts we summoned. Watched per-frame; when a tracked cart's
        // passenger list and driver-seat reserver are both empty, the server destroys it.
        private static readonly HashSet<uint> ServerTrackedCarts = new HashSet<uint>();

        // Client-side: every client (host included) maintains its own "this cart was summoned
        // by a ForeverGolfCart user" set. The server pushes entries via TintMsg after spawn.
        // Looked up by GolfCartInfo at apply-tint time so we don't tint vanilla carts.
        private static readonly HashSet<uint> ClientTintedCarts = new HashSet<uint>();

        private static bool serializerRegistered;
        private static bool serverHandlerRegistered;
        private static bool clientHandlerRegistered;

        private float keyHeldFor;
        private bool summonedThisHold;

        private void Awake()
        {
            Instance = this;
            Log = Logger;

            hotkeyHoldDurationConfig = Config.Bind(
                "Spawn",
                "HotkeyHoldDuration",
                0.4f,
                "Seconds to hold the summon key before the cart spawns. Prevents accidental Q taps from summoning a cart.");
            summonKeyConfig = Config.Bind(
                "Spawn",
                "SummonKey",
                KeyCode.Q,
                "Key to summon a cart. Any UnityEngine.KeyCode value (letters, numbers, function keys, modifiers, mouse buttons via Mouse0..4).");
            pastelTintEnabledConfig = Config.Bind(
                "Visuals",
                "PastelTintEnabled",
                true,
                "Tint summoned carts with a pastel hue derived from their netId so non-natural carts are recognisable. Each cart's color is consistent across modded clients.");
            verboseLoggingConfig = Config.Bind(
                "Diagnostics",
                "VerboseLogging",
                false,
                "Emit lines whenever the summon key fires, the summon request lands on the server, and the cart spawns / auto-seat. Off by default.");

            serverEnterMethod = AccessTools.Method(typeof(GolfCartInfo), "ServerEnter");

            RegisterSerializers();

            // Subscribing to NetworkClient.OnConnectedEvent here looked correct, but Mirror's
            // NetworkManager.Awake later does `NetworkClient.OnConnectedEvent = OnClientConnectInternal`
            // (assignment, not +=), wiping our subscription. v0.3.0 shipped that broken handler-
            // registration path, so when the host's tint broadcast looped back to its own client
            // the client found no handler for the message id → "Unknown message id: 14576" →
            // Mirror disconnects the client → crash to lobby. v0.3.1 polls in Update instead.
            new Harmony(ModGuid).PatchAll();
            Log.LogInfo($"{ModName} v{ModVersion} loaded.");
        }

        private void OnDestroy()
        {
        }

        private static void EnsureHandlersRegistered()
        {
            if (!serverHandlerRegistered && NetworkServer.active)
            {
                NetworkServer.ReplaceHandler<ForeverGolfCartSummonMsg>(OnSummonRequest);
                serverHandlerRegistered = true;
                LogVerbose("registered server handler ForeverGolfCartSummonMsg.");
            }
            if (!clientHandlerRegistered && NetworkClient.active)
            {
                NetworkClient.ReplaceHandler<ForeverGolfCartTintMsg>(OnTintMessage);
                clientHandlerRegistered = true;
                LogVerbose("registered client handler ForeverGolfCartTintMsg.");
            }
            // Mirror tears handler dictionaries down on shutdown, so flip the flags back to
            // false when the corresponding side goes offline. The next connect re-registers.
            if (serverHandlerRegistered && !NetworkServer.active)
                serverHandlerRegistered = false;
            if (clientHandlerRegistered && !NetworkClient.active)
                clientHandlerRegistered = false;
        }

        private static void RegisterSerializers()
        {
            if (serializerRegistered)
                return;
            Writer<ForeverGolfCartSummonMsg>.write = (w, m) => { };
            Reader<ForeverGolfCartSummonMsg>.read = r => default(ForeverGolfCartSummonMsg);
            Writer<ForeverGolfCartTintMsg>.write = (w, m) => w.WriteUInt(m.netId);
            Reader<ForeverGolfCartTintMsg>.read = r => new ForeverGolfCartTintMsg { netId = r.ReadUInt() };
            serializerRegistered = true;
        }

        private void Update()
        {
            EnsureHandlersRegistered();
            UpdateHotkey();
            if (NetworkServer.active)
                UpdateServerCartTracking();
        }

        private void UpdateHotkey()
        {
            if (InputBridge.IsHeld(summonKeyConfig.Value))
            {
                keyHeldFor += Time.deltaTime;
                if (!summonedThisHold && keyHeldFor >= hotkeyHoldDurationConfig.Value)
                {
                    LogVerbose($"summon key {summonKeyConfig.Value} held for {keyHeldFor:F2}s — invoking summon.");
                    TrySummonCart();
                    summonedThisHold = true;
                }
            }
            else
            {
                keyHeldFor = 0f;
                summonedThisHold = false;
            }
        }

        internal static void LogVerbose(string message)
        {
            if (Instance != null && Instance.verboseLoggingConfig.Value)
                Log?.LogInfo("ForeverGolfCart: " + message);
        }

        private void TrySummonCart()
        {
            PlayerInfo info = GameManager.LocalPlayerInfo;
            if (info == null) return;
            if (info.ActiveGolfCartSeat.IsValid())
                return;
            if (info.AsGolfer != null && info.AsGolfer.IsMatchResolved)
                return;

            if (NetworkServer.active)
            {
                ServerSummonForPlayer(info);
            }
            else if (NetworkClient.active && NetworkClient.ready)
            {
                NetworkClient.Send(new ForeverGolfCartSummonMsg());
            }
        }

        private static void OnSummonRequest(NetworkConnectionToClient conn, ForeverGolfCartSummonMsg _)
        {
            if (conn == null || conn.identity == null)
                return;
            PlayerInfo info = conn.identity.GetComponent<PlayerInfo>();
            if (info == null)
                return;
            ServerSummonForPlayer(info);
        }

        private static void ServerSummonForPlayer(PlayerInfo target)
        {
            if (!NetworkServer.active || target == null)
                return;
            if (GameManager.GolfCartSettings == null || GameManager.GolfCartSettings.Prefab == null)
            {
                Log?.LogWarning("ForeverGolfCart summon ignored: GolfCartSettings.Prefab missing.");
                return;
            }

            GolfCartInfo cart = Object.Instantiate(
                GameManager.GolfCartSettings.Prefab,
                target.transform.position,
                Quaternion.Euler(0f, target.transform.eulerAngles.y, 0f));
            if (cart == null)
            {
                Log?.LogWarning("ForeverGolfCart: cart prefab failed to instantiate.");
                return;
            }

            // 0.3.0/0.3.1 called ServerReserveDriverSeatPreNetworkSpawn → spawn → PostNetworkSpawn
            // → ServerEnter. The Pre call sets the `driverSeatReserver` SyncVar to the summoner;
            // vanilla normally clears that via CmdInformDrivingSeatReservationReceived once the
            // player walks up and enters, but our ServerEnter path bypasses that Cmd, so the
            // reserver stayed permanently set on every summoned cart. When a fresh player joined
            // the lobby later, initial-state sync fired `OnDriverSeatReserverChanged(null, host)`
            // before their own `GameManager.LocalPlayerInfo` was wired up — `null == null` entered
            // the first branch → NRE on `LocalPlayerInventory` → size-hash mismatch → kicked.
            //
            // ServerTryAssignPassengerToSeat already handles authority assignment when seat==0
            // (mirrors what ServerReserveDriverSeatPostNetworkSpawn would have done), so we can
            // skip the reservation step entirely and seat the player directly.
            NetworkServer.Spawn(cart.gameObject);

            uint netId = cart.netId;
            ServerTrackedCarts.Add(netId);
            ClientTintedCarts.Add(netId); // host is also a client — make the tint visible locally
            LogVerbose($"summoned cart netId={netId} for {target.name} — invoking ServerEnter.");

            if (serverEnterMethod != null)
            {
                try
                {
                    serverEnterMethod.Invoke(cart, new object[] { target });
                }
                catch (System.Exception ex)
                {
                    Log?.LogWarning($"ForeverGolfCart: ServerEnter reflection invoke failed: {ex.Message}");
                }
            }

            // Apply locally on the host immediately, then broadcast to remote clients so
            // every modded peer tints the same cart. Mirror's NetworkServer.SendToAll only
            // reaches clients that have a handler registered for the message type — vanilla
            // peers ignore unknown ids without disconnecting in this Mirror version.
            if (Instance != null && Instance.pastelTintEnabledConfig.Value)
            {
                ApplyPastelTint(cart);
                if (Instance != null)
                    Instance.StartCoroutine(ReapplyTintNextFrames(cart));
            }
            NetworkServer.SendToAll(new ForeverGolfCartTintMsg { netId = netId });
        }

        private static IEnumerator ReapplyTintNextFrames(GolfCartInfo cart)
        {
            // Cosmetic / driver overlays that fire after seat-entry can stomp the property
            // block. Re-apply for several frames to win the race.
            for (int i = 0; i < 6 && cart != null; i++)
            {
                yield return null;
                ApplyPastelTint(cart);
            }
        }

        private static void OnTintMessage(ForeverGolfCartTintMsg msg)
        {
            ClientTintedCarts.Add(msg.netId);
            if (Instance == null || !Instance.pastelTintEnabledConfig.Value)
                return;
            if (NetworkClient.spawned.TryGetValue(msg.netId, out NetworkIdentity identity) && identity != null)
            {
                GolfCartInfo cart = identity.GetComponent<GolfCartInfo>();
                if (cart != null)
                {
                    ApplyPastelTint(cart);
                    if (Instance != null)
                        Instance.StartCoroutine(ReapplyTintNextFrames(cart));
                }
            }
        }

        // OnStartClient catches the case where the tint message arrived before the cart
        // spawned locally — we already added the netId to ClientTintedCarts in the message
        // handler, so this fires the apply on first sight.
        //
        // 0.3.0 placed [HarmonyPatch] on the postfix method instead of an enclosing type, so
        // PatchAll() (which scans types) never installed it — the late-arrival tint path was
        // dead code. 0.3.1 wraps it in the standard nested-class pattern.
        [HarmonyPatch(typeof(GolfCartInfo), "OnStartClient")]
        internal static class Patch_GolfCartInfo_OnStartClient
        {
            private static void Postfix(GolfCartInfo __instance)
            {
                if (Instance == null || !Instance.pastelTintEnabledConfig.Value)
                    return;
                if (__instance == null || __instance.netId == 0)
                    return;
                if (!ClientTintedCarts.Contains(__instance.netId))
                    return;
                ApplyPastelTint(__instance);
            }
        }

        private void UpdateServerCartTracking()
        {
            if (ServerTrackedCarts.Count == 0)
                return;

            List<uint> toRemove = null;
            foreach (uint netId in ServerTrackedCarts)
            {
                if (!NetworkServer.spawned.TryGetValue(netId, out NetworkIdentity identity) || identity == null)
                {
                    if (toRemove == null) toRemove = new List<uint>();
                    toRemove.Add(netId);
                    continue;
                }
                GolfCartInfo cart = identity.GetComponent<GolfCartInfo>();
                if (cart == null)
                {
                    if (toRemove == null) toRemove = new List<uint>();
                    toRemove.Add(netId);
                    continue;
                }

                if (IsCartFullyEmpty(cart))
                {
                    if (toRemove == null) toRemove = new List<uint>();
                    toRemove.Add(netId);
                    NetworkServer.Destroy(cart.gameObject);
                }
            }

            if (toRemove != null)
            {
                foreach (uint id in toRemove)
                {
                    ServerTrackedCarts.Remove(id);
                    ClientTintedCarts.Remove(id);
                }
            }
        }

        private static bool IsCartFullyEmpty(GolfCartInfo cart)
        {
            if (cart.NetworkdriverSeatReserver != null)
                return false;
            for (int i = 0; i < cart.passengers.Count; i++)
            {
                if (cart.passengers[i] != null)
                    return false;
            }
            return true;
        }

        private static void ApplyPastelTint(GolfCartInfo cart)
        {
            if (cart == null) return;
            // Hash netId to HSV pastel: hue 0..1, saturation 0.55, value 0.92 — recognisable
            // but never overwhelms the silhouette. Multiplied into the existing material color
            // via direct material instance edits (more robust than MaterialPropertyBlock when
            // cosmetic overlays fire after seat-entry).
            uint id = cart.netId;
            float hue = (id * 0.61803398f) % 1f;
            Color tint = Color.HSVToRGB(hue, 0.55f, 0.92f);
            tint.a = 1f;

            Renderer[] renderers = cart.GetComponentsInChildren<Renderer>(includeInactive: true);
            foreach (Renderer r in renderers)
            {
                if (r == null) continue;
                Material[] mats = r.materials; // returns instances, isolated from the prefab
                if (mats == null) continue;
                bool changed = false;
                for (int i = 0; i < mats.Length; i++)
                {
                    Material m = mats[i];
                    if (m == null) continue;
                    if (m.HasProperty("_Color"))
                    {
                        m.color = m.color * tint;
                        changed = true;
                    }
                    if (m.HasProperty("_BaseColor"))
                    {
                        m.SetColor("_BaseColor", m.GetColor("_BaseColor") * tint);
                        changed = true;
                    }
                }
                if (changed)
                    r.materials = mats;
            }
        }
    }

    internal struct ForeverGolfCartSummonMsg : NetworkMessage
    {
    }

    internal struct ForeverGolfCartTintMsg : NetworkMessage
    {
        public uint netId;
    }
}
