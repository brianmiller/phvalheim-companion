using System.Reflection;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;

namespace PhValheimCompanion
{
    [BepInPlugin(GUID, MODNAME, VERSION)]
    public class Main : BaseUnityPlugin
    {
        public const string MODNAME = "PhValheim Companion";
        public const string AUTHOR = "posixone";
        public const string GUID = "posixone_PhValheimCompanion";
        public const string VERSION = "1.0.4";

        internal static ManualLogSource StaticLogger;

        // The only configuration this mod ever actually used.
        //
        // It previously came from a Configuration class inherited from the
        // valheim-discord-notifier fork, which also carried a webhook URL, an ignored-username
        // list and a messages.json template file -- none of which PhValheim ever read. That
        // class is gone; the one setting that mattered is bound here directly.
        internal static ConfigEntry<bool> Enabled;

        // The off switch for the launch-help notice -- the dialog shown when a world's client
        // manifest is installed but nothing handed this game a world to join.
        //
        // It exists because that state has two causes and the Companion cannot tell them apart
        // (see ClientManifest): the player's PhValheim client may be too old to pass the
        // handoff, or they may simply have started Valheim from Steam to play single-player on
        // an install PhValheim set up. The first player needs the notice; the second will never
        // want it. Since no amount of cleverness in here can separate them, the honest answer
        // is to let the second one turn it off for good.
        //
        // BepInEx writes this to BepInEx/config/, which a world update does not overwrite --
        // the payload only carries the loader's own BepInEx.cfg, not per-plugin config. So the
        // choice survives, which is the entire point of offering it.
        internal static ConfigEntry<bool> ShowLaunchHelp;

        // NO BepInProcess attribute, and no side detection in Awake().
        //
        // [BepInProcess("valheim_server.x86_64")] does not match the real process name and so
        // silently prevents the plugin from loading server-side at all -- that is what broke
        // phvalheim-tickmonitor. This plugin is dual-role by design: HungHeads runs on the
        // CLIENT (hanging a trophy is a client-side act) while any server-side work runs on the
        // server. Whichever side a patch needs is decided at runtime from ZNet, once ZNet
        // exists, never here.
        private void Awake()
        {
            StaticLogger = Logger;

            Enabled = Config.Bind("General", "Enabled", true, "Is the plugin enabled?");
            ShowLaunchHelp = Config.Bind("General", "ShowLaunchHelp", true,
                "Show the PhValheim notice when Valheim is started without a world handed to it -- "
                + "for example from Steam rather than from the PhValheim app, or by a PhValheim app "
                + "too old to pass the world's connection details. Set to false to never show it.");
            if (!Enabled.Value)
            {
                Logger.LogMessage($"{AUTHOR}'s {MODNAME} (v{VERSION}) is disabled by configuration.");
                return;
            }

            new Harmony(GUID).PatchAll(Assembly.GetExecutingAssembly());

            // Installed here, not from a patch on the main menu: the sentry has to outlive the
            // main menu's GameObject, which is destroyed on the way into a world. See JoinSentry
            // for why that matters -- it is the difference between a disconnect and a failure.
            JoinSentry.Install();

            Logger.LogMessage($"{AUTHOR}'s {MODNAME} (v{VERSION}) has started");
        }
    }
}
