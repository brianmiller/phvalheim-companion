using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace PhValheimCompanion
{
    // What the PhValheim client told us to join, and what is installed to join it with.
    //
    // The payload arrives on Valheim's own command line as
    //
    //     --phvalheim-launch <base64 of the Launch! link's body>
    //
    // which is the same base64 the client itself was handed. Two consequences are deliberate:
    //
    //   * It comes from the link, not from a file on disk. A crossplay world reissues its join
    //     code every time it restarts, so a cached copy goes stale silently and the player gets
    //     "server not found" with nothing on screen to explain it. The link is regenerated on
    //     every click, so it is right by construction. That is the quick_connect_servers.cfg
    //     bug not being repeated.
    //
    //   * Its presence IS the gate. No payload means the game was not started by the PhValheim
    //     client, so the dialog must not appear -- a player launching Valheim from Steam to
    //     play single-player should see no trace of this. There is no second "was I launched by
    //     PhValheim?" check to get out of step with the transport, because they are the same
    //     fact.
    //
    // argv rather than an environment variable: on Linux the client execs valheim.x86_64
    // directly and env would survive, but the Windows path goes through Steam's -applaunch,
    // where it does not. argv is the one mechanism that works everywhere.
    public sealed class LaunchPayload
    {
        public const string ArgName = "--phvalheim-launch";

        public string World { get; private set; }
        public string Password { get; private set; }
        public string Host { get; private set; }
        public string Port { get; private set; }
        public string PhValheimHost { get; private set; }
        public string HttpScheme { get; private set; }
        public bool IsVanilla { get; private set; }
        public bool IsCrossplay { get; private set; }
        public string JoinCode { get; private set; }

        // How this world will actually be joined.
        //
        // Derived ONCE, here, and read by both the dialog and ConnectFlow. The first version
        // let each of them work it out separately from IsCrossplay and JoinCode, and they
        // disagreed in front of Brian: the dialog announced "Joining host:port" on a world
        // that is crossplay. Two sides answering the same question from the same inputs is
        // still two places to be wrong, and the UI is the half that cannot be checked by a
        // test. Same lesson as the "restart pending" badge -- derive once, read twice.
        public enum JoinMethod { Address, JoinCode }

        public JoinMethod Method =>
            (IsCrossplay && !string.IsNullOrEmpty(JoinCode)) ? JoinMethod.JoinCode : JoinMethod.Address;

        // A crossplay world whose join code has not been published yet.
        //
        // This is the state that produced the wrong text, and it is not an error. Valheim
        // registers the lobby around 30 seconds after the server process starts, so a player
        // who updates a world, starts it and clicks Launch straight away gets a launch link
        // with field 9 empty. The server is still reachable at gameDNS:port -- it is a
        // dedicated server either way -- so the join works. What must not happen is the dialog
        // claiming this is an ordinary address-only world, because that is the one case where
        // "connect by code" would have worked a minute later.
        public bool CrossplayCodeNotReady => IsCrossplay && string.IsNullOrEmpty(JoinCode);

        private static bool _parsed;
        private static LaunchPayload _current;

        // Parsed once. GetCommandLineArgs() cannot change during the process, and the dialog
        // and the reopen button both ask for this.
        public static LaunchPayload Current
        {
            get
            {
                if (!_parsed)
                {
                    _parsed = true;
                    _current = Parse(Environment.GetCommandLineArgs());
                }
                return _current;
            }
        }

        public static bool Present => Current != null;

        // Internal so the test harness can feed it an argv array without starting a game.
        internal static LaunchPayload Parse(string[] argv)
        {
            if (argv == null) return null;

            string encoded = null;
            for (int i = 0; i < argv.Length; i++)
            {
                if (argv[i] != ArgName) continue;
                if (i + 1 < argv.Length) encoded = argv[i + 1];
                break;
            }

            if (string.IsNullOrEmpty(encoded)) return null;

            string decoded;
            try
            {
                decoded = Encoding.UTF8.GetString(Convert.FromBase64String(encoded));
            }
            catch (Exception e)
            {
                // Loud, because the alternative is a player staring at a menu with no dialog
                // and no idea why. This is a client/server version mismatch, not player error.
                Main.StaticLogger.LogError($"{ArgName} is not valid base64 ({e.GetType().Name}). The PhValheim client and server versions may not match. No connect dialog will be shown.");
                return null;
            }

            // Field order is fixed by phvBuildLaunchString() in the server's db_gets.php:
            //
            //   0 "launch"  1 world  2 password  3 gameDNS  4 port
            //   5 phvalheimHost  6 httpScheme  7 vanilla  8 crossplay  9 joinCode
            //
            // Fields 8 and 9 arrived in server 2.53. They are read defensively for the same
            // reason the client reads field 7 defensively: a current client may be pointed at
            // an older server, which sends a shorter string. Absent crossplay means not
            // crossplay, which is what every pre-2.53 world is.
            //
            // Known limitation, shared with phvalheim-client's own parser: the separator is '?'
            // and a world password containing '?' would shift every field after it. Fixing that
            // means changing the wire format on both sides, so both parsers have the same bug
            // rather than two different ones.
            var f = decoded.Split('?');

            if (f.Length < 7 || f[0] != "launch")
            {
                Main.StaticLogger.LogError($"{ArgName} decoded to something unexpected ({f.Length} fields, first is \"{(f.Length > 0 ? f[0] : "")}\"). No connect dialog will be shown.");
                return null;
            }

            var p = new LaunchPayload
            {
                World = f[1],
                Password = f[2],
                Host = f[3],
                Port = f[4],
                PhValheimHost = f[5],
                HttpScheme = f[6],
                IsVanilla = f.Length >= 8 && f[7] == "1",
                IsCrossplay = f.Length >= 9 && f[8] == "1",
                JoinCode = f.Length >= 10 ? f[9] : ""
            };

            Main.StaticLogger.LogMessage($"PhValheim launch payload: world=\"{p.World}\" host={p.Host}:{p.Port} vanilla={p.IsVanilla} crossplay={p.IsCrossplay} joinCode={(string.IsNullOrEmpty(p.JoinCode) ? "(none)" : "present")}");
            return p;
        }

        // The mod list shown in the dialog comes from this machine's plugins directory, not
        // from anything the server said. The server knows what it INTENDED to send; the
        // directory is what the player will actually be running, and if the client's sync
        // half-finished those two differ. Showing the second one is the only version that can
        // be trusted.
        public static List<string> InstalledPlugins()
        {
            var names = new List<string>();

            string pluginsDir;
            try
            {
                pluginsDir = BepInEx.Paths.PluginPath;
            }
            catch (Exception e)
            {
                Main.StaticLogger.LogWarning($"Could not locate the BepInEx plugin path ({e.GetType().Name}); the dialog will not list mods.");
                return names;
            }

            if (string.IsNullOrEmpty(pluginsDir) || !Directory.Exists(pluginsDir))
            {
                Main.StaticLogger.LogWarning($"Plugin path \"{pluginsDir}\" does not exist; the dialog will not list mods.");
                return names;
            }

            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            // Mods unzip either into plugins/<Author-Mod>/ or as a loose dll directly in
            // plugins/, depending on how the author packaged the zip. Both shapes are normal
            // and both have to be counted, or the list silently under-reports.
            try
            {
                foreach (var dir in Directory.GetDirectories(pluginsDir))
                {
                    var name = Path.GetFileName(dir);
                    if (IsSelf(name)) continue;
                    if (seen.Add(name)) names.Add(Prettify(name));
                }

                foreach (var file in Directory.GetFiles(pluginsDir, "*.dll"))
                {
                    var name = Path.GetFileNameWithoutExtension(file);
                    if (IsSelf(name)) continue;
                    if (seen.Add(name)) names.Add(Prettify(name));
                }
            }
            catch (Exception e)
            {
                Main.StaticLogger.LogWarning($"Could not read the plugin directory ({e.GetType().Name}); the mod list may be incomplete.");
            }

            names.Sort(StringComparer.OrdinalIgnoreCase);
            return names;
        }

        // The Companion is not a mod the player chose, so listing it is noise. It also ships
        // inside the server image rather than from a catalogue, so it will never be in the
        // world's mod selection -- showing it would make the list disagree with the admin UI.
        private static bool IsSelf(string name)
        {
            return name.IndexOf("PhValheimCompanion", StringComparison.OrdinalIgnoreCase) >= 0
                || name.IndexOf("phvalheim-companion", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        // Thunderstore and Hexium both name directories "Owner-Mod_Name". Players recognise
        // the mod, not the owner, so lead with the mod and keep the owner for disambiguation.
        //
        // Plain text, no markup: the dialog strips angle brackets out of everything that came
        // from outside the plugin, so any tag added here would be stripped back out again.
        // Styling belongs in the dialog, next to the rest of the formatting.
        private static string Prettify(string raw)
        {
            var dash = raw.IndexOf('-');
            if (dash <= 0 || dash == raw.Length - 1) return raw.Replace('_', ' ');

            var owner = raw.Substring(0, dash);
            var mod = raw.Substring(dash + 1).Replace('_', ' ');
            return $"{mod}  ({owner})";
        }
    }
}
