using HarmonyLib;
using System.IO;

namespace PhValheimCompanion
{
    [HarmonyPatch(typeof(ItemStand), "DelayedPowerActivation")]
    internal class HungHeads
    {        
        static void Postfix(ref ItemStand __instance)
        {
            if (!ZNet.instance.IsServer() && !ZNet.instance.IsDedicated()) 
            {
                // get PhValheim backend connectiong information
                string phvalheimBackend = PhValheimBackend.GetPhValheimBackend();

                // get name of head just activated from bossstone
                string bossHead = __instance.m_supportedItems[0].name;

                // execute main HungHeads stuff
                if (phvalheimBackend != "ERROR")
                {                    
                    Main.StaticLogger.LogMessage("Completed BossStone Detected: " + bossHead);
                    Main.StaticLogger.LogMessage("PhValheim Backend Detected: " + phvalheimBackend);
                }
                else
                {
                    Main.StaticLogger.LogMessage("Completed BossStone detected, but there's a problem.");
                    Main.StaticLogger.LogMessage("ERROR: Could not find the phvalheim.backend file.");
                    Main.StaticLogger.LogMessage("ERROR: Make sure you're running the latest version of phvalheim-client.");
                    Main.StaticLogger.LogMessage("ERROR: This is a non-fatal error but will prevent your client from updating");
                    Main.StaticLogger.LogMessage("ERROR: the phvalheim-server you are connected to with telemetry and statistics.");
                }     
            }
        }
    }




}