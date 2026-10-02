using System;
using System.IO;

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
                string phvalheimBackend = "http://127.0.0.1:8081";
                return phvalheimBackend;
            }
        }

        // Was Configuration.phvalheimPublicApi, a STATIC FIELD:
        //     public static Uri phvalheimPublicApi = new Uri(GetPhValheimBackend() + "/api.php");
        //
        // A static field initialiser runs once, at type load, which read phvalheim.backend
        // before the client had necessarily written it and then cached that answer for the
        // life of the process. A method re-reads the file at the moment it is needed.
        public static Uri PublicApiUri()
        {
            return new Uri(GetPhValheimBackend() + "/api.php");
        }
    }
}
