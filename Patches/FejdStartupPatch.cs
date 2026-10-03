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
                __instance.m_versionLabel.text += $"\n<size=6><color={Theme.Button}>{Main.AUTHOR}'s {Main.MODNAME} v{Main.VERSION}</color></size>";

                // The connect dialog is attached here rather than shown here. SetupGui runs
                // while the menu is still being built, and UnifiedPopup is not accepting
                // pushes yet -- a popup sent now is silently dropped. ConnectDialog's Update
                // waits for UnifiedPopup.IsAvailable() instead.
                //
                // Attaching it to FejdStartup's own GameObject ties its lifetime to the main
                // menu: when the menu scene goes away so does the component, so nothing is
                // left polling while the player is in a world.
                //
                // Attached when EITHER the launch payload or the server-written manifest is
                // there, and the dialog decides between them.
                //
                // The payload means "the PhValheim client launched this game and told us the
                // world" -- the connect dialog. The manifest alone means "a PhValheim client
                // installed this world, but nothing was handed to us this time", which is the
                // case that used to be silent: from 2.53 that world has a password and no
                // QuickConnect entry, so the player needs telling how to get in. See
                // ClientManifest for why the two cannot be collapsed and why the manifest
                // cannot identify WHICH of its two causes applies.
                //
                // With neither, there is still no component and no trace of the Companion --
                // a plain Steam install that never met PhValheim is untouched.
                if (!LaunchPayload.Present && !ClientManifest.Present) return;

                // The one case where the manifest is present and the player wants nothing:
                // they play this install single-player from Steam and have said so. Only the
                // manifest-only path is suppressed; an explicit launch is never silenced,
                // because that player asked to join a world this very moment.
                if (!LaunchPayload.Present && !Main.ShowLaunchHelp.Value)
                {
                    Main.StaticLogger.LogMessage("No launch payload, and launch help is switched off in the config -- staying quiet.");
                    return;
                }

                if (__instance.gameObject.GetComponent<ConnectDialog>() == null)
                {
                    __instance.gameObject.AddComponent<ConnectDialog>();
                    Main.StaticLogger.LogMessage("PhValheim launch detected; the connect dialog will appear once the main menu is ready.");
                }
            }
        }
    }
}
