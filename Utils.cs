using System.Collections.Generic;
using System.IO;
using System.Net;
using LitJson;

namespace PhValheimCompanion
{
    public class Utils
    {
        public static void PostDiscordMessage(string message, string username = null)
        {
            Main.StaticLogger.LogMessage($"Posting message to Discord: {message}");

            var discordHttpWebRequest = (HttpWebRequest)WebRequest.Create(Main.Configuration.WebhookUrl.Value);
            discordHttpWebRequest.ContentType = "application/json";
            discordHttpWebRequest.Method = "POST";

            using (var streamWriter = new StreamWriter(discordHttpWebRequest.GetRequestStream()))
            {
                var body = new Dictionary<string, string> { { "content", message } };
                if (username != null) body.Add("username", username);

                streamWriter.Write(JsonMapper.ToJson(body));
            }

            discordHttpWebRequest.GetResponseAsync();
        }



        public static void PostPhValheimBackendMessage(string message, string username = null)
        {
            Main.StaticLogger.LogMessage($"Posting message to PhValheim Backend: {message}");

            var phvalheimHttpWebRequest = (HttpWebRequest)WebRequest.Create(Main.Configuration.PhValheimAdminApiUrl.Value);
            phvalheimHttpWebRequest.ContentType = "application/json";
            phvalheimHttpWebRequest.Method = "POST";

            using (var streamWriter = new StreamWriter(phvalheimHttpWebRequest.GetRequestStream()))
            {
                var body = new Dictionary<string, string> { { "content", message } };
                if (username != null) body.Add("username", username);

                streamWriter.Write(JsonMapper.ToJson(body));
            }

            phvalheimHttpWebRequest.GetResponseAsync();

        }





        public static string FetchIPAddress()
        {
            string ipAddress;
            const string url = @"https://api.ipify.org/";

            var request = (HttpWebRequest)WebRequest.Create(url);
            request.AutomaticDecompression = DecompressionMethods.GZip;

            using var response = (HttpWebResponse)request.GetResponse();
            using var stream = response.GetResponseStream();
            using var reader = new StreamReader(stream);
            
            ipAddress = reader.ReadToEnd();

            return ipAddress;
        }
    }
}