# Herdkeeper: Runtime Acceptance

Status: **Runtime-pending**. No Herdkeeper game session has been observed. General method, environments and log locations: `docs/testing.md`.

Always use a backed-up or disposable copy of the world.

## Smallest Required Test

1. Start the dedicated server with only Herdkeeper enabled. Confirm `Breeding ...` lines for Wolf, Wolf_spiritcaller, Boar, Lox and Moose, `Not handling ...` lines for Hen and Asksvin, and no errors in `BepInEx/LogOutput.log`.
2. With an unmodded client, pen two tamed boars more than 300 m from the world center, note the count, and move at least 300 m away. Wait 15 minutes.
3. Return and confirm that tamed piglets have appeared, that the pen stopped at five (the vanilla limit) and that the piglets grow into tamed boars.

## Full Matrix

- Wolves, lox and moose; mixed pens; a pen within 20 m of another pen of the same species.
- Animals near the world center (simulated by the dedicated server itself): vanilla behavior only.
- Returning mid-pass and while a parent is pregnant; restarting the server while away; two players in different areas.
- Listen host and single player (the host's game is the server).
- A console or crossplay client joining; a client without the mod sees the same offspring.
- Removing the plugin: animals and offspring remain ordinary vanilla data.
- Large world: server frame time during discovery sweeps.

## Observation Log

| Date | Role | Valheim / BepInEx | Plugin SHA-256 | Result | Notes |
|---|---|---|---|---|---|
| | | | | | No observations yet. |
