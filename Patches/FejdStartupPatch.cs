using HarmonyLib;

// Namespace was DiscordNotifier.Patches, which is why this file could not see Main: the class
// it referenced lives in PhValheimCompanion. The fork never renamed it and nothing caught it
// because the project it was built in resolved Main from the upstream namespace.
namespace PhValheimCompanion.Patches
{
    internal class FejdStartupPatch
    {
        [HarmonyPatch(typeof(FejdStartup), "SetupGui")]
        internal class SetupGui
        {
            static void Postfix(ref FejdStartup __instance)
            {
                __instance.m_versionLabel.text += $"\n<size=6><color=#ADB2FFFF>{Main.AUTHOR}'s {Main.MODNAME} v{Main.VERSION}</color></size>";

                // The connect dialog is attached here rather than shown here. SetupGui runs
                // while the menu is still being built, and UnifiedPopup is not accepting
                // pushes yet -- a popup sent now is silently dropped. ConnectDialog's Update
                // waits for UnifiedPopup.IsAvailable() instead.
                //
                // Attaching it to FejdStartup's own GameObject ties its lifetime to the main
                // menu: when the menu scene goes away so does the component, so nothing is
                // left polling while the player is in a world.
                //
                // Nothing is attached unless the PhValheim client launched this game. No
                // --phvalheim-launch argument means no payload, no component and no trace of
                // the Companion for someone who started Valheim from Steam to play alone.
                if (!LaunchPayload.Present) return;

                if (__instance.gameObject.GetComponent<ConnectDialog>() == null)
                {
                    __instance.gameObject.AddComponent<ConnectDialog>();
                    Main.StaticLogger.LogMessage("PhValheim launch detected; the connect dialog will appear once the main menu is ready.");
                }
            }
        }
    }
}
