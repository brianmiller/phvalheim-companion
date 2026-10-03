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

            // THE LAUNCH-HELP NOTICE -- the no-payload dialog.
            //
            // Rendered here rather than in a file of its own because the budget maths, the
            // wrap width and the visualiser all live here, and a second copy of them is a
            // second thing to be wrong. This body has no scrolling mod list, so the full-panel
            // budget is the one that applies.
            failures += RenderHelp(asm, dialog, "LAUNCH HELP: modded world with an address",
                new[]
                {
                    "world=Midgard", "host=valheim.example.com", "port=25003",
                    "vanilla=0", "crossplay=0", "minClientVersion=2.0.14",
                });

            // A crossplay world has no address. The body must not print one, and must say what
            // to use instead -- printing host:port here would send the player at a port that
            // is not even open.
            failures += RenderHelp(asm, dialog, "LAUNCH HELP: crossplay world (no address)",
                new[]
                {
                    "world=JustStarted", "host=valheim.example.com", "port=25004",
                    "vanilla=0", "crossplay=1", "minClientVersion=2.0.14",
                });

            failures += RenderHelp(asm, dialog, "LAUNCH HELP: worst case long name",
                new[]
                {
                    "world=AVeryLongWorldNameThatSomebodyWillAbsolutelyUse",
                    "host=a-rather-long-hostname.someones-homelab.example.com", "port=25999",
                    "vanilla=0", "crossplay=0", "minClientVersion=2.0.14",
                });

            // A manifest written by a server that did not set the version. The sentence about
            // app versions is dropped rather than printed with a blank in it.
            failures += RenderHelp(asm, dialog, "LAUNCH HELP: no minClientVersion in the manifest",
                new[] { "world=Bare", "host=h.example.com", "port=25000" });

            failures += ManifestParserChecks(asm);

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

        // Render the launch-help body from a manifest, and assert the things that make it
        // correct rather than only that it fits.
        //
        // The parse is done by the SHIPPED ClientManifest.FromLines, not by this file: the
        // split-on-first-'=' rule is the part most likely to be got wrong, and a harness with
        // its own parser would pass while the product truncated every URL at the query string.
        private static int RenderHelp(Assembly asm, Type dialog, string label, string[] lines)
        {
            Type manifestType = asm.GetType("PhValheimCompanion.ClientManifest");
            if (manifestType == null)
            {
                Console.WriteLine($"FAIL  [{label}] ClientManifest missing from the assembly.");
                return 1;
            }

            MethodInfo fromLines = manifestType.GetMethod("FromLines",
                BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public);
            MethodInfo buildHelp = dialog.GetMethod("BuildLaunchHelpBody",
                BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public);

            if (fromLines == null || buildHelp == null)
            {
                Console.WriteLine($"FAIL  [{label}] ClientManifest.FromLines or ConnectDialog.BuildLaunchHelpBody not found "
                    + "-- if either was renamed this harness is blind; fix it, do not delete it.");
                return 1;
            }

            object manifest = fromLines.Invoke(null, new object[] { lines, label });
            if (manifest == null)
            {
                Console.WriteLine($"FAIL  [{label}] FromLines returned null for a manifest that names a world.");
                return 1;
            }

            int failures = 0;
            string body = (string)buildHelp.Invoke(null, new object[] { manifest });

            int rendered = CountRenderedLines(body);
            bool over = rendered > _lineBudget;
            if (over) failures++;

            Console.WriteLine();
            Console.WriteLine($"--- {label} ---");
            Console.WriteLine(Visualise(body));
            Console.WriteLine($"    rendered lines: {rendered} (budget {_lineBudget}) {(over ? "<<< OVER BUDGET" : "ok")}");

            // CONTENT ASSERTIONS. The budget alone would pass on an empty body.
            bool crossplay = (bool)manifestType.GetProperty("IsCrossplay").GetValue(manifest);
            string host = (string)manifestType.GetProperty("Host").GetValue(manifest);
            string port = (string)manifestType.GetProperty("Port").GetValue(manifest);
            string stripped = StripTags(body);

            // The world name, always: a notice that does not say which world it is about
            // cannot be acted on.
            // !IsNullOrEmpty FIRST. string.Contains("") is true of every string, so asserting
            // only Contains(world) passes vacuously the moment the world name is blank -- which
            // is precisely the state the parser is supposed to refuse. Caught by mutating
            // ClientManifest to accept a nameless manifest: all four cases reported "ok".
            string world = (string)manifestType.GetProperty("World").GetValue(manifest);
            failures += Expect(label, !string.IsNullOrEmpty(world) && stripped.Contains(world),
                $"names the world \"{world}\"");

            // The address appears for an address world and NEVER for a crossplay one. Both
            // directions, because asserting only the first would pass on a body that always
            // prints host:port -- which is the bug.
            string addr = host + ":" + port;
            failures += crossplay
                ? Expect(label, !stripped.Contains(addr), "does NOT print an address for a crossplay world")
                : Expect(label, stripped.Contains(addr), $"prints the address {addr}");

            // Never the password itself, only where to find it. The manifest carries no
            // password, so the only way this could fail is someone adding one later.
            failures += Expect(label, stripped.IndexOf("Password", StringComparison.Ordinal) >= 0
                && stripped.IndexOf("on the world's page", StringComparison.Ordinal) >= 0,
                "says where the password is");

            // It must NOT assert the player's app is out of date -- it cannot know that. See
            // ClientManifest's note on the two indistinguishable causes.
            failures += Expect(label,
                stripped.IndexOf("is out of date", StringComparison.OrdinalIgnoreCase) < 0
                && stripped.IndexOf("your app is too old", StringComparison.OrdinalIgnoreCase) < 0,
                "does not claim the player's app is out of date");

            // The version sentence appears only when there is a version to name, and a blank
            // must never be printed as though it were one.
            string minVer = (string)manifestType.GetProperty("MinClientVersion").GetValue(manifest);
            failures += string.IsNullOrEmpty(minVer)
                ? Expect(label, stripped.IndexOf("or newer", StringComparison.Ordinal) < 0,
                    "omits the version requirement when the manifest has no version")
                : Expect(label, stripped.Contains(minVer) && stripped.Contains("or newer"),
                    $"names the required version {minVer}");

            // It must always say what to DO. The budget cut that brought this body from 15
            // lines to 6 is exactly the kind of edit that can delete the instruction and leave
            // a description of the problem behind.
            failures += Expect(label, stripped.IndexOf("Start it from the PhValheim app", StringComparison.Ordinal) >= 0,
                "tells the player what to do");

            return failures;
        }

        // The manifest parser's edge cases, with the controls that matter.
        //
        // RenderHelp above only ever feeds it well-formed input, and a parser that accepted
        // ANYTHING would pass every one of those cases. These are the ones that decide whether
        // the notice appears at all, and whether what it prints is the whole value.
        private static int ManifestParserChecks(Assembly asm)
        {
            Type t = asm.GetType("PhValheimCompanion.ClientManifest");
            MethodInfo fromLines = t?.GetMethod("FromLines",
                BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public);
            if (fromLines == null)
            {
                Console.WriteLine("FAIL  [manifest parser] ClientManifest.FromLines not found.");
                return 1;
            }

            Func<string[], object> parse = lines => fromLines.Invoke(null, new object[] { lines, "parser-check" });
            Func<object, string, string> str = (m, prop) => (string)t.GetProperty(prop).GetValue(m);

            int failures = 0;
            Console.WriteLine();
            Console.WriteLine("--- manifest parser ---");

            // CONTROL 1: no world name -> no manifest -> no dialog. Without this the parser
            // could return an object with World=="" and the notice would appear titled with a
            // blank, telling the player nothing about which world it concerns.
            failures += Expect("parser", parse(new[] { "host=h", "port=1" }) == null,
                "refuses a manifest with no world name");

            // CONTROL 2: and it is not refusing everything. A parser that always returned null
            // would pass CONTROL 1 and silently disable the whole feature.
            failures += Expect("parser", parse(new[] { "world=W" }) != null,
                "accepts a manifest that does name a world");

            // A value containing .=. survives whole. No field we read today has one, but a
            // WORLD NAME may, and splitting on every .=. would title the notice with half a
            // name -- which reads like a server-side typo rather than a parser bug.
            var eqy = parse(new[] { "world=Odin=Thor" });
            failures += Expect("parser", str(eqy, "World") == "Odin=Thor",
                $"splits on the FIRST .=. only (got \"{str(eqy, "World")}\")");

            // Comments and blank lines are skipped rather than parsed into junk keys.
            var commented = parse(new[] { "# written by phvalheim", "", "world=W", "port=25000" });
            failures += Expect("parser", commented != null && str(commented, "Port") == "25000",
                "skips comments and blank lines");

            // Flags are '1'/'0', and anything else is NOT true. An == "0" test would have read
            // an empty value as crossplay and withheld the address from a world that has one.
            var flags = parse(new[] { "world=W", "host=h", "port=1", "crossplay=" });
            failures += Expect("parser", flags != null && (bool)t.GetProperty("HasAddress").GetValue(flags),
                "an empty crossplay flag is not crossplay, so the address still shows");

            return failures;
        }

        private static int Expect(string label, bool ok, string what)
        {
            Console.WriteLine($"    {(ok ? "ok  " : "FAIL")}  {what}");
            return ok ? 0 : 1;
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
