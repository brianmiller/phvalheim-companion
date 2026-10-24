using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using BepInEx;
using UnityEngine;

namespace PhValheimCompanion
{
    // Server-side player telemetry (2.58).
    //
    // WHY THIS EXISTS. PhValheim's player count used to be scraped out of each world's log:
    // a PlayFab "now N player(s)" line on crossplay worlds, a ten-minute "Connections N"
    // heartbeat plus socket events on the rest. It was wrong often enough to be switched off
    // by default -- a count from before a restart survived the restart, a crossplay world read
    // 0 with a player on it -- and the maintainer's verdict was: real data or nothing. The
    // dedicated server knows exactly who is connected; this writes that down.
    //
    // WHAT IT WRITES. Every FlushInterval seconds on a DEDICATED SERVER only:
    //   <BepInEx>/data/PhValheim.Companion/player_stats.json
    //   {
    //     "timestamp_utc": "2026-10-23T18:00:00.0000000Z",
    //     "source": "companion", "version": "1.0.5",
    //     "player_count": 2,
    //     "players": [ { "name": "Brian", "character_id": "12:34" }, ... ]
    //   }
    // The list is ZNet.GetPlayerList(): the server's own roster, the one it broadcasts to every
    // peer, so it counts players who have a character in the world -- not a client still on the
    // loading screen, and never the server itself (a dedicated server has no local player).
    //
    // HOW IT IS READ. playerMonitor (engine/tools) reads the file for each running world and
    // treats it as current only while its mtime is fresh; a world whose server is gone leaves
    // a stale file, which reads as "unknown", never as a confident zero. The same shape and
    // location as PhValheim.TickMonitor's tick_stats.json, which the health bar reads.
    //
    // CLIENT SIDE. Nothing. ZNet.IsDedicated() is false on a client and on a player-hosted
    // server, and this plugin is dual-role (the server tree is the client tree), so the gate is
    // checked on every tick rather than once at Awake -- ZNet does not exist yet at Awake.
    //
    // The write is atomic (temp file, then File.Copy over the target): the reader is a shell
    // script on a 2-minute cron and must never see half a JSON document.
    public class PlayerTelemetry : MonoBehaviour
    {
        public const string DataDirName = "PhValheim.Companion";
        public const string FileName = "player_stats.json";
        private const float FlushInterval = 5f;

        private float _timer = 0f;
        private string _dir = string.Empty;
        private bool _announced = false;

        private void Awake()
        {
            _dir = Path.Combine(Paths.BepInExRootPath, "data", DataDirName);
        }

        private void Update()
        {
            _timer += Time.unscaledDeltaTime;
            if (_timer < FlushInterval) return;
            _timer = 0f;

            ZNet znet = ZNet.instance;
            if (znet == null || !znet.IsServer() || !znet.IsDedicated()) return;

            List<ZNet.PlayerInfo> players;
            try
            {
                players = znet.GetPlayerList();
            }
            catch (Exception ex)
            {
                Main.StaticLogger?.LogWarning($"PlayerTelemetry: could not read the player list: {ex.Message}");
                return;
            }

            var sb = new StringBuilder();
            sb.Append("{\n");
            sb.Append("  \"timestamp_utc\": \"").Append(DateTime.UtcNow.ToString("o")).Append("\",\n");
            sb.Append("  \"source\": \"companion\",\n");
            sb.Append("  \"version\": \"").Append(Main.VERSION).Append("\",\n");
            sb.Append("  \"player_count\": ").Append(players.Count).Append(",\n");
            sb.Append("  \"players\": [");
            for (int i = 0; i < players.Count; i++)
            {
                if (i > 0) sb.Append(",");
                sb.Append("\n    { \"name\": \"").Append(JsonEscape(players[i].m_name))
                  .Append("\", \"character_id\": \"").Append(JsonEscape(players[i].m_characterID.ToString()))
                  .Append("\" }");
            }
            if (players.Count > 0) sb.Append("\n  ");
            sb.Append("]\n}\n");

            try
            {
                Directory.CreateDirectory(_dir);
                string target = Path.Combine(_dir, FileName);
                string tmp = target + ".tmp";
                File.WriteAllText(tmp, sb.ToString());
                File.Copy(tmp, target, true);
                File.Delete(tmp);
                if (!_announced)
                {
                    _announced = true;
                    Main.StaticLogger?.LogInfo($"PlayerTelemetry: writing {target} every {FlushInterval:F0}s");
                }
            }
            catch (Exception ex)
            {
                Main.StaticLogger?.LogWarning($"PlayerTelemetry: could not write {FileName}: {ex.Message}");
            }
        }

        // Player names are user input. A quote or a backslash in one must not break the document
        // the engine parses with jq.
        internal static string JsonEscape(string s)
        {
            if (string.IsNullOrEmpty(s)) return string.Empty;
            var sb = new StringBuilder(s.Length + 8);
            foreach (char c in s)
            {
                switch (c)
                {
                    case '"': sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    default:
                        if (c < 0x20) sb.Append("\\u").Append(((int)c).ToString("x4"));
                        else sb.Append(c);
                        break;
                }
            }
            return sb.ToString();
        }
    }
}
