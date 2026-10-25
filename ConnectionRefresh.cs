using System;
using System.Net.Http;
using System.Threading.Tasks;

namespace PhValheimCompanion
{
    // Network work runs asynchronously; Tick applies results on Unity's menu thread only.
    internal sealed class ConnectionRefresh
    {
        private static readonly HttpClient Client = new HttpClient(new HttpClientHandler { AllowAutoRedirect=false }) { Timeout=TimeSpan.FromSeconds(10) };
        private Task<string> pending;
        private DateTime next=DateTime.MinValue;
        private bool expired;
        internal bool Tick(LaunchPayload payload)
        {
            if(payload==null || expired) return false;
            if(string.IsNullOrEmpty(payload.RefreshToken)) { payload.ConnectionStatus="Live refresh needs a new launch from server 2.59+"; return false; }
            if(pending!=null)
            {
                if(!pending.IsCompleted) return false;
                try { var data=pending.GetAwaiter().GetResult(); if(!payload.ApplyConnectionState(data)) payload.ConnectionStatus="Backend returned invalid data · retrying in 15s"; }
                catch(UnauthorizedAccessException) {expired=true;payload.ConnectionStatus="Launch expired · launch this world again from PhValheim";}
                catch(Exception) {payload.ConnectionStatus="Backend unreachable · retrying in 15s";next=DateTime.UtcNow.AddSeconds(15);}
                pending=null;return true;
            }
            if(DateTime.UtcNow<next) return false;
            next=DateTime.UtcNow.AddSeconds(5);
            pending=Fetch(payload);return false;
        }
        private static async Task<string> Fetch(LaunchPayload p)
        {
            if(p.HttpScheme!="https" && p.HttpScheme!="http") throw new InvalidOperationException("Invalid backend scheme");
            var uri=new Uri(p.HttpScheme+"://"+p.PhValheimHost+"/companion.php?world="+Uri.EscapeDataString(p.World));
            if(!string.IsNullOrEmpty(uri.UserInfo)) throw new InvalidOperationException("Credentials in URL");
            using(var request=new HttpRequestMessage(HttpMethod.Get,uri))
            {
                request.Headers.Authorization=new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer",p.RefreshToken);
                using(var response=await Client.SendAsync(request).ConfigureAwait(false))
                {
                    if(response.StatusCode==System.Net.HttpStatusCode.Forbidden) throw new UnauthorizedAccessException();
                    response.EnsureSuccessStatusCode();
                    var body=await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                    if(body.Length>8192) throw new InvalidOperationException("Response too large");
                    return body;
                }
            }
        }
    }
}
