using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Text;
using LitJson;
using UnityEngine;


namespace PhValheimCompanion
{
    public static class Utils
    {

        public static BindingFlags BindFlags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static;

        public static void PostDiscordMessage(string message, string username = null)
        {
            // do not run if discord webhookurl is empty           
            if (Main.Configuration.DiscordWebhookUrl.Value.Length <= 0) return;

            Main.StaticLogger.LogMessage($"Posting message to Discord: {message}");

            var discordHttpWebRequest = (HttpWebRequest)WebRequest.Create(Main.Configuration.DiscordWebhookUrl.Value);
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


        public static void PostPhValheimBackendMessage(string message)
        {
            using var client = new HttpClient();
            var endpoint = new Uri("http://127.0.0.1:8081/adminAPI.php");
            var payload = new StringContent(message, Encoding.UTF8, "application/json");
            var result = client.PostAsync(endpoint, payload).Result.Content.ReadAsStringAsync().Result;

            Debug.Log("PhValheim Companion: [Sent] " + message);
            if (result == "ack")
            {
                Debug.Log("PhValheim Companion: [Received] Backend response:OK");
            }
            else if(string.IsNullOrEmpty(result))
            {
                Debug.Log("PhValheim Companion: [Received] ERROR:The PhValheim backend didn't return a response.");
            }
            else
            {
                Debug.Log("PhValheim Companion: [Received] Backend response: " + result);
            }


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

        public static T GetPrivateField<T>(this object obj, string fieldName)
        {
            var prop = obj.GetType().GetField(fieldName, BindFlags);
            var value = prop.GetValue(obj);
            return (T)value;
        }

    }
}