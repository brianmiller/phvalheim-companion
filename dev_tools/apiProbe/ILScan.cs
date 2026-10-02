using System.Collections.Immutable;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;

namespace apiProbe;

// Why a real IL walker and not a byte scan.
//
// The question this answers -- "does FejdStartup.OnCharacterStart already check
// HasServerToJoin(), or do I have to patch it?" -- changes what the Companion has to do. A
// naive scan for the call opcode (0x28) would also hit any 0x28 byte sitting inside a ldstr
// token or an i8 constant, so it would invent calls that are not there. Since the whole point
// is to stop guessing, the probe has to be right: operands are skipped by their declared width
// so every byte examined really is an opcode.
internal static class ILScan
{
    // Operand widths, by opcode. Anything not listed takes no operand.
    private static readonly HashSet<int> One = new()
    {
        0x0E, 0x0F, 0x10, 0x11, 0x12, 0x13, 0x1F, 0x2B, 0x2C, 0x2D,
        0x2E, 0x2F, 0x30, 0x31, 0x32, 0x33, 0x34, 0x35, 0x36, 0x37, 0xDE
    };

    private static readonly HashSet<int> Four = new()
    {
        0x20, 0x22, 0x27, 0x28, 0x29, 0x38, 0x39, 0x3A,
        0x3B, 0x3C, 0x3D, 0x3E, 0x3F, 0x40, 0x41, 0x42, 0x43, 0x44,
        0x6F, 0x70, 0x71, 0x72, 0x73, 0x74, 0x75, 0x79,
        0x7B, 0x7C, 0x7D, 0x7E, 0x7F, 0x80, 0x81, 0x8D, 0x8F,
        0xA3, 0xA4, 0xA5, 0xC2, 0xC6, 0xD0, 0xDD
    };

    private static readonly HashSet<int> Eight = new() { 0x21, 0x23 };

    // Two-byte opcodes, keyed on the second byte after the 0xFE prefix.
    private static readonly HashSet<int> TwoByteFour = new() { 0x06, 0x07, 0x15, 0x16, 0x1C };
    private static readonly HashSet<int> TwoByteTwo = new() { 0x09, 0x0A, 0x0B, 0x0C, 0x0D, 0x0E };
    private static readonly HashSet<int> TwoByteOne = new() { 0x12, 0x19 };

    public static int Dump(string assemblyPath, string typeName, string methodName)
    {
        using var fs = File.OpenRead(assemblyPath);
        using var pe = new PEReader(fs);
        var md = pe.GetMetadataReader();

        var matches = new List<MethodDefinitionHandle>();
        string owner = null;

        foreach (var th in md.TypeDefinitions)
        {
            var td = md.GetTypeDefinition(th);
            if (md.GetString(td.Name) != typeName) continue;
            foreach (var mh in td.GetMethods())
            {
                var mdf = md.GetMethodDefinition(mh);
                if (md.GetString(mdf.Name) != methodName) continue;
                matches.Add(mh);
                owner = md.GetString(td.Name);
            }
        }

        if (matches.Count == 0)
        {
            Console.WriteLine($"IL: MISSING  {typeName}.{methodName}  (no such method body in {Path.GetFileName(assemblyPath)})");
            return 1;
        }

        foreach (var mh in matches)
        {
            var mdf = md.GetMethodDefinition(mh);
            if (mdf.RelativeVirtualAddress == 0)
            {
                Console.WriteLine($"IL: {owner}.{methodName} has NO BODY (abstract or extern)");
                continue;
            }

            var body = pe.GetMethodBody(mdf.RelativeVirtualAddress);
            var il = body.GetILBytes();
            Console.WriteLine($"IL: {owner}.{methodName}  ({il.Length} bytes)");

            foreach (var line in Walk(md, il)) Console.WriteLine("    " + line);
        }

        return 0;
    }

    // "Which vanilla methods touch this field?" -- the question that tells you which flow to
    // copy. Reading the field's declaration says nothing about who drives it; the writers do.
    public static int Writers(string assemblyPath, string fieldName)
    {
        using var fs = File.OpenRead(assemblyPath);
        using var pe = new PEReader(fs);
        var md = pe.GetMetadataReader();

        int found = 0;
        foreach (var th in md.TypeDefinitions)
        {
            var td = md.GetTypeDefinition(th);
            foreach (var mh in td.GetMethods())
            {
                var mdf = md.GetMethodDefinition(mh);
                if (mdf.RelativeVirtualAddress == 0) continue;

                MethodBodyBlock body;
                try { body = pe.GetMethodBody(mdf.RelativeVirtualAddress); } catch { continue; }

                foreach (var line in Walk(md, body.GetILBytes()))
                {
                    if (!line.EndsWith("." + fieldName)) continue;
                    // Report the access KIND, because a reader and a writer mean different
                    // things: a writer is a flow to copy, a reader is a consumer to satisfy.
                    found++;
                    Console.WriteLine($"  {md.GetString(td.Name)}.{md.GetString(mdf.Name)}  ->  {line.Trim()}");
                }
            }
        }

        if (found == 0) Console.WriteLine($"NO ACCESS FOUND  {fieldName}");
        return 0;
    }

    private static IEnumerable<string> Walk(MetadataReader md, byte[] il)
    {
        int i = 0;
        while (i < il.Length)
        {
            int op = il[i];
            int start = i;
            i++;

            bool two = false;
            if (op == 0xFE && i < il.Length) { op = il[i]; i++; two = true; }

            // switch carries a variable-length jump table; get its width before skipping.
            if (!two && op == 0x45)
            {
                if (i + 4 > il.Length) break;
                int n = BitConverter.ToInt32(il, i);
                i += 4 + 4 * n;
                continue;
            }

            int operand =
                two ? (TwoByteFour.Contains(op) ? 4 : TwoByteTwo.Contains(op) ? 2 : TwoByteOne.Contains(op) ? 1 : 0)
                    : (Four.Contains(op) ? 4 : Eight.Contains(op) ? 8 : One.Contains(op) ? 1 : 0);

            // Calls say what a method reaches for; field access says what it DECIDES on.
            //
            // ldflda (0x7C) and stfld (0x7D) earn their place here the hard way: ServerJoinData
            // is a struct, so `this.m_joinServer.IsValid` compiles to ldflda + call, with no
            // ldfld anywhere. Leaving ldflda out made OnCharacterStart look like it tested a
            // field it never loaded, which is a probe inventing an answer.
            bool interesting = !two && (op == 0x28 || op == 0x6F || op == 0x73
                                     || op == 0x7B || op == 0x7C || op == 0x7D
                                     || op == 0x7E || op == 0x80);

            if (interesting && i + 4 <= il.Length)
            {
                int token = BitConverter.ToInt32(il, i);
                string kind = op switch
                {
                    0x28 => "call    ",
                    0x6F => "callvirt",
                    0x73 => "newobj  ",
                    0x7B => "ldfld   ",
                    0x7C => "ldflda  ",
                    0x7D => "stfld   ",
                    0x7E => "ldsfld  ",
                    _ => "stsfld  "
                };
                yield return $"IL_{start:x4}  {kind} {Resolve(md, token)}";
            }

            i += operand;
        }
    }

    private static string Resolve(MetadataReader md, int token)
    {
        try
        {
            var handle = MetadataTokens.EntityHandle(token);
            switch (handle.Kind)
            {
                case HandleKind.MethodDefinition:
                {
                    var m = md.GetMethodDefinition((MethodDefinitionHandle)handle);
                    var t = md.GetTypeDefinition(m.GetDeclaringType());
                    return $"{md.GetString(t.Name)}.{md.GetString(m.Name)}";
                }
                case HandleKind.MemberReference:
                {
                    var r = md.GetMemberReference((MemberReferenceHandle)handle);
                    return $"{ParentName(md, r.Parent)}.{md.GetString(r.Name)}";
                }
                case HandleKind.FieldDefinition:
                {
                    var f = md.GetFieldDefinition((FieldDefinitionHandle)handle);
                    var t = md.GetTypeDefinition(f.GetDeclaringType());
                    return $"{md.GetString(t.Name)}.{md.GetString(f.Name)}";
                }
                case HandleKind.TypeDefinition:
                    return md.GetString(md.GetTypeDefinition((TypeDefinitionHandle)handle).Name);
                case HandleKind.TypeReference:
                    return md.GetString(md.GetTypeReference((TypeReferenceHandle)handle).Name);
                case HandleKind.MethodSpecification:
                {
                    var spec = md.GetMethodSpecification((MethodSpecificationHandle)handle);
                    return Resolve(md, MetadataTokens.GetToken(spec.Method)) + "<>";
                }
                default:
                    return $"token:{handle.Kind}";
            }
        }
        catch (Exception e)
        {
            return $"token:0x{token:x8} UNRESOLVED ({e.GetType().Name})";
        }
    }

    private static string ParentName(MetadataReader md, EntityHandle parent)
    {
        switch (parent.Kind)
        {
            case HandleKind.TypeReference:
                return md.GetString(md.GetTypeReference((TypeReferenceHandle)parent).Name);
            case HandleKind.TypeDefinition:
                return md.GetString(md.GetTypeDefinition((TypeDefinitionHandle)parent).Name);
            case HandleKind.TypeSpecification:
                return "<generic>";
            default:
                return parent.Kind.ToString();
        }
    }
}
