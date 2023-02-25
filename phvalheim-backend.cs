using System.IO;
using HarmonyLib;

namespace PhValheimCompanion
{
    public class PhValheimBackend
    {
        public static string GetPhValheimBackend()
        {
            // get root bepinex directory
            string thisDir = BepInEx.Paths.BepInExRootPath;

            // construct path to phvalheim.backend file
            string phvalheimBackendFile = Path.Combine(thisDir, "phvalheim.backend");

            // read phvalheim.backend file if exists and execute
            if (File.Exists(phvalheimBackendFile))
            {
                string phvalheimBackend = System.IO.File.ReadAllText(phvalheimBackendFile);
                return phvalheimBackend;
            }
            else
            {
                return "ERROR";
            }
        }
    }
}