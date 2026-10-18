using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Globalization;
using System.Net.Http;
using System.Runtime.Serialization.Json;
using System.Threading;
using System.Threading.Tasks;

internal sealed class UpdateService
{
    private const string ReleasesApi="https://api.github.com/repos/kawan-bastos/velqora/releases/latest";
    private static readonly HttpClient Client=CreateClient();
    private readonly string currentVersion;
    private readonly string executable;
    private readonly string installDirectory;
    private readonly string dataDirectory;
    private readonly string releasesApi;
    private readonly UpdateDownloader downloader=new UpdateDownloader();
    private UpdateInfo available;
    internal UpdateInfo Available { get { return available; } }
    internal string LocalVersion { get { return currentVersion; } }
    internal bool UsesInstaller { get { return File.Exists(Path.Combine(installDirectory, "unins000.exe")); } }
    internal string LastDownloadedFile { get; private set; }
    internal UpdateService(string currentVersion,string executable,string dataDirectory):this(currentVersion,executable,dataDirectory,ReleasesApi){}
    internal UpdateService(string currentVersion,string executable,string dataDirectory,string releasesApi){this.currentVersion=currentVersion;this.executable=executable;installDirectory=Path.GetDirectoryName(executable);this.dataDirectory=dataDirectory;this.releasesApi=releasesApi;}
    private static HttpClient CreateClient(){var c=new HttpClient();c.Timeout=TimeSpan.FromSeconds(20);c.DefaultRequestHeaders.UserAgent.ParseAdd("Velqora-Updater/"+typeof(UpdateService).Assembly.GetName().Version.ToString(3));return c;}
    internal async Task<UpdateInfo> CheckAsync(CancellationToken token)
    {
        try
        {
            using(var response=await Client.GetAsync(releasesApi,HttpCompletionOption.ResponseContentRead,token).ConfigureAwait(false))
            {
                if(!response.IsSuccessStatusCode)
                {
                    if((int)response.StatusCode==403||(int)response.StatusCode==429)
                    {
                        string body=await response.Content.ReadAsStringAsync().ConfigureAwait(false);string remaining=response.Headers.Contains("X-RateLimit-Remaining")?response.Headers.GetValues("X-RateLimit-Remaining").FirstOrDefault():null;
                        if((int)response.StatusCode==429||remaining=="0"||body.IndexOf("rate limit",StringComparison.OrdinalIgnoreCase)>=0||body.IndexOf("abuse",StringComparison.OrdinalIgnoreCase)>=0)throw new UpdateRateLimitException(GetRetryAfterUtc(response));
                    }
                    response.EnsureSuccessStatusCode();
                }
                Release release;using(var stream=await response.Content.ReadAsStreamAsync().ConfigureAwait(false))release=new DataContractJsonSerializer(typeof(Release)).ReadObject(stream) as Release;
                if(release==null||release.Draft||release.Prerelease)throw new UpdateException("A release mais recente não é uma versão pública estável.");
                string version=(release.TagName??"").TrimStart('v','V');if(UpdateVersionComparer.Compare(version,currentVersion)<=0){available=null;return null;}
                bool installer=UsesInstaller;
                string executableName=installer?"Velqora-Setup-Windows-v"+version+".exe":"Velqora.exe";
                ReleaseAsset asset=release.Assets==null?null:release.Assets.FirstOrDefault(a=>string.Equals(a.Name,executableName,StringComparison.OrdinalIgnoreCase));
                if(installer&&asset==null){executableName="Velqora-Setup-v"+version+".exe";asset=release.Assets==null?null:release.Assets.FirstOrDefault(a=>string.Equals(a.Name,executableName,StringComparison.OrdinalIgnoreCase));}
                if(!installer&&asset==null){asset=release.Assets==null?null:release.Assets.FirstOrDefault(a=>string.Equals(a.Name,"Macro.do.Butler.exe",StringComparison.OrdinalIgnoreCase));executableName="Macro.do.Butler.exe";}
                if(asset==null)throw new UpdateException("A atualização não contém o pacote compatível com esta instalação.");
                if(!ValidUrl(release.HtmlUrl)||!ValidUrl(asset.BrowserDownloadUrl)||!string.Equals(new Uri(asset.BrowserDownloadUrl).Host,"github.com",StringComparison.OrdinalIgnoreCase))throw new UpdateException("A release contém uma URL inválida ou não confiável.");
                DateTime publishedAt;DateTime? published=DateTime.TryParse(release.PublishedAt,CultureInfo.InvariantCulture,DateTimeStyles.AssumeUniversal|DateTimeStyles.AdjustToUniversal,out publishedAt)?publishedAt:(DateTime?)null;
                var info=new UpdateInfo{Version=version,Name=release.Name,DownloadUrl=asset.BrowserDownloadUrl,FileSize=asset.Size,PublishedAt=published,ReleaseNotes=ReleaseNotes(release.Body),MinimumSupportedVersion=null,IsInstaller=installer};
                ReleaseAsset checksum=release.Assets.FirstOrDefault(a=>string.Equals(a.Name,installer?"SHA256SUMS.txt":executableName=="Velqora.exe"?"Velqora.sha256":"Macro.do.Butler.sha256",StringComparison.OrdinalIgnoreCase));
                if(checksum!=null)info.Sha256=await FetchHash(checksum.BrowserDownloadUrl,installer?executableName:null,token).ConfigureAwait(false);
                available=info;return info;
            }
        }
        catch(OperationCanceledException){throw;}
        catch(Exception ex){if(ex is UpdateException)throw;throw new UpdateException("Não foi possível verificar atualizações: "+ex.Message,ex);}
    }
    private static DateTime GetRetryAfterUtc(HttpResponseMessage response)
    {
        IEnumerable<string> values;string retry;
        if(response.Headers.TryGetValues("Retry-After",out values))
        {
            retry=values.FirstOrDefault();int seconds;DateTimeOffset date;
            if(int.TryParse(retry,out seconds)&&seconds>=0)return DateTime.UtcNow.AddSeconds(seconds);
            if(DateTimeOffset.TryParse(retry,CultureInfo.InvariantCulture,DateTimeStyles.AssumeUniversal|DateTimeStyles.AdjustToUniversal,out date))return date.UtcDateTime;
        }
        if(response.Headers.TryGetValues("X-RateLimit-Reset",out values)){long epoch;if(long.TryParse(values.FirstOrDefault(),out epoch))try{return DateTimeOffset.FromUnixTimeSeconds(epoch).UtcDateTime;}catch{}}
        return DateTime.UtcNow.AddHours(1);
    }
    internal async Task<string> DownloadAsync(IProgress<double> progress,CancellationToken token)
    {if(available==null)throw new UpdateException("Nenhuma atualização está disponível para baixar.");LastDownloadedFile=await downloader.DownloadAsync(available,progress,token).ConfigureAwait(false);return LastDownloadedFile;}
    internal string CreateBackup(){try{return new UpdateBackupService(installDirectory,dataDirectory).CreateBackup(currentVersion);}catch(Exception ex){Log("Backup failed: "+ex.Message);throw new UpdateException("Não foi possível criar o backup dos dados. A atualização não será iniciada.",ex);}}
    internal void Install(bool restart,int processId,bool keepPrevious,int previousVersionsToKeep)
    {if(string.IsNullOrWhiteSpace(LastDownloadedFile)||!File.Exists(LastDownloadedFile))throw new UpdateException("Baixe a atualização antes de instalar.");CreateBackup();if(UsesInstaller){if(available==null||!available.IsInstaller)throw new UpdateException("O pacote baixado não é um instalador compatível.");new SetupUpdateLauncher().Prepare(LastDownloadedFile,processId);}else new UpdateInstaller().Prepare(LastDownloadedFile,executable,restart,processId,keepPrevious,previousVersionsToKeep);Log("Installer helper started for version "+(available==null?"unknown":available.Version));}
    internal static void Log(string message){try{string directory=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"Velqora");Directory.CreateDirectory(directory);File.AppendAllText(Path.Combine(directory,"update.log"),DateTime.UtcNow.ToString("o")+" "+message+Environment.NewLine);}catch{}}
    private static bool ValidUrl(string value){Uri uri;return Uri.TryCreate(value,UriKind.Absolute,out uri)&&uri.Scheme==Uri.UriSchemeHttps;}
    private static System.Collections.Generic.IList<string> ReleaseNotes(string body)
    {
        string text=body??string.Empty;
        text=System.Text.RegularExpressions.Regex.Replace(text,"(?is)<(script|style)\\b[^>]*>.*?</\\1>",string.Empty);
        text=System.Text.RegularExpressions.Regex.Replace(text,"(?i)</?(?:br|p|div|li|h[1-6]|ul|ol|section|article|tr|td)[^>]*>",Environment.NewLine);
        text=System.Text.RegularExpressions.Regex.Replace(text,"(?s)<[^>]+>",string.Empty);
        text=System.Net.WebUtility.HtmlDecode(text).Replace("<!--StartFragment-->","").Replace("<!--EndFragment-->","").Replace("--StartFragment--","").Replace("--EndFragment--","");
        text=System.Text.RegularExpressions.Regex.Replace(text,"\\[[^\\]]*\\]\\([^)]*\\)|(?:https?://|www\\.)?github\\.com/\\S+|https?://\\S+","",System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        return text.Split(new[]{'\r','\n'},StringSplitOptions.RemoveEmptyEntries)
            .Select(x=>System.Text.RegularExpressions.Regex.Replace(x.Trim(),"[\\t ]+"," ").Trim().TrimStart('-','*','•',' '))
            .Where(x=>x.Length>0&&!System.Text.RegularExpressions.Regex.IsMatch(x,"^(?:Notas da versão(?:\\s+—.*)?|Novidades|Melhorias|Correções)$",System.Text.RegularExpressions.RegexOptions.IgnoreCase))
            .Select(x=>x.Length>240?x.Substring(0,237)+"...":x)
            .Take(8).ToList();
    }
    private static async Task<string> FetchHash(string url,string filename,CancellationToken token)
    {if(!ValidUrl(url)||!new Uri(url).Host.EndsWith("github.com",StringComparison.OrdinalIgnoreCase))throw new UpdateException("URL do checksum inválida.");using(var response=await Client.GetAsync(url,HttpCompletionOption.ResponseContentRead,token).ConfigureAwait(false)){response.EnsureSuccessStatusCode();string contents=await response.Content.ReadAsStringAsync().ConfigureAwait(false);return ParseChecksum(contents,filename);}}
    internal static string ParseChecksum(string contents,string filename)
    {if(contents==null||contents.Length>65536)throw new UpdateException("Lista de checksums inválida.");if(filename!=null){foreach(string line in contents.Split(new[]{'\r','\n'},StringSplitOptions.RemoveEmptyEntries)){var m=System.Text.RegularExpressions.Regex.Match(line,@"^([0-9a-fA-F]{64})\s+\*?(.+)$");if(m.Success&&string.Equals(m.Groups[2].Value.Trim(),filename,StringComparison.OrdinalIgnoreCase))return m.Groups[1].Value;}throw new UpdateException("Checksum do instalador não encontrado.");}var match=System.Text.RegularExpressions.Regex.Match(contents,"\\b[0-9a-fA-F]{64}\\b");if(!match.Success)throw new UpdateException("Arquivo SHA-256 da versão inválido.");return match.Value;}

    [System.Runtime.Serialization.DataContract]
    private sealed class Release
    {
        [System.Runtime.Serialization.DataMember(Name="tag_name")] public string TagName{get;set;}
        [System.Runtime.Serialization.DataMember(Name="name")] public string Name{get;set;}
        [System.Runtime.Serialization.DataMember(Name="html_url")] public string HtmlUrl{get;set;}
        [System.Runtime.Serialization.DataMember(Name="body")] public string Body{get;set;}
        [System.Runtime.Serialization.DataMember(Name="published_at")] public string PublishedAt{get;set;}
        [System.Runtime.Serialization.DataMember(Name="draft")] public bool Draft{get;set;}
        [System.Runtime.Serialization.DataMember(Name="prerelease")] public bool Prerelease{get;set;}
        [System.Runtime.Serialization.DataMember(Name="assets")] public System.Collections.Generic.List<ReleaseAsset> Assets{get;set;}
    }
    [System.Runtime.Serialization.DataContract]
    private sealed class ReleaseAsset
    {
        [System.Runtime.Serialization.DataMember(Name="name")] public string Name{get;set;}
        [System.Runtime.Serialization.DataMember(Name="browser_download_url")] public string BrowserDownloadUrl{get;set;}
        [System.Runtime.Serialization.DataMember(Name="size")] public long Size{get;set;}
    }
}
