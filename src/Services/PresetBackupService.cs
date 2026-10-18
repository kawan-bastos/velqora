using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;

internal sealed class PresetBackupService
{
    internal const int MaximumBackups = 10;
    private readonly string sourcePath;
    private readonly string backupDirectory;
    private readonly Func<DateTime> now;

    internal PresetBackupService(string sourcePath, Func<DateTime> now = null)
    {
        this.sourcePath = sourcePath;
        backupDirectory = Path.Combine(Path.GetDirectoryName(sourcePath), "backups");
        this.now = now ?? (() => DateTime.Now);
    }

    internal string CreateBackup()
    {
        if (!File.Exists(sourcePath)) return null;
        Directory.CreateDirectory(backupDirectory);
        string extension = Path.GetExtension(sourcePath);
        // Separate saves can share the same clock tick; never reuse or overwrite a backup.
        string target = Path.Combine(backupDirectory, "presets_" + now().ToString("yyyy-MM-dd_HHmmss_fff", CultureInfo.InvariantCulture) + "_" + Guid.NewGuid().ToString("N") + extension + ".bak");
        File.Copy(sourcePath, target, false);
        PruneOldBackups();
        return target;
    }

    internal IList<PresetBackupInfo> ListBackups()
    {
        if (!Directory.Exists(backupDirectory)) return new List<PresetBackupInfo>();
        return Directory.GetFiles(backupDirectory, "presets_*.*.bak")
            .Select(path => new PresetBackupInfo { FilePath = path, CreatedAt = File.GetCreationTime(path) })
            .OrderByDescending(item => item.CreatedAt).ToList();
    }

    internal IList<Preset> ReadAndValidate(string path)
    {
        var result = new List<Preset>();
        if (!File.Exists(path)) throw new InvalidDataException("O arquivo de backup não foi encontrado.");
        if (path.EndsWith(".json.bak", StringComparison.OrdinalIgnoreCase) || Path.GetExtension(path).Equals(".json", StringComparison.OrdinalIgnoreCase))
        {
            using (var stream = File.OpenRead(path))
            {
                var serializer = new System.Runtime.Serialization.Json.DataContractJsonSerializer(typeof(PresetStore));
                var store = serializer.ReadObject(stream) as PresetStore;
                if(store==null||!PresetService.IsSupportedFormat(store.FormatVersion)||store.Presets==null||store.Presets.Any(p=>!PresetTransferService.IsValidPreset(p))) throw new InvalidDataException("O backup JSON contém dados inválidos.");
                result=store.Presets;
            }
        }
        else foreach (string line in File.ReadAllLines(path))
        {
            if (string.IsNullOrWhiteSpace(line)) continue;
            Preset preset;
            if (!PresetService.TryParseLine(line, out preset) || !PresetTransferService.IsValidPreset(preset)) throw new InvalidDataException("O backup contém um preset inválido.");
            result.Add(preset);
        }
        return result;
    }

    internal void Restore(string path, PresetService presetService)
    {
        IList<Preset> presets = ReadAndValidate(path);
        CreateBackup();
        presetService.ReplaceAll(presets);
    }

    private void PruneOldBackups()
    {
        IList<PresetBackupInfo> backups = ListBackups();
        foreach (PresetBackupInfo old in backups.Skip(MaximumBackups))
        {
            try { File.Delete(old.FilePath); } catch { /* Backups are retained if Windows cannot remove an old file. */ }
        }
    }
}
