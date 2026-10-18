using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Net.Sockets;

internal static class UpdateHarness
{
    private static int assertions;
    private static void Check(bool value,string name){assertions++;if(!value)throw new Exception("FAIL: "+name);Console.WriteLine("PASS: "+name);}
    private static string Hash(byte[] bytes){using(var sha=SHA256.Create())return BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-","");}
    private static byte[] FakeExe(int size){var bytes=new byte[size];bytes[0]=(byte)'M';bytes[1]=(byte)'Z';for(int i=2;i<size;i++)bytes[i]=(byte)(i%251);return bytes;}
    private static async Task MainAsync()
    {
        Check(UpdateVersionComparer.Compare("2.0.0","1.9.9")>0,"SemVer greater stable version");Check(UpdateVersionComparer.Compare("2.0.0-beta","2.0.0")<0,"SemVer prerelease sorts below stable");Check(UpdateVersionComparer.Compare("2.0.0-beta.2","2.0.0-beta.11")<0,"SemVer numeric prerelease identifiers");Check(UpdateVersionComparer.Compare("1.2.3","1.2.3")==0,"SemVer equal versions");
        var notesMethod=typeof(UpdateService).GetMethod("ReleaseNotes",System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Static);var cleanNotes=(System.Collections.Generic.IList<string>)notesMethod.Invoke(null,new object[]{"Melhoria [detalhes](https://github.com/privado/repo)\nhttps://github.com/privado/repo/releases\ngithub.com/privado/repo/releases"});Check(cleanNotes.Count==1&&!cleanNotes[0].Contains("github.com"),"release notes exposed in UI have repository links stripped");
        string html="<!--StartFragment--><h1><span>Notas da versão — Macro do Butler</span></h1><h2><span>Novidades</span></h2><ul><li><span>Atalhos globais configuráveis.</span></li><li><span>Configurações organizadas em abas.</span></li></ul><h2><span>Melhorias</span></h2><ul><li><span>Layout mais organizado.</span></li></ul><!--EndFragment-->";var plainNotes=(System.Collections.Generic.IList<string>)notesMethod.Invoke(null,new object[]{html});Check(plainNotes.Count==3&&!string.Join(" ",plainNotes).Contains("<")&&plainNotes[0]=="Atalhos globais configuráveis.","HTML release notes are converted to clean readable summaries");
        Type releaseType=typeof(UpdateService).GetNestedType("Release",System.Reflection.BindingFlags.NonPublic);object release;using(var json=new MemoryStream(Encoding.UTF8.GetBytes("{\"tag_name\":\"v2.1.0\",\"published_at\":\"2026-10-14T17:50:39Z\",\"assets\":[]}")))release=new System.Runtime.Serialization.Json.DataContractJsonSerializer(releaseType).ReadObject(json);string publishedText=(string)releaseType.GetProperty("PublishedAt").GetValue(release,null);DateTime publishedAt;Check(DateTime.TryParse(publishedText,System.Globalization.CultureInfo.InvariantCulture,System.Globalization.DateTimeStyles.AssumeUniversal|System.Globalization.DateTimeStyles.AdjustToUniversal,out publishedAt)&&publishedAt.Year==2026,"GitHub ISO published_at deserializes and parses");
        byte[] good=FakeExe(700000);var server=new LocalServer(good);server.Start();string path=null;
        try
        {
            server.Bytes=Encoding.UTF8.GetBytes("{\"tag_name\":\"v3.3.0\",\"name\":\"Macro do Butler v3.3.0\",\"html_url\":\"https://github.com/owner/repo/releases/tag/v3.3.0\",\"published_at\":\"2026-10-14T17:50:39Z\",\"assets\":[{\"name\":\"Macro.do.Butler.exe\",\"browser_download_url\":\"https://github.com/owner/repo/releases/download/v3.3.0/Macro.do.Butler.exe\",\"size\":700000}]}" );server.StatusCode=200;var service=new UpdateService("3.2.0",Path.Combine(Path.GetTempPath(),"test.exe"),Path.GetTempPath(),server.Url+"api");UpdateInfo found=await service.CheckAsync(CancellationToken.None);Check(found!=null&&found.Version=="3.3.0"&&found.PublishedAt.HasValue,"release API parses version and ISO publication date");
            server.Bytes=Encoding.UTF8.GetBytes("{\"message\":\"API rate limit exceeded\"}");server.StatusCode=403;server.RetryAfter="5";bool rateLimitCaught=false;try{await service.CheckAsync(CancellationToken.None);}catch(UpdateRateLimitException ex){rateLimitCaught=ex.RetryAfterUtc>DateTime.UtcNow.AddSeconds(3)&&ex.RetryAfterUtc<DateTime.UtcNow.AddSeconds(10);}Check(rateLimitCaught,"GitHub rate-limit response carries retry-after guidance");server.StatusCode=200;server.RetryAfter=null;server.Bytes=good;
            var downloader=new UpdateDownloader();var info=new UpdateInfo{DownloadUrl=server.Url+"good",FileSize=good.Length,Sha256=Hash(good)};path=await downloader.DownloadAsync(info,null,CancellationToken.None);Check(File.Exists(path)&&new FileInfo(path).Length==good.Length,"HTTP download size/hash validates");File.Delete(path);path=null;
            info.Sha256=new string('0',64);await Throws<UpdateException>(()=>downloader.DownloadAsync(info,null,CancellationToken.None),"bad SHA-256 rejected");
            info.Sha256=Hash(good);info.FileSize=good.Length+1;await Throws<UpdateException>(()=>downloader.DownloadAsync(info,null,CancellationToken.None),"incomplete/mismatched size rejected");
            info.FileSize=100;await Throws<UpdateException>(()=>downloader.DownloadAsync(info,null,CancellationToken.None),"unexpected declared size rejected");
            var cts=new CancellationTokenSource();cts.Cancel();await Throws<OperationCanceledException>(()=>downloader.DownloadAsync(new UpdateInfo{DownloadUrl=server.Url+"slow",Sha256=Hash(good)},null,cts.Token),"cancelled request returns cancellation");
            var bad=FakeExe(700000);bad[0]=0;server.Bytes=bad;await Throws<UpdateException>(()=>downloader.DownloadAsync(new UpdateInfo{DownloadUrl=server.Url+"bad",FileSize=bad.Length,Sha256=Hash(bad)},null,CancellationToken.None),"non-PE response rejected");
        }
        finally{server.Dispose();if(path!=null&&File.Exists(path))File.Delete(path);}
        string root=Path.Combine(Environment.CurrentDirectory,"build-check","update-tests-"+Guid.NewGuid().ToString("N"));string install=Path.Combine(root,"install with spaces"),userdata=Path.Combine(root,"user data");Directory.CreateDirectory(install);Directory.CreateDirectory(userdata);
        try
        {
            File.WriteAllText(Path.Combine(install,"presets.json"),"{\"formatVersion\":2,\"presets\":[],\"games\":[]}");File.WriteAllText(Path.Combine(install,"presets.cfg"),"legacy-data");Directory.CreateDirectory(Path.Combine(install,"backups","nested"));File.WriteAllText(Path.Combine(install,"backups","nested","old.bak"),"backup");File.WriteAllText(Path.Combine(userdata,"hotkeys.json"),"{\"ToggleMacro\":\"Ctrl+F1\"}");File.WriteAllText(Path.Combine(userdata,"macro-settings.json"),"{\"IntervalMilliseconds\":9}");
            string backup=new UpdateBackupService(install,userdata).CreateBackup("2.0.0");var manifest=new System.Runtime.Serialization.Json.DataContractJsonSerializer(typeof(UpdateBackupManifest));UpdateBackupManifest bm;using(var f=File.OpenRead(Path.Combine(backup,"backup-manifest.json")))bm=(UpdateBackupManifest)manifest.ReadObject(f);
            Console.WriteLine("BACKUP FILE COUNT: "+bm.Files.Count+"; "+string.Join(",",bm.Files.Select(f=>f.Name)));
            Check(bm.Files.Count==5,"backup manifest includes every existing data file");Check(File.ReadAllText(Path.Combine(backup,"install","presets.json")).Contains("presets"),"preset JSON copied to backup");Check(File.ReadAllText(Path.Combine(backup,"install","presets.cfg"))=="legacy-data","legacy CFG copied without mutation");Check(File.Exists(Path.Combine(install,"backups","nested","old.bak")),"existing nested backups remain untouched");Check(bm.Files.Any(f=>f.Name=="user-data/hotkeys.json"),"user hotkeys recorded in manifest");
            var prefStore=new UpdatePreferencesStorageService(userdata);var prefs=UpdatePreferences.Defaults();prefs.CheckAutomatically=false;prefs.KeepPreviousVersion=true;prefs.NextUpdateCheckUtc=DateTime.UtcNow.AddMinutes(10);prefStore.Save(prefs);Check(!prefStore.Load().CheckAutomatically&&prefStore.Load().KeepPreviousVersion&&prefStore.Load().NextUpdateCheckUtc.HasValue,"update preferences and cooldown persist");
            string original=Path.Combine(install,"Macro do Butler.exe"),updated=Path.Combine(root,"updated.exe"),log=Path.Combine(root,"installer.log");byte[] oldExe=FakeExe(700000),newExe=FakeExe(750000);File.WriteAllBytes(original,oldExe);File.WriteAllBytes(updated,newExe);
            string script=BuildInstallerScript(updated,original,false,log,999999,true,1);string scriptPath=Path.Combine(root,"install.ps1");File.WriteAllText(scriptPath,script,new UTF8Encoding(false));int exit=RunPowerShell(scriptPath);
            Check(exit==0&&File.ReadAllBytes(original).SequenceEqual(newExe),"installer replaces executable in a path with spaces");Check(Directory.GetFiles(Path.Combine(install,"versions"),"*.exe").Any(f=>File.ReadAllBytes(f).SequenceEqual(oldExe)),"installer archives the previous executable");
            byte[] badExe=FakeExe(1000);File.WriteAllBytes(updated,badExe);string rollbackScript=BuildInstallerScript(updated,original,false,log,999999,true,1);File.WriteAllText(scriptPath,rollbackScript,new UTF8Encoding(false));exit=RunPowerShell(scriptPath);
            Check(exit!=0&&File.ReadAllBytes(original).SequenceEqual(newExe),"installer rolls back when replacement validation fails");
        }
        finally{Console.WriteLine("Synthetic fixtures: "+root);}
        Console.WriteLine("ASSERTIONS: "+assertions);
    }
    private static async Task Throws<T>(Func<Task<string>> action,string name) where T:Exception{bool thrown=false;try{await action();}catch(T){thrown=true;}Check(thrown,name);}
    private static string BuildInstallerScript(string source,string target,bool restart,string log,int pid,bool keep,int retention){var method=typeof(UpdateInstaller).GetMethod("BuildScript",System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Static);return (string)method.Invoke(null,new object[]{source,target,restart,log,pid,keep,retention});}
    private static int RunPowerShell(string script){var p=Process.Start(new ProcessStartInfo("powershell.exe","-NoProfile -ExecutionPolicy Bypass -File \""+script+"\""){UseShellExecute=false,CreateNoWindow=true,RedirectStandardError=true,RedirectStandardOutput=true});p.WaitForExit(15000);if(!p.HasExited){p.Kill();throw new Exception("Installer harness timed out");}string output=p.StandardOutput.ReadToEnd()+p.StandardError.ReadToEnd();if(output.Length>0)Console.WriteLine(output);return p.ExitCode;}
    private sealed class LocalServer:IDisposable
    {
        private readonly TcpListener listener;private Task loop;internal byte[] Bytes;internal string Url;internal int StatusCode=200;internal string RetryAfter;
        internal LocalServer(byte[] bytes){Bytes=bytes;listener=new TcpListener(System.Net.IPAddress.Loopback,0);}
        internal void Start(){listener.Start();int port=((System.Net.IPEndPoint)listener.LocalEndpoint).Port;Url="http://127.0.0.1:"+port+"/";loop=Task.Run(async()=>{while(true){TcpClient client;try{client=await listener.AcceptTcpClientAsync();}catch{break;}Task handler=Task.Run(async()=>{using(client){var stream=client.GetStream();var buffer=new byte[4096];int n=await stream.ReadAsync(buffer,0,buffer.Length);string request=Encoding.ASCII.GetString(buffer,0,n);if(request.Contains("/slow"))await Task.Delay(1000);byte[] body=Bytes;string reason=StatusCode==403?"Forbidden":"OK";string retry=RetryAfter==null?"":"Retry-After: "+RetryAfter+"\r\n";string headerText="HTTP/1.1 "+StatusCode+" "+reason+"\r\nContent-Length: "+body.Length+"\r\nConnection: close\r\n"+retry+"\r\n";byte[] header=Encoding.ASCII.GetBytes(headerText);try{await stream.WriteAsync(header,0,header.Length);await stream.WriteAsync(body,0,body.Length);}catch{}}});}});}
        public void Dispose(){listener.Stop();}
    }
    public static int Main(){try{MainAsync().GetAwaiter().GetResult();return 0;}catch(Exception e){Console.Error.WriteLine(e);return 1;}}
}
