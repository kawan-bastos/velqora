using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;

internal sealed class PresetTransferService
{
    private readonly PresetBackupService backupService;
    internal PresetTransferService(PresetBackupService backupService) { this.backupService = backupService; }

    internal void Export(string path, IList<Preset> presets)
    {
        foreach (Preset preset in presets) if (!IsValidPreset(preset)) throw new InvalidDataException("Um preset atual contém valores inválidos e não pode ser exportado.");
        var export = new PresetExportFile { FormatVersion = PresetService.FormatFor(presets), Application = "Velqora", ExportedAt = DateTime.Now, Presets = new List<Preset>(presets) };
        string temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var stream = File.Create(temporary)) new System.Runtime.Serialization.Json.DataContractJsonSerializer(typeof(PresetExportFile)).WriteObject(stream, export);
            if (File.Exists(path)) File.Replace(temporary, path, null);
            else { try { File.Move(temporary, path); } catch (UnauthorizedAccessException) { File.Copy(temporary, path, false); } }
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    internal IList<Preset> ReadAndValidate(string path)
    {
        try
        {
            if (!File.Exists(path)) throw new PresetImportException("O arquivo selecionado não foi encontrado.");
            if (Path.GetExtension(path).Equals(".json", StringComparison.OrdinalIgnoreCase))
            {
                using (var stream = File.OpenRead(path))
                {
                    var file = new System.Runtime.Serialization.Json.DataContractJsonSerializer(typeof(PresetImportFile)).ReadObject(stream) as PresetImportFile;
                    if (file == null || !PresetService.IsSupportedFormat(file.FormatVersion) || file.Presets == null) throw new PresetImportException("O JSON de presets tem uma versão ou estrutura inválida.");
                    if (file.Presets.Any(p => !IsValidPreset(p))) throw new PresetImportException("Há presets com valores ou categorias inválidos.");
                    return file.Presets.Select(Clone).ToList();
                }
            }
            var presets = new List<Preset>();
            foreach (string line in File.ReadAllLines(path))
            {
                if (string.IsNullOrWhiteSpace(line)) continue;
                Preset preset;
                if (!PresetService.TryParseLine(line, out preset)) throw new PresetImportException("O arquivo não está no formato presets.cfg compatível.");
                if (!IsValidPreset(preset)) throw new PresetImportException("Há presets com valores fora dos limites permitidos.");
                presets.Add(preset);
            }
            return presets.Select(Clone).ToList();
        }
        catch (PresetImportException) { throw; }
        catch (Exception exception) { throw new PresetImportException("Não foi possível ler o CFG. Verifique se o arquivo está íntegro e acessível.", exception); }
    }

    internal ImportResult Import(string path, PresetService presetService, string selectedPreset, DuplicatePresetMode mode)
    {
        IList<Preset> incoming = ReadAndValidate(path);
        IList<Preset> current = presetService.LoadAll();
        var merged = new List<Preset>(current);
        var result = new ImportResult { SelectedPreset = selectedPreset };
        bool destructive = false;
        foreach (Preset item in incoming)
        {
            int index = merged.FindIndex(p => PresetService.IdentityEquals(p, item));
            if (index < 0) { merged.Add(Clone(item)); result.Imported++; continue; }
            if (mode == DuplicatePresetMode.Ignore) { result.Ignored++; continue; }
            merged[index] = Clone(item); result.Replaced++; destructive = true;
        }
        if (result.Imported == 0 && result.Replaced == 0) return result;
        if (destructive || result.Imported > 0) backupService.CreateBackup();
        presetService.ReplaceAll(merged);
        if (selectedPreset != null && !merged.Any(p => p.Name.Equals(selectedPreset, StringComparison.OrdinalIgnoreCase))) result.SelectedPreset = null;
        return result;
    }

    internal static bool IsValidPreset(Preset preset)
    {
        return preset != null && !string.IsNullOrWhiteSpace(preset.Name) && preset.Name.Trim().Length <= 100 &&
            !string.IsNullOrWhiteSpace(preset.GameName) &&
            preset.GameName.Trim().Length <= 100 && (preset.CharacterName == null || preset.CharacterName.Trim().Length <= 100) && (preset.WeaponName == null || preset.WeaponName.Trim().Length <= 100) &&
            preset.PrimaryVertical >= 0 && preset.PrimaryVertical <= 10000 && preset.PrimaryHorizontal >= -5000 && preset.PrimaryHorizontal <= 5000 &&
            preset.SecondaryVertical >= 0 && preset.SecondaryVertical <= 10000 && preset.SecondaryHorizontal >= -5000 && preset.SecondaryHorizontal <= 5000 &&
            preset.IntervalMilliseconds >= 2 && preset.IntervalMilliseconds <= 50 &&
            (!preset.SecondaryIntervalMilliseconds.HasValue || (preset.SecondaryIntervalMilliseconds.Value >= 2 && preset.SecondaryIntervalMilliseconds.Value <= 50));
    }

    private static Preset Clone(Preset p) { return new Preset { PrimaryTrajectory=Trajectory.Normalize(p.PrimaryTrajectory), SecondaryTrajectory=Trajectory.Normalize(p.SecondaryTrajectory), GameName=p.GameName, CharacterName=p.CharacterName, WeaponName=p.WeaponName, WeaponSlot=p.WeaponSlot, Name = p.Name == null ? null : p.Name.Trim(), PrimaryVertical = p.PrimaryVertical, PrimaryHorizontal = p.PrimaryHorizontal, SecondaryVertical = p.SecondaryVertical, SecondaryHorizontal = p.SecondaryHorizontal, IntervalMilliseconds = p.IntervalMilliseconds, SecondaryIntervalMilliseconds = p.SecondaryIntervalMilliseconds, RequireRightButton = p.RequireRightButton, CreatedAt=p.CreatedAt, UpdatedAt=p.UpdatedAt }; }
}

[DataContract]
internal sealed class PresetImportFile
{
    [DataMember(Name="formatVersion")] public int FormatVersion { get; set; }
    [DataMember(Name="presets")] public List<Preset> Presets { get; set; }
}
