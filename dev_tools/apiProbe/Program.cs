using System.Reflection;
using System.Text;

// Usage: apiProbe <libsDir> <Type>[.member] [<Type>[.member]...]
//
// Prints one line per matching member, as a signature. Prints "MISSING" for a type or member
// that is not in the metadata at all. The MISSING line is the whole point -- see the control in
// probe.sh.
if (args.Length < 2)
{
    Console.Error.WriteLine("usage: apiProbe <libsDir> <Type>[.member] ...");
    return 2;
}

string libsDir = args[0];

// PathAssemblyResolver throws if two paths carry the same assembly identity, and the game's
// libs and this process's runtime BOTH ship netstandard.dll. Dedupe by file name with the
// game's copy winning, so the probe reads the types the game would actually bind to.
var byName = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
var runtimeDir = Path.GetDirectoryName(typeof(object).Assembly.Location);
foreach (var p in Directory.GetFiles(runtimeDir, "*.dll")) byName[Path.GetFileName(p)] = p;
foreach (var p in Directory.GetFiles(libsDir, "*.dll")) byName[Path.GetFileName(p)] = p;

var resolver = new PathAssemblyResolver(byName.Values.ToList());
using var mlc = new MetadataLoadContext(resolver, "System.Private.CoreLib");

var loaded = new List<Assembly>();
foreach (var path in Directory.GetFiles(libsDir, "*.dll"))
{
    try { loaded.Add(mlc.LoadFromAssemblyPath(path)); }
    catch (Exception e) { Console.Error.WriteLine($"skip {Path.GetFileName(path)}: {e.GetType().Name}"); }
}

const BindingFlags All = BindingFlags.Public | BindingFlags.NonPublic
                       | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;

foreach (var request in args.Skip(1))
{
    // "!Type.Method" dumps what a method body actually calls. See ILScan for why.
    if (request.StartsWith("!"))
    {
        var spec = request.Substring(1);
        int d = spec.LastIndexOf('.');
        if (d < 1) { Console.WriteLine($"IL: bad request {spec} (want !Type.Method)"); continue; }
        apiProbe.ILScan.Dump(ValheimAssembly(libsDir),
                             spec.Substring(0, d), spec.Substring(d + 1));
        continue;
    }

    // "=Type.Member" reports VISIBILITY, which is the one question the publicized assembly
    // cannot answer. Point this at a real, un-publicized assembly_valheim.dll: a member that
    // comes back NON-PUBLIC is one the Companion must reach by reflection, because a direct
    // call to it compiles green against the publicized reference and throws the first time a
    // player clicks the button.
    if (request.StartsWith("="))
    {
        var spec = request.Substring(1);

        // "Type..ctor" has to be split before the LAST dot would split it, or the type name
        // keeps a trailing dot, resolves to nothing, and every constructor in the codebase
        // reports MISSING -- which looks exactly like Valheim having deleted them.
        string tn, mn;
        if (spec.EndsWith("..ctor"))
        {
            tn = spec.Substring(0, spec.Length - ".ctor".Length - 1);
            mn = ".ctor";
        }
        else
        {
            int d = spec.LastIndexOf('.');
            if (d < 1) { Console.WriteLine($"VIS: bad request {spec} (want =Type.Member)"); continue; }
            tn = spec.Substring(0, d);
            mn = spec.Substring(d + 1);
        }

        Type t = FindType(loaded, tn);
        if (t == null) { Console.WriteLine($"MISSING TYPE  {tn}"); continue; }
        if (!t.IsPublic && !t.IsNestedPublic) { Console.WriteLine($"NON-PUBLIC TYPE  {tn}"); continue; }

        var vis = new List<string>();
        foreach (var m in t.GetMethods(All)) if (m.Name == mn) vis.Add(m.IsPublic ? "PUBLIC" : "NON-PUBLIC");
        foreach (var f in t.GetFields(All)) if (f.Name == mn) vis.Add(f.IsPublic ? "PUBLIC" : "NON-PUBLIC");
        foreach (var c in t.GetConstructors(All)) if (mn == ".ctor") vis.Add(c.IsPublic ? "PUBLIC" : "NON-PUBLIC");

        // Properties were missing from this list, so a perfectly present property
        // (FejdStartup.ServerPassword) reported MISSING MEMBER. A property's accessibility is
        // its accessors', so read those.
        foreach (var p in t.GetProperties(All))
        {
            if (p.Name != mn) continue;
            var getter = p.GetGetMethod(true);
            var setter = p.GetSetMethod(true);
            bool anyPublic = (getter != null && getter.IsPublic) || (setter != null && setter.IsPublic);
            vis.Add(anyPublic ? "PUBLIC" : "NON-PUBLIC");
        }

        if (vis.Count == 0) Console.WriteLine($"MISSING MEMBER  {tn}.{mn}");
        // Any public overload is enough to call; report the best case so an extra private
        // overload does not read as a failure.
        else if (vis.Contains("PUBLIC")) Console.WriteLine($"PUBLIC  {tn}.{mn}");
        else Console.WriteLine($"NON-PUBLIC  {tn}.{mn}");
        continue;
    }

    // "@Field" lists every vanilla method that reads or writes a field.
    if (request.StartsWith("@"))
    {
        Console.WriteLine($"ACCESS {request.Substring(1)}");
        apiProbe.ILScan.Writers(ValheimAssembly(libsDir), request.Substring(1));
        continue;
    }

    // "?Name" asks which type OWNS a member, rather than what a known type's member looks
    // like. Needed because the design notes recorded API calls by name without recording the
    // type they hang off, and guessing wrong there is how you ship a plugin that throws.
    if (request.StartsWith("?"))
    {
        string want = request.Substring(1);
        int found = 0;
        foreach (var a in loaded)
        {
            Type[] types;
            try { types = a.GetTypes(); } catch { continue; }
            foreach (var t in types)
            {
                foreach (var m in t.GetMethods(All))
                    if (m.Name == want)
                    {
                        found++;
                        try { Console.WriteLine($"OWNER {t.FullName}.{m.Name}  ->  {Fmt(m.ReturnType)} ({string.Join(", ", m.GetParameters().Select(p => $"{Fmt(p.ParameterType)} {p.Name}"))}){(m.IsStatic ? "  [static]" : "")}  [{a.GetName().Name}]"); }
                        catch { Console.WriteLine($"OWNER {t.FullName}.{m.Name}  (signature UNRESOLVED)"); }
                    }
                foreach (var f in t.GetFields(All))
                    if (f.Name == want) { found++; Console.WriteLine($"OWNER {t.FullName}.{f.Name}  (field)  [{a.GetName().Name}]"); }
            }
        }
        if (found == 0) Console.WriteLine($"NO OWNER FOUND  {want}");
        continue;
    }

    int dot = request.LastIndexOf('.');
    // A request with no dot, or whose prefix is not a known type, is treated as a bare type
    // name. "FejdStartup" and "FejdStartup.SelectCharacter" both have to work.
    string typeName = request, memberName = null;

    Type type = FindType(loaded, request);
    if (type == null && dot > 0)
    {
        typeName = request.Substring(0, dot);
        memberName = request.Substring(dot + 1);
        type = FindType(loaded, typeName);
    }

    if (type == null)
    {
        Console.WriteLine($"MISSING TYPE  {typeName}");
        continue;
    }

    var hits = new List<string>();

    // Each signature is decoded lazily and can throw on a type this lib set cannot resolve.
    // Report that member as UNRESOLVED rather than aborting -- a probe that dies on one field
    // tells you nothing about the other forty, and silence would read as "not present".
    void Try(Func<string> f, string what)
    {
        try { hits.Add(f()); }
        catch (Exception e) { hits.Add($"  UNRESOLVED  {what}  ({e.GetType().Name})"); }
    }

    foreach (var m in type.GetMethods(All))
        if (memberName == null || m.Name == memberName)
            Try(() => $"  method  {Fmt(m.ReturnType)} {m.Name}({string.Join(", ", m.GetParameters().Select(p => $"{Fmt(p.ParameterType)} {p.Name}"))}){(m.IsStatic ? "  [static]" : "")}", m.Name);

    foreach (var c in type.GetConstructors(All))
        if (memberName == null || memberName == ".ctor" || memberName == type.Name)
            Try(() => $"  ctor    {type.Name}({string.Join(", ", c.GetParameters().Select(p => $"{Fmt(p.ParameterType)} {p.Name}"))})", ".ctor");

    foreach (var f in type.GetFields(All))
        if (memberName == null || f.Name == memberName)
            Try(() => $"  field   {Fmt(f.FieldType)} {f.Name}{(f.IsStatic ? "  [static]" : "")}", f.Name);

    foreach (var p in type.GetProperties(All))
        if (memberName == null || p.Name == memberName)
            Try(() => $"  prop    {Fmt(p.PropertyType)} {p.Name}", p.Name);

    foreach (var n in type.GetNestedTypes(All))
        if (memberName == null || n.Name == memberName)
            hits.Add($"  nested  {n.Name}");

    if (hits.Count == 0)
    {
        Console.WriteLine($"MISSING MEMBER  {typeName}.{memberName}   (type exists in {type.Assembly.GetName().Name})");
        continue;
    }

    Console.WriteLine($"TYPE {type.FullName}   [{type.Assembly.GetName().Name}]");
    foreach (var h in hits.OrderBy(x => x)) Console.WriteLine(h);
}

return 0;

// The IL modes need a concrete file, and the directory handed in may hold either the
// publicized reference copy or a real un-publicized assembly. Hardcoding the publicized name
// made every IL assertion report MISSING when pointed at a real assembly -- which reads as
// "Valheim changed everything" rather than "the probe looked for the wrong file".
// Which assembly the IL requests ("!Type.Method", "~field") read.
//
// Normally the game's, because that is what the probe was built for: finding out what a
// Valheim method really does before patching it. But the same question gets asked of OUR OWN
// code -- "does this method body actually consult ClientManifest, or does it only mention it
// in a comment?" -- and a source grep cannot answer that. If the first argument names a .dll
// instead of a directory, that file is the subject and references resolve from its folder.
//
// Added after shipping a dialog whose attach gate and body builder were both correct and whose
// Update() still returned early, so Show() was never called. Every string the feature needs
// was in the dll; the path to them was unreachable. Only the IL could tell the difference.
static string ValheimAssembly(string libsDir)
{
    // APIPROBE_ASSEMBLY points the IL requests ("!Type.Method", "~field") at a different
    // assembly while references still resolve from libsDir.
    //
    // Normally the subject is the game's assembly, because that is what the probe was built
    // for: finding out what a Valheim method really does before patching it. But the same
    // question gets asked of OUR OWN code -- "does this method body actually consult
    // ClientManifest, or does it only mention it in a comment?" -- and no source grep can
    // answer that.
    //
    // Added after shipping a dialog whose attach gate and body builder were both correct and
    // whose Update() still returned early, so Show() was never called. Every string the
    // feature needed was in the dll and eight verify markers were green; the path to them was
    // unreachable. Only the IL could tell the difference.
    var over = Environment.GetEnvironmentVariable("APIPROBE_ASSEMBLY");
    if (!string.IsNullOrEmpty(over) && File.Exists(over)) return over;

    foreach (var name in new[] { "assembly_valheim_publicized.dll", "assembly_valheim.dll" })
    {
        var p = Path.Combine(libsDir, name);
        if (File.Exists(p)) return p;
    }
    return Path.Combine(libsDir, "assembly_valheim.dll");
}

static Type FindType(List<Assembly> asms, string name)
{
    foreach (var a in asms)
    {
        Type[] types;
        try { types = a.GetTypes(); } catch { continue; }
        foreach (var t in types)
            if (t.FullName == name || t.Name == name) return t;
    }
    return null;
}

static string Fmt(Type t)
{
    if (t == null) return "?";
    var n = t.Name;
    if (t.IsGenericType)
    {
        var baseName = n.Contains('`') ? n.Substring(0, n.IndexOf('`')) : n;
        return $"{baseName}<{string.Join(", ", t.GetGenericArguments().Select(Fmt))}>";
    }
    return n;
}
