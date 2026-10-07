using System;
using System.IO;
using System.Reflection;

namespace ConnectOutcomes
{
    internal static class Program
    {
        private static string _libs;
        private static Type _flow;

        private static Assembly ResolveFromLibs(object sender, ResolveEventArgs e)
        {
            string name = new AssemblyName(e.Name).Name;
            string path = Path.Combine(_libs, name + ".dll");
            return File.Exists(path) ? Assembly.LoadFrom(path) : null;
        }

        private static FieldInfo F(string n) =>
            _flow.GetField(n, BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Public);
        private static FieldInfo Backing(string n) =>
            _flow.GetField("<" + n + ">k__BackingField", BindingFlags.NonPublic | BindingFlags.Static);

        // Put the flow into "a join is in flight" with the given history, then report what
        // NoticeMainMenu(true) decides. _connectingSince is set far in the past so the grace
        // period is satisfied without a Unity clock.
        private static string Outcome(bool joined, bool reachedCharacterSelect)
        {
            F("Connecting").SetValue(null, true);
            F("_connectingSince").SetValue(null, -10000f);
            F("_joined").SetValue(null, joined);
            F("_reachedCharacterSelect").SetValue(null, reachedCharacterSelect);
            Backing("LastFailure").SetValue(null, null);
            Backing("LastNotice").SetValue(null, null);

            var m = _flow.GetMethod("Decide",
                BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Public);
            bool gaveUp = (bool)m.Invoke(null, new object[] { true, 0f });

            bool connecting = (bool)F("Connecting").GetValue(null);
            var f = (string)Backing("LastFailure").GetValue(null);
            var n = (string)Backing("LastNotice").GetValue(null);
            return "gaveUp=" + gaveUp + " connecting=" + connecting
                 + " failure=" + (f == null ? "null" : "set")
                 + " notice=" + (n == null ? "null" : "set");
        }

        private static int Main(string[] args)
        {
            if (args.Length < 2)
            {
                Console.Error.WriteLine("usage: connectOutcomes <PhValheimCompanion.dll> <libsDir>");
                return 2;
            }
            string dll = Path.GetFullPath(args[0]);
            _libs = Path.GetFullPath(args[1]);
            AppDomain.CurrentDomain.AssemblyResolve += ResolveFromLibs;

            Assembly asm = Assembly.LoadFrom(dll);
            _flow = asm.GetType("PhValheimCompanion.ConnectFlow");

            // Every branch of the decision logs, and Main.StaticLogger is null outside
            // BepInEx -- so without this the first call dies on a NullReferenceException
            // inside the method under test, which looks exactly like the method being broken.
            Type main = asm.GetType("PhValheimCompanion.Main");
            FieldInfo logger = main?.GetField("StaticLogger",
                BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
            if (logger != null)
            {
                Type mls = Type.GetType("BepInEx.Logging.ManualLogSource, BepInEx", false)
                           ?? logger.FieldType;
                try
                {
                    logger.SetValue(null, Activator.CreateInstance(mls, true, new object[] { "probe" }));
                }
                catch
                {
                    try { logger.SetValue(null, Activator.CreateInstance(mls, new object[] { "probe" })); }
                    catch (Exception e)
                    {
                        Console.Error.WriteLine("FAIL  could not construct a log source (" + e.GetType().Name
                            + "); the decision cannot be driven. Fix the harness, do not delete it.");
                        return 1;
                    }
                }
            }
            if (_flow == null)
            {
                Console.Error.WriteLine("FAIL  PhValheimCompanion.ConnectFlow missing from the assembly.");
                return 1;
            }
            foreach (string n in new[] { "_joined", "_reachedCharacterSelect", "_connectingSince", "Connecting" })
            {
                if (F(n) == null)
                {
                    Console.Error.WriteLine("FAIL  ConnectFlow." + n + " not found -- this harness is blind now. "
                        + "Fix the harness, do not delete it.");
                    return 1;
                }
            }

            Console.WriteLine("disconnect " + Outcome(true, false));
            Console.WriteLine("cancel     " + Outcome(false, true));
            Console.WriteLine("failure    " + Outcome(false, false));
            Console.WriteLine("joined_via " + Outcome(true, true));

            Outcome(false, true);
            Console.WriteLine("notice_text " + (string)Backing("LastNotice").GetValue(null));
            return 0;
        }
    }
}
