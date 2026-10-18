using System;
using System.IO;
using System.Linq;
using System.Runtime.Serialization.Json;

internal static class PresetForceMigration
{
    // The marker and converted values are committed together; retries never divide stored velocities again.
    internal static bool Ensure(string path,Action beforeCommit = null)
    {
        if(!File.Exists(path))return false;
        byte[] original=File.ReadAllBytes(path);
        PresetStore store;
        var serializer=new DataContractJsonSerializer(typeof(PresetStore));
        using(var stream=new MemoryStream(original)) store=serializer.ReadObject(stream) as PresetStore;
        if(store==null||!PresetService.IsSupportedFormat(store.FormatVersion)||store.Presets==null)
            throw new InvalidDataException("Formato JSON de presets inválido. Os dados não foram alterados.");
        if(store.ForceUnitsVersion<0||store.ForceUnitsVersion>PresetForceUnits.Version)
            throw new InvalidDataException("A escala dos presets pertence a uma versão incompatível. Os dados não foram alterados.");
        if(store.ForceUnitsVersion==PresetForceUnits.Version)return false;
        foreach(var preset in store.Presets) PresetForceUnits.Round(preset);
        store.ForceUnitsVersion=PresetForceUnits.Version;
        store.FormatVersion=PresetService.FormatFor(store.Presets);
        string folder=Path.Combine(Path.GetDirectoryName(path),"migration-backups","force-units-v1-"+DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fff")+"-"+Guid.NewGuid().ToString("N"));
        string temporary=path+".force-units-"+Guid.NewGuid().ToString("N")+".tmp";
        try
        {
            // This backup is separate from the rotating backups and is never pruned automatically.
            Directory.CreateDirectory(folder);
            using(var backup=new FileStream(Path.Combine(folder,"presets.json"),FileMode.CreateNew,FileAccess.Write,FileShare.None))
            { backup.Write(original,0,original.Length); backup.Flush(true); }
            using(var output=new FileStream(temporary,FileMode.CreateNew,FileAccess.Write,FileShare.None))
            { serializer.WriteObject(output,store); output.Flush(true); }
            // Read back before replacing any live data.
            using(var verification=File.OpenRead(temporary))
            {
                var converted=(PresetStore)serializer.ReadObject(verification);
                if(converted.ForceUnitsVersion!=PresetForceUnits.Version||converted.Presets.Count!=store.Presets.Count)
                    throw new InvalidDataException("A conversão dos presets não pôde ser verificada.");
            }
            beforeCommit?.Invoke();
            if(!original.SequenceEqual(File.ReadAllBytes(path)))
                throw new IOException("Os presets mudaram durante a atualização. Reabra o aplicativo para tentar novamente.");
            File.Replace(temporary,path,null);
            return true;
        }
        finally { if(File.Exists(temporary))File.Delete(temporary); }
    }
}
