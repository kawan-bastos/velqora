using System;
using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;

internal sealed class UpdateDownloader
{
    private static readonly HttpClient Client = CreateClient();
    private static HttpClient CreateClient(){var c=new HttpClient();c.Timeout=TimeSpan.FromMinutes(10);c.DefaultRequestHeaders.UserAgent.ParseAdd("Velqora-Updater/"+typeof(UpdateDownloader).Assembly.GetName().Version.ToString(3));return c;}
    internal async Task<string> DownloadAsync(UpdateInfo info, IProgress<double> progress, CancellationToken token)
    {
        if (string.IsNullOrWhiteSpace(info.Sha256) || !System.Text.RegularExpressions.Regex.IsMatch(info.Sha256, "\\A[0-9a-fA-F]{64}\\z"))
            throw new UpdateException("A atualização não possui SHA-256 válido e não será executada.");
        string target=Path.Combine(Path.GetTempPath(),"Velqora_update_"+Guid.NewGuid().ToString("N")+(info.IsInstaller?".exe":".tmp"));
        try
        {
            using(var response=await Client.GetAsync(info.DownloadUrl,HttpCompletionOption.ResponseHeadersRead,token).ConfigureAwait(false))
            {
                response.EnsureSuccessStatusCode();long? length=response.Content.Headers.ContentLength;
                if(info.FileSize.HasValue&&length.HasValue&&info.FileSize.Value!=length.Value)throw new UpdateException("O tamanho informado pela release não corresponde ao download.");
                using(var source=await response.Content.ReadAsStreamAsync().ConfigureAwait(false))
                using(var dest=new FileStream(target,FileMode.CreateNew,FileAccess.Write,FileShare.None,81920,true))
                {var buffer=new byte[81920];long total=0;int read;while((read=await source.ReadAsync(buffer,0,buffer.Length,token).ConfigureAwait(false))>0){await dest.WriteAsync(buffer,0,read,token).ConfigureAwait(false);total+=read;if(length.HasValue&&progress!=null)progress.Report(Math.Min(100,total*100.0/length.Value));}dest.Flush(true);if(info.FileSize.HasValue&&total!=info.FileSize.Value)throw new UpdateException("O arquivo baixado está incompleto: tamanho inesperado.");if(length.HasValue&&total!=length.Value)throw new UpdateException("O arquivo baixado está incompleto.");}
            }
            if(new FileInfo(target).Length<512*1024)throw new UpdateException("O arquivo baixado é pequeno demais para ser um executável válido.");
            byte[] signature=new byte[2];using(var f=File.OpenRead(target)){if(f.Read(signature,0,2)!=2||signature[0]!=(byte)'M'||signature[1]!=(byte)'Z')throw new UpdateException("O arquivo baixado não é um executável Windows válido.");}
            if(!string.IsNullOrWhiteSpace(info.Sha256)){string actual;using(var sha=SHA256.Create())using(var f=File.OpenRead(target))actual=BitConverter.ToString(sha.ComputeHash(f)).Replace("-","");if(!string.Equals(actual,info.Sha256.Replace(" ","").Replace(":",""),StringComparison.OrdinalIgnoreCase))throw new UpdateException("A validação SHA-256 do download falhou.");}
            if(progress!=null)progress.Report(100);return target;
        }
        catch(OperationCanceledException){TryDelete(target);throw;}
        catch(Exception ex){TryDelete(target);if(ex is UpdateException)throw;throw new UpdateException("Não foi possível baixar a atualização: "+ex.Message,ex);}
    }
    private static void TryDelete(string path){try{if(File.Exists(path))File.Delete(path);}catch{}}
}
