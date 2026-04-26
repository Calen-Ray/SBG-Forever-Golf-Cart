# Changelog

## v0.3.2

- **Fix joining clients getting kicked because of a stale driver-seat reservation on
  summoned carts.** 0.3.0/0.3.1 called `ServerReserveDriverSeatPreNetworkSpawn(target)` to
  set the cart's `driverSeatReserver` SyncVar before `NetworkServer.Spawn`. Vanilla normally
  clears that reservation through `CmdInformDrivingSeatReservationReceived` once the player
  walks up to the placed cart and enters; our `ServerEnter` shortcut bypassed the Cmd, so
  the reserver stayed permanently set on every summoned cart. When a fresh player joined the
  lobby, initial-state sync of the cart fired `OnDriverSeatReserverChanged(null, hostPlayer)`
  before their own `GameManager.LocalPlayerInfo` was available. The hook compares
  `previousReserver == GameManager.LocalPlayerInfo` (`null == null` → true), enters the
  branch, and NREs on `LocalPlayerInventory.ItemUseCancelled`. Mirror catches the exception
  but the read pointer is now off → "GolfCartInfo OnDeserialize size mismatch" → client gets
  disconnected on rejoin. Skipping the reservation flow entirely (the cart goes straight
  from `Instantiate` → `NetworkServer.Spawn` → `ServerEnter`) keeps the SyncVar at `null` so
  joining clients never see a non-default value, and `ServerTryAssignPassengerToSeat` already
  handles authority assignment when the player takes seat 0.

## v0.3.1

- **Fix the host crashing to lobby on summon.** v0.3.0 hooked
  `NetworkClient.OnConnectedEvent += OnClientConnected` in `Plugin.Awake` to register the
  custom `ForeverGolfCartTintMsg` handler. But Mirror's `NetworkManager.Awake` runs *after*
  plugin Awake and assigns the same delegate (`OnConnectedEvent = OnClientConnectInternal`),
  silently wiping the subscription. So when the host's tint broadcast looped back through
  its own local-loopback connection, the client side had no handler for the message id →
  Mirror logged "Unknown message id: 14576" → "NetworkClient: failed to unpack and invoke
  message. Disconnecting." → the player got bounced to main menu. v0.3.1 polls in `Update`
  instead and re-registers handlers whenever NetworkServer/NetworkClient becomes active.
- **Actually install the `OnStartClient` postfix.** Same Harmony placement bug we hit in
  InAirItems / DrivingRangeItems: `[HarmonyPatch]` was on the postfix method instead of a
  type, so `PatchAll()` skipped it. The late-arrival tint path that fires when the tint
  message lands before the cart spawn replicates was dead code in 0.3.0. Wrapped in a nested
  class so it actually runs.

## v0.3.0

- Fix the pastel tint not actually applying. v0.2.0 gated the OnStartClient hook behind
  `ServerTrackedCarts`, which the host populated AFTER `NetworkServer.Spawn` already
  triggered the host's own client init — so the gate's first run always missed. Now the
  server applies the tint directly post-spawn (with a 6-frame re-apply to defeat cosmetic
  overlays), then broadcasts a `ForeverGolfCartTintMsg{netId}` to every client. Clients
  add the netId to a local set and apply on receipt or when `OnStartClient` first sees the
  cart, whichever lands first.
- Switched tint application from `MaterialPropertyBlock` to direct material-instance edits
  (`r.materials[i].color *= tint`). MaterialPropertyBlock was being overwritten by the
  game's cosmetic overlays on seat-entry; instance edits stick.

## v0.2.0

- Fix the cart not actually appearing when Q is held. The 0.1.0 server flow only reserved
  the driver seat and never asked the player to enter, so vanilla's
  `ServerReserveDriverSeatPreNetworkSpawn` 5 s "unused cart destroy" timer cleaned the cart
  up before it spawned visibly. Now invokes `GolfCartInfo.ServerEnter(target)` directly via
  reflection right after `NetworkServer.Spawn`, so the requesting player is seated as the
  driver immediately.
- Add `Diagnostics.VerboseLogging` (default false) for remote diagnosis of summon failures.

## v0.1.0

- Initial release. Hold Q (configurable) to summon a pastel-tinted cart with auto driver entry.
  Empty carts despawn on exit.
