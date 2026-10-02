using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;

// Render the connect dialog body for a set of representative worlds, then check each one
// against the layout budget the panel actually has.
//
// Usage: renderDialog <companionDll> <libsDir>
//
// Everything here reflects the SHIPPED assembly. Nothing re-implements the formatting.
namespace RenderDialog
{
    internal static class Program
    {
        // Must match ConnectDialog.BodyLineBudget. Read from the DLL rather than hardcoded, so
        // the two cannot drift -- a budget this file believed in alone would be worthless.
        private static int _lineBudget;

        // The budget that applies when the scrolling mod list is showing: the body is confined
        // to the top of the rect, so it is much tighter than the full-panel one.
        private static int _lineBudgetWithList;

        // The body column is roughly this many characters wide at BodyFontSize in Valheim's
        // popup. Used to predict WRAPPING, which is what actually drives height: a single
        // logical line of 200 characters costs three rendered lines, and counting '\n' alone
        // would call it one and declare the layout fine.
        private const int WrapChars = 58;

        // The longest failure notice the dialog must survive. Kept in step with ConnectFlow by
        // a check in test-publicizer-trap.sh rather than by hope.
        private const int NoticeCharCap = 80;

        private static string _libs;

        private static int Main(string[] args)
        {
            if (args.Length < 2)
            {
                Console.Error.WriteLine("usage: renderDialog <PhValheimCompanion.dll> <libsDir>");
                return 2;
            }

            string dllPath = Path.GetFullPath(args[0]);
            _libs = Path.GetFullPath(args[1]);

            if (!File.Exists(dllPath))
            {
                Console.Error.WriteLine($"FAIL  no Companion dll at {dllPath} -- build it first (dotnet build -c Release).");
                return 2;
            }

            AppDomain.CurrentDomain.AssemblyResolve += ResolveFromLibs;

            Assembly asm = Assembly.LoadFrom(dllPath);
            Type dialog = asm.GetType("PhValheimCompanion.ConnectDialog");
            Type payloadType = asm.GetType("PhValheimCompanion.LaunchPayload");

            if (dialog == null || payloadType == null)
            {
                Console.Error.WriteLine("FAIL  ConnectDialog or LaunchPayload missing from the assembly.");
                return 1;
            }

            MethodInfo build = dialog.GetMethod("BuildBodyText",
                BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public);
            if (build == null)
            {
                Console.Error.WriteLine("FAIL  ConnectDialog.BuildBodyText not found. If it was renamed or made private, "
                    + "this harness is blind and the layout is untested again -- fix the harness, do not delete it.");
                return 1;
            }

            FieldInfo budget = dialog.GetField("BodyLineBudget",
                BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public);
            _lineBudget = budget != null ? (int)budget.GetRawConstantValue() : 8;

            // The two cases have DIFFERENT budgets and conflating them is what let the
            // overlapping layout through: with the scrolling list up, the body only owns the
            // top slice of the rect, so a 7-line body that passes the full-panel budget still
            // renders straight through the list.
            FieldInfo budgetWithList = dialog.GetField("BodyLineBudgetWithList",
                BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public);
            if (budgetWithList == null)
            {
                Console.Error.WriteLine("FAIL  ConnectDialog.BodyLineBudgetWithList not found -- the list-mode layouts "
                    + "would be checked against the full-panel budget, which is the bug this constant exists to catch.");
                return 1;
            }
            _lineBudgetWithList = (int)budgetWithList.GetRawConstantValue();

            MethodInfo parse = payloadType.GetMethod("Parse",
                BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public);
            if (parse == null)
            {
                Console.Error.WriteLine("FAIL  LaunchPayload.Parse not found.");
                return 1;
            }

            // Main.StaticLogger is assigned by BepInEx at plugin load, so outside the game it is
            // null and the FIRST thing Parse does on success is log through it. Give it a real
            // ManualLogSource rather than making the product code defensive about a situation
            // that only this harness creates.
            if (!InstallLogger(asm))
            {
                Console.Error.WriteLine("FAIL  could not install a log source; Parse would NullReference on its success path.");
                return 1;
            }

            int failures = 0;

            // The world Brian photographed, plus the cases that stress the budget hardest.
            failures += Render(build, parse, "MODDED CROSSPLAY (Brian's screenshot)",
                world: "ModdedCrossplayPublished", crossplay: true, vanilla: false, joinCode: "331682",
                mods: new List<string> { "CustomSeed  (ZeroBandwidth)", "serverblankpassword", "ThorsKist", "TickMonitor  (PhValheim)" });

            failures += Render(build, parse, "MODDED, DIRECT IP",
                world: "MyWorld", crossplay: false, vanilla: false, joinCode: "",
                mods: new List<string> { "CustomSeed", "ThorsKist", "TickMonitor" });

            failures += Render(build, parse, "WORST CASE: long name, 14 long mods",
                world: "AVeryLongWorldNameThatSomebodyWillAbsolutelyUse", crossplay: true, vanilla: false, joinCode: "999999",
                mods: Enumerable.Range(1, 14).Select(i => "SomeQuiteLongModName_" + i).ToList());

            failures += Render(build, parse, "CROSSPLAY, CODE NOT READY YET",
                world: "JustStarted", crossplay: true, vanilla: false, joinCode: "",
                mods: new List<string> { "CustomSeed" });

            failures += Render(build, parse, "MODDED WORLD, NO MODS FOUND (sync failed)",
                world: "BrokenSync", crossplay: false, vanilla: false, joinCode: "",
                mods: new List<string>());

            // The failure notice costs two extra lines at the TOP of the body, and it appears
            // on exactly the worlds that are already worst case -- a join that failed is often
            // a big modded world. Rendering it is the only way to know the notice does not push
            // the dialog back over the panel it just stopped overflowing.
            // A conservative UPPER BOUND, not a copy of any one message. ConnectFlow's longest
            // template is 68 characters before {payload.Host} expands; with a realistic hostname
            // that lands around 74. Testing 80 means every message that actually ships is
            // covered without this file having to track which one is currently longest -- and
            // dev_tools/test-publicizer-trap.sh fails the build if a template grows past the
            // budget this number assumes.
            string worstNotice = "A failure message of exactly eighty characters, which is the documented cap...";
            worstNotice = worstNotice.PadRight(NoticeCharCap, '.');
            if (worstNotice.Length != NoticeCharCap)
            {
                Console.Error.WriteLine($"FAIL  the worst-case notice is {worstNotice.Length} chars, expected {NoticeCharCap}.");
                return 1;
            }
            SetLastFailure(asm, worstNotice);
            failures += Render(build, parse, "AFTER A FAILED JOIN (notice shown)",
                world: "ModdedCrossplayPublished", crossplay: true, vanilla: false, joinCode: "331682",
                mods: new List<string> { "CustomSeed  (ZeroBandwidth)", "serverblankpassword", "ThorsKist", "TickMonitor  (PhValheim)" });
            SetLastFailure(asm, null);

            Console.WriteLine(failures == 0
                ? "== all layouts within budget =="
                : $"== {failures} layout(s) OVER BUDGET ==");

            return failures == 0 ? 0 : 1;
        }

        // ConnectFlow.LastFailure is a private-set property, so reach its backing field.
        private static void SetLastFailure(Assembly companion, string message)
        {
            Type flow = companion.GetType("PhValheimCompanion.ConnectFlow");
            FieldInfo f = flow?.GetField("<LastFailure>k__BackingField",
                BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public);
            if (f == null)
            {
                Console.Error.WriteLine("WARN  could not set ConnectFlow.LastFailure; the failure-notice layout is NOT being checked.");
                return;
            }
            f.SetValue(null, message);
        }

        private static bool InstallLogger(Assembly companion)
        {
            try
            {
                Type main = companion.GetType("PhValheimCompanion.Main");
                FieldInfo logger = main?.GetField("StaticLogger",
                    BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public);
                if (logger == null) return false;

                Type source = logger.FieldType;   // BepInEx.Logging.ManualLogSource
                object instance = Activator.CreateInstance(source, new object[] { "renderDialog" });
                logger.SetValue(null, instance);
                return true;
            }
            catch (Exception e)
            {
                Console.Error.WriteLine($"       ({e.GetType().Name}: {e.Message})");
                return false;
            }
        }

        private static Assembly ResolveFromLibs(object sender, ResolveEventArgs e)
        {
            string name = new AssemblyName(e.Name).Name + ".dll";
            string path = Path.Combine(_libs, name);
            return File.Exists(path) ? Assembly.LoadFrom(path) : null;
        }

        private static int Render(MethodInfo build, MethodInfo parse, string label,
            string world, bool crossplay, bool vanilla, string joinCode, List<string> mods)
        {
            // Field order is fixed by the server's phvBuildLaunchString() and pinned by
            // dev_tools/test-launch-payload-contract.sh:
            //   0 launch, 1 world, 2 password, 3 gameDNS, 4 port, 5 phvalheimHost,
            //   6 httpScheme, 7 vanilla, 8 crossplay, 9 joinCode
            string[] fields =
            {
                "launch", world, "", "valheim.example.com", "25000", "phv.example.com",
                "https", vanilla ? "1" : "0", crossplay ? "1" : "0", joinCode,
            };

            // '?' is the separator, not '|'. Getting this wrong produced a one-field split, the
            // parser took its error path, and that path NREs on Main.StaticLogger outside
            // BepInEx -- so the harness failed loudly rather than silently rendering nothing,
            // which is the right way round. Pinned by dev_tools/test-launch-payload-contract.sh.
            string b64 = Convert.ToBase64String(Encoding.UTF8.GetBytes(string.Join("?", fields)));
            object payload = parse.Invoke(null, new object[] { new[] { "valheim", "--phvalheim-launch", b64 } });

            if (payload == null)
            {
                Console.WriteLine($"FAIL  [{label}] LaunchPayload.Parse returned null -- the field layout above no longer matches.");
                return 1;
            }

            int failures = 0;

            // Both modes. listIsSeparate=true is what players see when the scrolling mod list
            // builds; false is the fallback when it does not. Checking only one would leave the
            // other free to overflow, and the fallback is exactly the path that runs when
            // something has already gone wrong.
            foreach (bool listIsSeparate in new[] { true, false })
            {
                string body = (string)build.Invoke(null, new object[] { payload, mods, listIsSeparate });

                int rendered = CountRenderedLines(body);
                int applicable = listIsSeparate ? _lineBudgetWithList : _lineBudget;
                bool over = rendered > applicable;
                if (over) failures++;

                Console.WriteLine();
                Console.WriteLine($"--- {label}  [{(listIsSeparate ? "scrolling list" : "inline fallback")}] ---");
                Console.WriteLine(Visualise(body));
                Console.WriteLine($"    rendered lines: {rendered} (budget {applicable}) {(over ? "<<< OVER BUDGET" : "ok")}");
            }

            return failures;
        }

        // Strip the rich-text tags, then count lines INCLUDING the ones wrapping produces.
        private static int CountRenderedLines(string body)
        {
            int total = 0;
            foreach (string logical in StripTags(body).Split('\n'))
            {
                int len = logical.TrimEnd().Length;
                total += len <= WrapChars ? 1 : (len + WrapChars - 1) / WrapChars;
            }
            return total;
        }

        // Show the body as the player sees it: tags removed, <pos> turned into a column stop so
        // the table alignment is actually visible in the terminal.
        private static string Visualise(string body)
        {
            var outLines = new List<string>();
            foreach (string logical in body.Split('\n'))
            {
                string line = logical;
                int col = 0;

                int posAt = line.IndexOf("<pos=", StringComparison.Ordinal);
                if (posAt >= 0)
                {
                    int close = line.IndexOf('>', posAt);
                    string spec = line.Substring(posAt + 5, close - posAt - 5).TrimEnd('%');
                    if (double.TryParse(spec, out double pct)) col = (int)Math.Round(WrapChars * pct / 100.0);
                }

                string stripped = StripTags(line);
                if (posAt >= 0)
                {
                    string left = StripTags(line.Substring(0, posAt));
                    string right = StripTags(line.Substring(line.IndexOf('>', posAt) + 1));
                    stripped = left.PadRight(Math.Max(col, left.Length + 1)) + right;
                }

                outLines.Add("    | " + stripped);
            }
            return string.Join("\n", outLines);
        }

        private static string StripTags(string s)
        {
            var sb = new StringBuilder();
            bool inTag = false;
            foreach (char c in s)
            {
                if (c == '<') { inTag = true; continue; }
                if (c == '>') { inTag = false; continue; }
                if (!inTag) sb.Append(c);
            }
            return sb.ToString();
        }
    }
}
