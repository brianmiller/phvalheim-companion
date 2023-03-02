# PhValheim Server Side Companion

phvalheim-companion is a custom mod for Valheim servers backed by PhValheim (phvalheim-server). This is a server-side mod that is automatically added to all deployed PhValheim worlds >=phvalheim-server 2.4.

## Summary

- This mod is intended to connect in-game telemetry with PhValheim's backend. The idea is to capture events and other statistics about the running world, e.g., server start, server stop, player join, player disocnnect, player death, etc...
- The codebase for this mod was forked from Aaron Scherer [valheim-discord-notifier](https://github.com/aequasi/valheim-discord-notifier). All external functionality of this forked mod has been disabled until it's needed futher.  Internal event handlers were used to tie into PhValheim's backend.  I learned alot about HTTP POST within C# from this code base. Thank you, @aequasi!

Current capability: output from game console
- On server start<br>
`PhValheim Companion: [Sent] {"action":"start","world":"mytestworld22"}`<br>
`PhValheim Companion: [Received] Backend response:OK`<br>
- On player join<br>
`PhValheim Companion: [Sent] {"action":"join","world":"mytestworld22","citizen":"Boogeyman"}`<br>
`PhValheim Companion: [Received] Backend response:OK`<br>

The `Backend response: OK` is PhValheim's backend returning a result back to game.

This mod and the new changes to phvalheim-server >=2.4 lay the foundation for advanced bi-directional interaction between a running world and PhValheim.

Next steps:
- Capture all players that join a world and store them in PhValheim's database.  These players will be listed (mouse hover) on the public interface so folks will be able to identify which character they used in the world.
- Capture player statistics. E.g., builds and deaths
- Capture progression of the world. E.g., No hung heads at the shrines = copper/tin age, A hung Eikthyr head = bronze age and so forth. DONE!

See https://github.com/brianmiller/phvalheim-server for more information regarding the main project.

## Changelog

- 1.0.1 A significant and new capability was added to phvalheim-client, phvalheim-server and phvalheim-companion, allowing in-game client notifications to be sent back to the originating PhValheim server. The first feature that uses this new method detects world progression. E.g., When a boss head is hung at the spawn shrines, PhValheim Server will now display icons on the public interface indicating progression.
- 1.0.0 Initial release sends world start and player join to the backend.