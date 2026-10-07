using System;
using System.Collections;
using UnityEngine;

namespace PhValheimCompanion
{
    // Joining the world the PhValheim client launched us for.
    //
    // The whole design rests on one thing found by reading FejdStartup's IL rather than
    // guessing: Valheim ALREADY knows how to "join this server, but pick a character first".
    // That is what the server browser does, and it is one private method:
    //
    //     void ProceedJoinRequest(ServerJoinData joinData) {
    //         if (!VerifyHasMultiplayerPrivilege(...)) return;
    //         if (CinematicsManager.IsStartedPlaying()) CinematicsManager.Stop();
    //         m_queuedJoinServer = joinData;
    //         if (m_serverListPanel.activeInHierarchy) {     // already past character select
    //             m_joinServer = m_queuedJoinServer;
    //             m_queuedJoinServer = ServerJoinData.None;
    //             JoinServer();
    //         } else {
    //             HideAll();
    //             ShowCharacterSelection();                  // <-- our path
    //         }
    //     }
    //
    // and OnCharacterStart() finishes it:
    //
    //     SelectCharacter(profile.GetFilename(), profile.m_fileSource);
    //     m_characterSelectScreen.SetActive(false);
    //     if (m_queuedJoinServer.IsValid) {
    //         m_joinServer = m_queuedJoinServer;
    //         m_queuedJoinServer = ServerJoinData.None;
    //         JoinServer();
    //     }
    //
    // So Brian's spec -- Connect, then character select, then connect automatically -- is
    // vanilla behaviour reached from a different button. There is NO Harmony patch on
    // OnCharacterStart and nothing reimplemented. Fewer moving parts, and it keeps working
    // through a Valheim update that changes how joining works internally.
    //
    // Two notes on what NOT to do, both of which were the plan before the IL was read:
    //
    //   * SetServerToJoin() is the wrong door. It writes m_joinServer, and OnCharacterStart
    //     overwrites m_joinServer from m_queuedJoinServer -- which would still be None. The
    //     player would select a character and land in the world list. The field that matters
    //     is m_queuedJoinServer, which only ProceedJoinRequest sets correctly.
    //
    //   * Valheim's own -joincode handler (AutoJoinServer) resolves the code and then calls
    //     JoinServer() directly, with no SelectCharacter() anywhere. That is exactly why
    //     -joincode drops you in as "Odev (Developer)". We reuse its join-code RESOLUTION and
    //     throw away its join, routing the resolved server through ProceedJoinRequest instead.
    internal static class ConnectFlow
    {
        // Set once the player has committed, so the dialog does not come back and the reopen
        // button disappears. Not a lock -- it is UI state, read on the main thread only.
        internal static bool Connecting;

        // When Connecting was last set true, so the watchdog below can tell "the join is in
        // flight" from "the join died and we are back where we started".
        private static float _connectingSince;

        // Whether THIS attempt ever actually got the player into the world.
        //
        // Nothing cleared Connecting on success -- Begin set it true and only Fail set it back
        // -- so after a good join it stayed true for the rest of the session. The watchdog below
        // sat armed the whole time and was silent only because the main menu was not up. The
        // moment the player deliberately disconnected, the menu came back, the watchdog saw
        // "Connecting, and we are at the main menu" and reported a connection failure for a
        // disconnect the player had asked for.
        //
        // The watchdog's premise -- menu up while connecting means the join died -- is only true
        // if the join never landed. That was never recorded, so the two cases were
        // indistinguishable. This is the missing bit of state, not an extra guard on top.
        private static bool _joined;

        // Whether THIS attempt got as far as the character select screen.
        //
        // The THIRD case, and the reason the watchdog was still lying. Look at what
        // ProceedJoinRequest actually does, quoted at the top of this file: it sets
        // m_queuedJoinServer and then calls ShowCharacterSelection(). NOTHING HAS TOUCHED THE
        // NETWORK YET -- JoinServer() only runs later, inside OnCharacterStart(), once the
        // player has picked a character.
        //
        // So a player who clicks Connect, sees character select, and clicks Back has cancelled
        // before any connection was attempted. The watchdog saw main-menu-up with _joined
        // false, concluded the join had died, and told them "Could not connect. The world may
        // still be starting." -- a diagnosis of a connection that was never made, about a
        // server whose state we had not asked.
        //
        // Observed from ConnectDialog.Update, which is correct HERE and was wrong for _joined:
        // the character select screen belongs to FejdStartup, which is still alive while it is
        // up. The scene change that destroys the dialog's GameObject is the one _joined needed
        // JoinSentry for.
        private static bool _reachedCharacterSelect;

        // Called by JoinSentry, which is the only thing alive in the game scene to call it.
        internal static void NoticeJoined()
        {
            if (_joined) return;
            _joined = true;
            Main.StaticLogger.LogMessage("The join landed: the player is in the world.");
        }

        // Called every frame from ConnectDialog.Update while a join is in flight.
        internal static void NoticeCharacterSelect(bool characterSelectActive)
        {
            if (!characterSelectActive || _reachedCharacterSelect) return;
            _reachedCharacterSelect = true;
            Main.StaticLogger.LogMessage(
                "Character select is up: the join is queued and waiting for the player to pick a character.");
        }

        // Why the last attempt failed, in words a player can act on, or null if there has not
        // been one. The dialog shows it when it comes back.
        //
        // Every failure path used to end at Main.StaticLogger and nowhere else, so the player
        // saw the dialog silently reappear with no idea whether they had misclicked, whether
        // the server was down, or whether the mod was broken. A log file is not a user
        // interface. The strings here are deliberately about what to DO next, not about what
        // threw -- the exception detail still goes to the log for me.
        internal static string LastFailure { get; private set; }

        // The same channel for something that is NOT a failure.
        //
        // Kept separate from LastFailure rather than reusing it with softer wording, because
        // the dialog styles a failure as a failure and the two are read differently -- and
        // because one bug here has already been "a state that doubled as another state". A
        // cancel sets this; a real failure sets LastFailure; a clean disconnect sets neither.
        internal static string LastNotice { get; private set; }

        private static void Fail(string playerMessage, string logDetail = null)
        {
            LastFailure = playerMessage;
            LastNotice = null;   // a failure is not also a cancel; never show both
            Connecting = false;
            Main.StaticLogger.LogError(logDetail ?? playerMessage);
        }

        // How long to wait for a PlayFab login before giving up on a join code. Valheim's own
        // AutoJoinServer waits on the LoginFinished event; polling is used here instead so a
        // change to that event's shape cannot break us, and so there is a bound on the wait
        // rather than a dialog that sits there forever.
        private const float PlayFabLoginTimeoutSeconds = 20f;

        // How long to wait for Valheim to turn gameDNS into an address. A BackgroundWorker
        // doing a DNS lookup; seconds is generous and the bound matters more than the value.
        private const float DnsTimeoutSeconds = 15f;

        // ProceedJoinRequest takes the main menu down on the same frame it is called, so the
        // menu being up again means the join came back. The grace period only has to cover
        // that handover, not the join itself.
        private const float MenuReturnGraceSeconds = 2f;

        // Nothing inside Valheim tells us a join failed. ZNet can give up and put the player
        // back on the main menu without an exception, without a callback, and without a log
        // line we could hook -- which left Connecting true forever, and Connecting is what
        // suppresses both the dialog and the reopen button. The result was Brian's report:
        // a failed IP:PORT join with no dialog and no way back short of restarting the game.
        //
        // So the menu being up is itself the signal. Called every frame from
        // ConnectDialog.Update while Connecting is true; returns true on the frame it gives
        // up, so the dialog can put itself back.
        internal static bool NoticeMainMenu(bool mainMenuActive)
        {
            return Decide(mainMenuActive, Time.realtimeSinceStartup);
        }

        // The decision, with the clock passed in.
        //
        // Split out so it can be RUN outside Unity. This three-way call has been wrong three
        // times -- a disconnect reported as a failure, then a cancel reported as a failure --
        // and none of those was visible to any test, because touching
        // Time.realtimeSinceStartup from a harness throws "ECall methods must be packaged
        // into a system module": it is an extern in the reference assembly. One parameter buys
        // the whole decision table an oracle. See dev_tools/test-connect-outcomes.sh.
        internal static bool Decide(bool mainMenuActive, float now)
        {
            if (!Connecting) return false;

            // NOTE: _joined is NOT observed here, and must not be. This method only runs from
            // ConnectDialog.Update, which lives on FejdStartup's GameObject and is destroyed on
            // the way into a world -- so a check placed here cannot run during the only window
            // in which the join can be seen to have landed. That was the first version of this
            // fix and it shipped doing nothing. JoinSentry does the observing, from an object
            // that survives the scene change, and calls NoticeJoined().
            if (!mainMenuActive) return false;
            if (now - _connectingSince < MenuReturnGraceSeconds) return false;

            if (_joined)
            {
                // The player got in, played, and left. That is a disconnect, not a failure, and
                // calling it one told Brian his own deliberate logout had failed to connect.
                // The attempt is over either way, so Connecting is cleared and the dialog is
                // offered again -- rejoining is the likely next move -- but with NO notice.
                Connecting = false;
                LastFailure = null;
                LastNotice = null;
                Main.StaticLogger.LogMessage(
                    "Back at the main menu after a successful join: treating it as a disconnect, not a failure.");
                return true;
            }

            if (_reachedCharacterSelect)
            {
                // CANCELLED, and reporting it as a failure was a claim about a connection that
                // had not been attempted: ProceedJoinRequest only queues the join and shows
                // character select, and JoinServer() does not run until OnCharacterStart().
                // Back from that screen means the player changed their mind, and the honest
                // answer names the step they stopped at rather than blaming the server.
                //
                // Not silent, unlike the disconnect case above. There the player knows what
                // they did; here they pressed Back and the dialog reappearing with no
                // explanation reads as the mod having failed at something.
                Connecting = false;
                LastFailure = null;
                // 48 chars. The dialog clips this line at FailureLineBudgetChars (56) with an
                // ellipsis, so a fuller sentence does not get a smaller font -- it gets cut
                // off mid-word. The reason they are back here is on screen behind the dialog;
                // what they need from this line is the one action that joins.
                LastNotice = "Cancelled. Click Connect, then pick a character.";
                Main.StaticLogger.LogMessage(
                    "Back at the main menu from character select without joining: the player cancelled, "
                    + "so no connection was ever attempted and none is being reported as failed.");
                return true;
            }

            // The player gets told. This is the path Brian hit: Valheim gives up on its own,
            // with no exception and no callback, and the dialog used to just reappear as if
            // nothing had happened.
            //
            // Reachable only AFTER a character was picked, since the cancel branch above takes
            // every other route back to the menu -- so "the world may still be starting" is
            // now a statement about a join that really was attempted.
            Fail("Could not connect. The world may still be starting.",
                 "The join did not go through -- Valheim returned to the main menu by itself.");
            return true;
        }

        public static void Begin(LaunchPayload payload)
        {
            var fejd = FejdStartup.instance;
            if (fejd == null)
            {
                Main.StaticLogger.LogError("Cannot connect: FejdStartup.instance is null. Join from the server list instead.");
                return;
            }

            Connecting = true;
            _connectingSince = Time.realtimeSinceStartup;
            LastFailure = null;   // this attempt has not failed yet; do not show the last one
            LastNotice = null;    // nor the last cancel
            // Per ATTEMPT, not per session. Left set from a previous successful join, a later
            // join that genuinely failed would be read as a disconnect and reported as nothing
            // at all -- the same bug as today with the sign flipped.
            _joined = false;
            // Same reason, same trap in the other direction: left set from a previous attempt
            // that stopped at character select, a join that later died on the network would be
            // reported as "you cancelled" and the player would be told nothing was wrong.
            _reachedCharacterSelect = false;

            // ZNet.RPC_ClientHandshake reads FejdStartup.ServerPassword during the handshake,
            // so setting it here is what stops the password prompt appearing for a world the
            // player already authenticated to in the PhValheim UI. Modded worlds send an empty
            // password and are gated by the CITIZENS list instead; an empty string is correct
            // for them, not a missing value.
            SetServerPassword(payload.Password ?? "");

            if (payload.IsCrossplay && !string.IsNullOrEmpty(payload.JoinCode))
            {
                Main.StaticLogger.LogMessage($"Connecting to \"{payload.World}\" by crossplay join code.");
                fejd.StartCoroutine(JoinByCode(fejd, payload));
            }
            else
            {
                Main.StaticLogger.LogMessage($"Connecting to \"{payload.World}\" at {payload.Host}:{payload.Port}.");
                JoinByAddress(fejd, payload);
            }
        }

        private static void JoinByAddress(FejdStartup fejd, LaunchPayload payload)
        {
            fejd.StartCoroutine(JoinByAddressRoutine(fejd, payload));
        }

        // Why this is a coroutine, and why it resolves the name before handing the join over.
        //
        // FejdStartup.JoinServer()'s dedicated-server branch is this, decompiled from a real
        // client:
        //
        //     ZNet.ResetServerHost();
        //     MultiBackendMatchmaking.GetServerIPAsync(serverJoin, delegate(bool ok, IPv6Address? a) {
        //         ... ZNet.SetServerHost(...);                 // <-- the host is set HERE
        //     });
        //     flag = true;
        //     ...
        //     TransitionToMainScene();                         // <-- and the scene loads HERE
        //
        // Nothing waits for that callback. Whether the join works comes down entirely to
        // whether GetServerIPAsync answers synchronously, and it only does so when the address
        // is already known:
        //
        //     if (server.TryGetIPAddress(out var address)) completedHandler?.Invoke(true, address);
        //     else s_instance.m_dnsResolver.ResolveDomainNameAsync(server.m_host, completedHandler);
        //
        // ...and ResolveDomainNameAsync answers synchronously only on a DNS cache hit;
        // otherwise it runs a BackgroundWorker. So on a cold cache, TransitionToMainScene()
        // runs with the server host still reset, and the player lands back on the main menu
        // with no error. That is Brian's IP:PORT bug, in full.
        //
        // The server list does not hit this because populating it resolves every entry, so by
        // the time anyone presses Join the cache is warm and vanilla's own call is synchronous.
        // We arrive with a cold cache because we skip the list entirely.
        //
        // The fix is therefore to warm the cache and nothing more: call Valheim's OWN resolver,
        // wait for it here where waiting is allowed, then hand over an unchanged join. There is
        // no reimplementation of the join, no second code path, and the address format stays
        // whatever Valheim decided it should be.
        //
        // Doing it this way also sidesteps a landmine in that callback: on a failed resolve it
        // sets retries = 50 and then dereferences address.Value anyway, throwing inside
        // Valheim's own code. Resolving up front means a dead name is reported here, with the
        // dialog still on screen, instead of as a NullReferenceException.
        //
        // NOTE the earlier comment in this file claimed gameDNS "can be handed over as-is and
        // no lookup is needed here". That was true of what the API accepts and wrong about
        // what it does.
        private static IEnumerator JoinByAddressRoutine(FejdStartup fejd, LaunchPayload payload)
        {
            if (!ushort.TryParse(payload.Port, out ushort port))
            {
                Fail("Bad port in the launch link. Re-launch from PhValheim.",
                     $"Cannot connect: \"{payload.Port}\" is not a valid port.");
                yield break;
            }

            if (string.IsNullOrEmpty(payload.Host))
            {
                Fail("No server address in the launch link. Re-launch it.",
                     "Cannot connect: the launch payload carried no host.");
                yield break;
            }

            ServerJoinDataDedicated dedicated;
            ServerJoinData data;
            try
            {
                dedicated = new ServerJoinDataDedicated(payload.Host, port);
                data = new ServerJoinData(dedicated);
            }
            catch (Exception e)
            {
                Fail($"Valheim would not accept the address {payload.Host}:{port}.",
                     $"Cannot connect: Valheim rejected {payload.Host}:{port} ({e.GetType().Name}: {e.Message}).");
                yield break;
            }

            bool answered = false;
            bool resolved = false;

            try
            {
                MultiBackendMatchmaking.GetServerIPAsync(dedicated, (succeeded, address) =>
                {
                    answered = true;
                    resolved = succeeded && address.HasValue;
                });
            }
            catch (Exception e)
            {
                // The resolver is a convenience we are borrowing, not the join itself. If this
                // Valheim build has moved it, going straight to ProceedJoinRequest is exactly
                // the behaviour we had before this method existed -- which worked for a literal
                // IP and for a warm cache. Better than refusing to connect at all.
                Main.StaticLogger.LogWarning($"Could not pre-resolve \"{payload.Host}\" ({e.GetType().Name}: {e.Message}); handing the join straight to Valheim.");
                Proceed(fejd, data);
                yield break;
            }

            float waited = 0f;
            while (!answered && waited < DnsTimeoutSeconds)
            {
                waited += Time.deltaTime;
                yield return null;
            }

            if (!answered)
            {
                Fail($"Could not look up {payload.Host}. Check your connection.",
                     $"Cannot connect: looking up \"{payload.Host}\" did not finish within {DnsTimeoutSeconds:0}s.");
                yield break;
            }

            if (!resolved)
            {
                Fail($"{payload.Host} could not be found. Ask your admin to check Game DNS.",
                     $"Cannot connect: \"{payload.Host}\" could not be resolved to an address.");
                yield break;
            }

            // One more frame, so Valheim's resolver has finished writing its cache entry before
            // JoinServer reads it. SetCacheEntry runs before our callback in the build this was
            // read from, which makes this belt-and-braces rather than load-bearing -- but the
            // ordering is incidental to that implementation and a frame costs nothing.
            yield return null;

            Proceed(fejd, data);
        }

        private static IEnumerator JoinByCode(FejdStartup fejd, LaunchPayload payload)
        {
            // A join code is meaningless until PlayFab has logged in. On a cold start the
            // player can reach the main menu before that completes, so this has to be waited
            // for rather than assumed.
            float waited = 0f;
            while (!IsPlayFabLoggedIn() && waited < PlayFabLoginTimeoutSeconds)
            {
                waited += Time.deltaTime;
                yield return null;
            }

            if (!IsPlayFabLoggedIn())
            {
                Main.StaticLogger.LogWarning($"PlayFab did not finish logging in within {PlayFabLoginTimeoutSeconds:0}s, so the crossplay join code could not be used. Falling back to {payload.Host}:{payload.Port}.");
                JoinByAddress(fejd, payload);
                yield break;
            }

            bool resolved = false;

            try
            {
                ZPlayFabMatchmaking.ResolveJoinCode(
                    payload.JoinCode,
                    serverData =>
                    {
                        resolved = true;
                        if (string.IsNullOrEmpty(serverData.remotePlayerId))
                        {
                            Main.StaticLogger.LogWarning($"Join code \"{payload.JoinCode}\" resolved but carried no host id. Falling back to {payload.Host}:{payload.Port}.");
                            JoinByAddress(fejd, payload);
                            return;
                        }

                        Main.StaticLogger.LogMessage($"Join code \"{payload.JoinCode}\" resolved to \"{serverData.serverName}\".");
                        Proceed(fejd, new ServerJoinData(new ServerJoinDataPlayFabUser(serverData.remotePlayerId)));
                    },
                    failReason =>
                    {
                        resolved = true;
                        // Falling back rather than stopping, because a dedicated PhValheim
                        // world is reachable at gameDNS:port whether or not it is also
                        // registered for crossplay. The join code is the nicer route, not the
                        // only one -- and a world that has just restarted may not have
                        // re-registered yet, which is the common case for this failing.
                        Main.StaticLogger.LogWarning($"Join code \"{payload.JoinCode}\" did not resolve ({failReason}). Falling back to {payload.Host}:{payload.Port}.");
                        JoinByAddress(fejd, payload);
                    });
            }
            catch (Exception e)
            {
                Main.StaticLogger.LogError($"Crossplay join-code lookup is unavailable in this Valheim build ({e.GetType().Name}: {e.Message}). Falling back to {payload.Host}:{payload.Port}.");
                JoinByAddress(fejd, payload);
                yield break;
            }

            // ResolveJoinCode is asynchronous and may never call back at all -- a dropped
            // request leaves the player on a menu with no explanation. Give it a bounded wait
            // and then take the other route.
            waited = 0f;
            while (!resolved && waited < PlayFabLoginTimeoutSeconds)
            {
                waited += Time.deltaTime;
                yield return null;
            }

            if (!resolved)
            {
                Main.StaticLogger.LogWarning($"Join code \"{payload.JoinCode}\" lookup never answered. Falling back to {payload.Host}:{payload.Port}.");
                JoinByAddress(fejd, payload);
            }
        }

        // ProceedJoinRequest is private on the assembly the game actually loads. We compile
        // against the publicized copy, where it is public, so calling it directly builds
        // cleanly and throws MissingMethodException at runtime. Reflection is the only form of
        // this call that survives contact with a real client.
        private static bool Proceed(FejdStartup fejd, ServerJoinData data)
        {
            if (Utils.InvokePrivate(fejd, "ProceedJoinRequest", data)) return true;

            Fail("Companion and Valheim versions do not match. Use the server list.",
                 "Could not hand the join to Valheim: ProceedJoinRequest is missing.");
            return false;
        }

        private static bool IsPlayFabLoggedIn()
        {
            try
            {
                return PlayFabManager.IsLoggedIn;
            }
            catch (Exception e)
            {
                // Treated as "logged in" so the caller stops waiting and the join code is
                // attempted anyway. If PlayFab really is unavailable the resolve fails and the
                // IP fallback runs -- whereas spinning here until timeout would reach the same
                // place far more slowly.
                Main.StaticLogger.LogWarning($"Could not read PlayFab login state ({e.GetType().Name}); attempting the join code regardless.");
                return true;
            }
        }

        private static void SetServerPassword(string password)
        {
            // Static auto-property; reflected for the same publicizer reason as the rest.
            try
            {
                var prop = typeof(FejdStartup).GetProperty("ServerPassword", Utils.BindFlags);
                if (prop != null && prop.CanWrite)
                {
                    prop.SetValue(null, password, null);
                    return;
                }

                var field = typeof(FejdStartup).GetField("<ServerPassword>k__BackingField", Utils.BindFlags);
                if (field != null)
                {
                    field.SetValue(null, password);
                    return;
                }

                // Non-fatal and worth saying out loud: the join still works, the player just
                // gets Valheim's password prompt they were not expecting.
                Main.StaticLogger.LogWarning("Could not pre-fill the server password; Valheim may ask for it.");
            }
            catch (Exception e)
            {
                Main.StaticLogger.LogWarning($"Could not pre-fill the server password ({e.GetType().Name}); Valheim may ask for it.");
            }
        }
    }
}
