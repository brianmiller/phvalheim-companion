using System.Reflection;
using BepInEx;
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

        public static Configuration Configuration;

        private void Awake()
        {
            var harmony = new Harmony(GUID);
            var assembly = Assembly.GetExecutingAssembly();

            StaticLogger = Logger;
            Configuration = new Configuration(Config);

            // if the this mod is disabled in the config, don't run
            if (!Configuration.Enabled.Value) return;

            harmony.PatchAll(assembly);
            Logger.LogMessage($"{AUTHOR}'s {MODNAME} (v{VERSION}) has started");
        }
    }
}
