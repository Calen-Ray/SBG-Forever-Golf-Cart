# ForeverGolfCart

Hold **Q** to summon a golf cart — every player with the mod can do this on demand instead of
hunting for the rare cart pickup. The cart appears at your feet, you're seated as the driver
automatically, and the moment you step out of an empty cart it despawns so the world doesn't
fill up with abandoned carts.

The summoned cart is tinted with a random pastel hue so you can tell it's not a vanilla
spawn at a glance.

## Configuration

`BepInEx/config/sbg.forevergolfcart.cfg`:

- `Spawn.HotkeyHoldDuration` — seconds you have to hold the hotkey before the cart spawns
  (default `0.4`).
- `Spawn.SummonKey` — `KeyCode` for the summon button (default `Q`).
- `Visuals.PastelTintEnabled` — toggle the tint (default `true`).

## Limitations / known gaps in v0.1

- True rebinding through the in-game controls menu isn't wired in — `KeyCode` config only.
- Pastel tint is computed locally at spawn time; modded clients will see slightly different
  hues unless someone wires up a synced color message.

## Build / deploy / release

```bash
dotnet build -c Release
pwsh tools/package.ps1
gh release create vX.Y.Z artifacts/Cray-ForeverGolfCart-<ver>.zip --notes-file CHANGELOG.md
```
