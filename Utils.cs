using System;
using System.Net.Http;
using System.Reflection;
using System.Text;
using UnityEngine;

namespace PhValheimCompanion
{
    public static class Utils
    {
        // What remains of the fork's Utils class.
        //
        // Removed with the valheim-discord-notifier code: PostDiscordMessage() (LitJson, a
        // webhook PhValheim never configured) and FetchIPAddress() (called api.ipify.org from
        // a game client).
        public static BindingFlags BindFlags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static;

        // KEEP THIS, and keep using it for private members.
        //
        // We compile against assembly_valheim_publicized.dll, so writing
        // WorldGenerator.instance.m_world directly would compile green and then throw at
        // runtime, because the assembly the game actually loads still has that field private.
        // Reflection is what makes the access survive the real assembly. Replacing these calls
        // with direct field access is the easiest way to ship a build that looks fine and dies
        // the first time a player hangs a trophy.
        public static T GetPrivateField<T>(this object obj, string fieldName)
        {
            var prop = obj.GetType().GetField(fieldName, BindFlags);
            var value = prop.GetValue(obj);
            return (T)value;
        }

        // The same argument as GetPrivateField, for the other two kinds of access the connect
        // flow needs. FejdStartup.ProceedJoinRequest and FejdStartup.m_queuedJoinServer are
        // both private in the assembly the game loads; calling them directly compiles clean
        // against the publicized reference and throws MissingMethodException in front of a
        // player. Every access to a Valheim private goes through here.
        //
        // These return a bool instead of throwing because a failure here is recoverable: it
        // means Valheim renamed or moved the member, which is a thing that happens at every
        // game update. The caller logs what was being attempted and leaves the player on the
        // menu, able to join by hand -- rather than taking an unhandled exception through
        // Harmony and taking the main menu with it.
        public static bool InvokePrivate(object obj, string methodName, params object[] args)
        {
            var types = new Type[args.Length];
            for (int i = 0; i < args.Length; i++) types[i] = args[i]?.GetType() ?? typeof(object);

            var method = obj.GetType().GetMethod(methodName, BindFlags, null, types, null)
                      ?? obj.GetType().GetMethod(methodName, BindFlags);

            if (method == null)
            {
                Main.StaticLogger.LogError($"Valheim API has moved: {obj.GetType().Name}.{methodName} not found. Join by hand from the server list.");
                return false;
            }

            method.Invoke(obj, args);
            return true;
        }

        // Static private fields need their own accessor: UnifiedPopup.instance is one, and
        // reading it as UnifiedPopup.instance compiles against the publicized assembly and
        // throws MissingFieldException on a real client. Returns null rather than throwing so
        // the caller can degrade -- a missing popup singleton costs a nicer button label, not
        // the join.
        public static object GetStaticFieldValue(Type type, string fieldName)
        {
            var field = type.GetField(fieldName, BindFlags);
            if (field == null)
            {
                Main.StaticLogger.LogWarning($"Valheim API has moved: static {type.Name}.{fieldName} not found.");
                return null;
            }

            return field.GetValue(null);
        }

        // The read half of SetPrivateField, for when a missing field must not be fatal.
        // GetPrivateField throws a NullReferenceException in that case, which is right for
        // HungHeads (where a missing world means nothing sensible can be reported) and wrong
        // for cosmetic reads.
        public static bool TryGetFieldValue(object obj, string fieldName, out object value)
        {
            value = null;
            if (obj == null) return false;

            var field = obj.GetType().GetField(fieldName, BindFlags);
            if (field == null) return false;

            value = field.GetValue(obj);
            return true;
        }

        public static bool SetPrivateField(object obj, string fieldName, object value)
        {
            var field = obj.GetType().GetField(fieldName, BindFlags);
            if (field == null)
            {
                Main.StaticLogger.LogError($"Valheim API has moved: {obj.GetType().Name}.{fieldName} not found.");
                return false;
            }

            field.SetValue(obj, value);
            return true;
        }

        // JSON without Newtonsoft, and the reason is packaging rather than taste.
        //
        // From 2.53 the Companion ships INSIDE the phvalheim-server image instead of through
        // Thunderstore, so there is no catalogue to resolve a dependency from. A Newtonsoft
        // reference would mean dropping a 712 KB Newtonsoft.Json.dll into BepInEx/plugins
        // beside whatever copy some other mod bundled, and two versions of one assembly in a
        // single plugin folder is a load-order lottery that fails at runtime, not at build.
        // One message with two string fields does not justify carrying that.
        //
        // This replaces JsonTemplates.cs, which existed only to be handed to a serializer.
        // The wire format is still declared in exactly one place -- here.
        public static string HeadHungJson(string action, string world)
        {
            return "{\"action\":\"" + JsonEscape(action) + "\",\"world\":\"" + JsonEscape(world) + "\"}";
        }

        // Minimal but correct: a world name is operator-supplied text and reaches the backend
        // as JSON. An unescaped quote or backslash in one would produce a malformed body that
        // the backend rejects, which surfaces as trophies silently not registering.
        public static string JsonEscape(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            var sb = new StringBuilder(s.Length + 8);
            foreach (char c in s)
            {
                switch (c)
                {
                    case '"': sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\b': sb.Append("\\b"); break;
                    case '\f': sb.Append("\\f"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    default:
                        if (c < ' ') sb.Append("\\u").Append(((int)c).ToString("x4"));
                        else sb.Append(c);
                        break;
                }
            }
            return sb.ToString();
        }

        public static void PostPhValheimBackendMessage(string jsonMessage, Uri endpoint)
        {
            using var client = new HttpClient();
            var payload = new StringContent(jsonMessage, Encoding.UTF8, "application/json");
            string result = client.PostAsync(endpoint, payload).Result.Content.ReadAsStringAsync().Result;

            Debug.Log("PhValheim Companion: [Sent] " + jsonMessage);
            if (result == "true")
            {
                Debug.Log("PhValheim Companion: [Received] Backend response: OK");
            }
            else if (result == "false")
            {
                Debug.Log("PhValheim Companion: [Received] Backend response: FAIL");
            }
            else if (string.IsNullOrEmpty(result))
            {
                Debug.Log("PhValheim Companion: [Received] ERROR: The PhValheim backend didn't return a response. Make sure your PhValheim Server is up-to-date.");
            }
            else
            {
                Debug.Log("PhValheim Companion: [Received] Backend response: " + result);
            }
        }
    }
}
