# Herdkeeper

Version **0.1.0** for Valheim **1.0.20** with BepInEx 5.

Tamed animals keep breeding in their pens while nobody is nearby. In vanilla Valheim, animals only breed while a player is close enough for the area to be loaded. Herdkeeper lets the server apply the same breeding rules to tamed animals in unloaded areas, without them needing food.

**Status: Runtime-pending.** The breeding rules are covered by automated tests, but the mod has not yet been observed in a live game. Use it on a backed-up world.

## Supported Animals

| Animal | Offspring | Pen limit (adults + young) |
|---|---|---:|
| Wolf | Wolf cub | 4 within 10 m |
| Boar | Piglet | 5 within 10 m |
| Lox | Lox calf | 4 within 20 m |
| Moose | Moose calf | 5 within 10 m |

Hens and asksvin are not handled because they lay eggs, which only hatch in a loaded area anyway. The animals are read from the game at startup, and the server log lists exactly which ones are handled.

## How It Works

- Every vanilla rule applies: tamed partners close together, love points, pregnancy time, the pen limit and the parent's level for the offspring. Hunger is ignored while nobody is nearby.
- Young animals grow up when a player returns, counted from their birth.
- Animals near a player, or near the world center on a dedicated server, are left entirely to vanilla and still need food.
- No new network messages and no custom saved data. Removing the mod leaves ordinary vanilla animals.

## Installation

Install side: **server**. Put `Herdkeeper.dll` in `BepInEx/plugins/Herdkeeper/` on the dedicated server, or in the host's game for a listen server or single player. Clients, including console and crossplay players, need nothing.

## Configuration

`BepInEx/config/se.junge.valheim.herdkeeper.cfg`:

- `[General] Enabled = true` turns the mod on or off. Restart after editing the file.

## License

MIT. See `LICENSE`.
