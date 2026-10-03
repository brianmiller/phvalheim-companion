using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;

namespace PhValheimCompanion
{
    // What this world is, when nobody told us on the command line.
    //
    // WHY IT EXISTS
    // LaunchPayload is the whole truth when the PhValheim client passes --phvalheim-launch.
    // When it does not, the Companion used to know nothing at all: FejdStartupPatch returned
    // early, no dialog was attached, and a player whose client is older than the handoff got
    // silence. From 2.53 a world has a real password and no QuickConnect server-list entry, so
    // that silence is the difference between joining and not.
    //
    // The manifest is written by the SERVER into the client payload, next to this dll, by
    // writeClientManifest() in the engine's 0-functions.sh. It answers one question -- "which
    // world am I installed for, and what does it need?" -- and it arrives with the payload, so
    // an already-shipped client gets it on its next world update without being updated itself.
    //
    // IT CARRIES NO PASSWORD, DELIBERATELY.
    // The password is argv-only. This file sits on every player's disk for as long as the
    // install lasts; argv lives for one process. The dialog tells the player where to read the
    // password instead of keeping a copy of it here.
    //
    // WHAT IT CANNOT TELL US
    // Not the installed client's version. Nothing in the game process knows that -- the client
    // is a separate program that has already exited by the time Valheim starts. So "manifest
    // present, no payload" has TWO causes and cannot distinguish them:
    //
    //     1. the PhValheim client that launched this game is older than MinClientVersion
    //     2. any client installed this world, and the player started Valheim from Steam
    //
    // Every word the dialog says has to be true in both cases. That constraint is why the
    // notice describes the situation and the fix rather than accusing the player's client of
    // being out of date -- see ConnectDialog.BuildLaunchHelpBody.
    public sealed class ClientManifest
    {
        public const string FileName = "phvalheim-world.cfg";

        public string World { get; private set; }
        public string Host { get; private set; }
        public string Port { get; private set; }
        public bool IsVanilla { get; private set; }
        public bool IsCrossplay { get; private set; }
        public string MinClientVersion { get; private set; }
        public string ClientUrl { get; private set; }

        private static bool _loaded;
        private static ClientManifest _current;

        // Read once. The file cannot change while the game runs -- the client writes it before
        // Valheim starts and nothing in-process touches it.
        public static ClientManifest Current
        {
            get
            {
                if (!_loaded)
                {
                    _loaded = true;
                    _current = Load();
                }
                return _current;
            }
        }

        public static bool Present => Current != null;

        private static ClientManifest Load()
        {
            var path = Locate();
            if (path == null) return null;

            try
            {
                return FromLines(File.ReadAllLines(path), path);
            }
            catch (Exception e)
            {
                Main.StaticLogger.LogWarning($"Could not read the client manifest at \"{path}\" ({e.GetType().Name}); no launch help will be offered.");
                return null;
            }
        }

        // Next to this assembly first, because that is where the server writes it and the
        // directory name is therefore guaranteed to match.
        //
        // The PluginPath search is a fallback for the case where Location comes back empty --
        // BepInEx can load an assembly from a byte array, and then there is no file path to
        // take a directory from. Searching is cheap and runs once.
        private static string Locate()
        {
            try
            {
                var here = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
                if (!string.IsNullOrEmpty(here))
                {
                    var beside = Path.Combine(here, FileName);
                    if (File.Exists(beside)) return beside;
                }
            }
            catch (Exception e)
            {
                Main.StaticLogger.LogWarning($"Could not resolve this plugin's own directory ({e.GetType().Name}); falling back to a search of the plugin path.");
            }

            try
            {
                var plugins = BepInEx.Paths.PluginPath;
                if (string.IsNullOrEmpty(plugins) || !Directory.Exists(plugins)) return null;

                var hits = Directory.GetFiles(plugins, FileName, SearchOption.AllDirectories);
                return hits.Length > 0 ? hits[0] : null;
            }
            catch (Exception e)
            {
                Main.StaticLogger.LogWarning($"Could not search the plugin path for a client manifest ({e.GetType().Name}); no launch help will be offered.");
                return null;
            }
        }

        // Internal so a harness can parse a manifest without a game or a filesystem.
        //
        // Split on the FIRST '=' only. clientUrl is a URL and carries its own '=' signs in its
        // query string; splitting on every one of them truncated it to the scheme in the first
        // draft, which is the kind of bug that looks like a server-side typo.
        internal static ClientManifest FromLines(string[] lines, string sourceForLogs)
        {
            if (lines == null) return null;

            var kv = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var raw in lines)
            {
                if (raw == null) continue;
                var line = raw.Trim();
                if (line.Length == 0 || line[0] == '#') continue;

                var eq = line.IndexOf('=');
                if (eq <= 0) continue;

                kv[line.Substring(0, eq).Trim()] = line.Substring(eq + 1).Trim();
            }

            // The world name is the one field with no sane default: a notice that cannot say
            // which world it is about is worse than no notice, because the player cannot tell
            // whether it concerns the world they were trying to reach. Refuse instead.
            string world;
            if (!kv.TryGetValue("world", out world) || string.IsNullOrEmpty(world))
            {
                Main.StaticLogger.LogWarning($"Client manifest at \"{sourceForLogs}\" has no world name; ignoring it.");
                return null;
            }

            var m = new ClientManifest
            {
                World = world,
                Host = Get(kv, "host"),
                Port = Get(kv, "port"),
                IsVanilla = Get(kv, "vanilla") == "1",
                IsCrossplay = Get(kv, "crossplay") == "1",
                MinClientVersion = Get(kv, "minClientVersion"),
                ClientUrl = Get(kv, "clientUrl"),
            };

            return m;
        }

        private static string Get(Dictionary<string, string> kv, string key)
        {
            string v;
            return kv.TryGetValue(key, out v) ? v : "";
        }

        // Whether an address can be shown to the player.
        //
        // A crossplay world has no address to connect to -- Valheim hosts it through PlayFab
        // and the UDP port is unused -- so printing host:port for one would be telling the
        // player to try something that cannot work. Same rule the launch dialog follows.
        public bool HasAddress =>
            !IsCrossplay && !string.IsNullOrEmpty(Host) && !string.IsNullOrEmpty(Port);
    }
}
