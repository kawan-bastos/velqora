using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;

[DataContract]
internal sealed class PresetStore : IExtensibleDataObject
{
    public ExtensionDataObject ExtensionData { get; set; }
    [DataMember(Name = "forceUnitsVersion", EmitDefaultValue = false)] public int ForceUnitsVersion { get; set; }
    [DataMember(Name = "formatVersion")] public int FormatVersion { get; set; }
    [DataMember(Name = "presets")] public List<Preset> Presets { get; set; }
    [DataMember(Name = "games")] public List<string> Games { get; set; }
}

internal sealed class PresetService
{
    // Version 3 explicitly protects advanced fields from v2-only applications that discard them.
    internal static bool IsSupportedFormat(int version) { return version==2||version==3; }
    internal static int FormatFor(IEnumerable<Preset> presets) { return presets.Any(p=>p!=null&&(p.PrimaryTrajectory!=null||p.SecondaryTrajectory!=null))?3:2; }
    internal const string UncategorizedGameName = "Não categorizado";
    private readonly string directory;
    private readonly string filePath;
    private readonly string legacyPath;
    private readonly bool wholeForceUnits;
    internal PresetService(string directory) : this(directory,true) { }
    internal PresetService(string directory,bool wholeForceUnits)
    {
        this.wholeForceUnits=wholeForceUnits;
        this.directory = directory;
        filePath = Path.Combine(directory, "presets.json");
        legacyPath = Path.Combine(directory, "presets.cfg");
    }
    internal string FilePath { get { return filePath; } }
    internal string LegacyFilePath { get { return legacyPath; } }
    internal void ReplaceAll(IEnumerable<Preset> presets) { WriteAll(presets); }

    internal IList<string> LoadGames()
    {
        if(wholeForceUnits) PresetForceMigration.Ensure(filePath);
        var games = new List<string>();
        if (File.Exists(filePath))
        {
            using (var stream=File.OpenRead(filePath))
            {
                var store=new DataContractJsonSerializer(typeof(PresetStore)).ReadObject(stream) as PresetStore;
                if(store!=null&&store.Games!=null)games.AddRange(store.Games.Where(g=>!IsUncategorized(g)));
                if(store!=null&&store.Presets!=null)games.AddRange(store.Presets.Where(p=>p!=null&&!IsUncategorized(p.GameName)).Select(p=>p.GameName));
                if(store!=null&&store.Presets!=null&&store.Presets.Any(p=>p!=null&&IsUncategorized(p.GameName)))games.Add(UncategorizedGameName);
            }
        }
        else if(File.Exists(legacyPath)&&LoadLegacy().Count>0) games.Add(UncategorizedGameName);
        return games.Where(g=>!string.IsNullOrWhiteSpace(g)).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(g=>g).ToList();
    }

    internal void SaveGame(string gameName)
    {
        if(string.IsNullOrWhiteSpace(gameName))throw new ArgumentException("Informe o nome do jogo.");
        if(IsUncategorized(gameName))throw new ArgumentException("“Não categorizado” é reservado para presets importados sem jogo.");
        IList<string> games=LoadGames();
        if(games.Any(g=>g.Equals(gameName.Trim(),StringComparison.OrdinalIgnoreCase)))return;
        games.Add(gameName.Trim());
        WriteAll(LoadAll(),games);
    }

    internal void DeleteGame(string gameName)
    {
        if(string.IsNullOrWhiteSpace(gameName))throw new ArgumentException("Selecione um jogo para excluir.");
        string name=gameName.Trim();
        if(IsUncategorized(name))throw new InvalidOperationException("A categoria “Não categorizado” é removida automaticamente quando não há mais presets importados sem jogo.");
        IList<Preset> all=LoadAll();
        if(all.Any(p=>p.GameName.Equals(name,StringComparison.OrdinalIgnoreCase)))throw new InvalidOperationException("Este jogo ainda tem presets. Exclua os presets vinculados antes de excluir o jogo.");
        WriteAll(all,LoadGames().Where(g=>!g.Equals(name,StringComparison.OrdinalIgnoreCase)));
    }

    internal void RenameGame(string oldName, string newName)
    {
        if(string.IsNullOrWhiteSpace(oldName))throw new ArgumentException("Selecione um jogo para editar.");
        if(string.IsNullOrWhiteSpace(newName))throw new ArgumentException("Digite o novo nome do jogo.");
        string oldValue=oldName.Trim(), newValue=newName.Trim();
        if(IsUncategorized(oldValue)||IsUncategorized(newValue))throw new InvalidOperationException("“Não categorizado” é reservado para presets importados sem jogo.");
        if(oldValue.Equals(newValue,StringComparison.OrdinalIgnoreCase))return;
        IList<string> games=LoadGames();
        if(!games.Any(g=>g.Equals(oldValue,StringComparison.OrdinalIgnoreCase)))throw new InvalidOperationException("O jogo selecionado não existe mais.");
        if(games.Any(g=>g.Equals(newValue,StringComparison.OrdinalIgnoreCase)))throw new InvalidOperationException("Já existe um jogo com esse nome.");

        IList<Preset> presets=LoadAll();
        foreach(Preset preset in presets.Where(p=>p.GameName.Equals(oldValue,StringComparison.OrdinalIgnoreCase)))
        {
            preset.GameName=newValue;
            preset.UpdatedAt=DateTime.Now;
        }
        WriteAll(presets,games.Select(g=>g.Equals(oldValue,StringComparison.OrdinalIgnoreCase)?newValue:g));
    }

    internal int TransferPresets(string sourceGame, string targetGame)
    {
        if(string.IsNullOrWhiteSpace(sourceGame)||string.IsNullOrWhiteSpace(targetGame))throw new ArgumentException("Selecione os jogos de origem e destino.");
        string source=sourceGame.Trim(), target=targetGame.Trim();
        if(IsUncategorized(source)||IsUncategorized(target))throw new InvalidOperationException("A transferência exige jogos cadastrados.");
        if(source.Equals(target,StringComparison.OrdinalIgnoreCase))throw new InvalidOperationException("O jogo de origem e o destino precisam ser diferentes.");
        IList<string> games=LoadGames();
        if(!games.Any(g=>g.Equals(source,StringComparison.OrdinalIgnoreCase))||!games.Any(g=>g.Equals(target,StringComparison.OrdinalIgnoreCase)))throw new InvalidOperationException("O jogo de origem ou destino não existe mais.");

        IList<Preset> all=LoadAll();
        List<Preset> moving=all.Where(p=>p.GameName.Equals(source,StringComparison.OrdinalIgnoreCase)).ToList();
        if(moving.Count==0)throw new InvalidOperationException("O jogo de origem não possui presets para transferir.");
        HashSet<string> targetNames=new HashSet<string>(all.Where(p=>p.GameName.Equals(target,StringComparison.OrdinalIgnoreCase)).Select(p=>p.Name),StringComparer.OrdinalIgnoreCase);
        List<string> conflicts=moving.Where(p=>targetNames.Contains(p.Name)).Select(p=>p.Name).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if(conflicts.Count>0)throw new InvalidOperationException("Não foi possível transferir porque o destino já possui preset(s) com o mesmo nome: "+string.Join(", ",conflicts)+". Renomeie esses presets e tente novamente.");

        foreach(Preset preset in moving){preset.GameName=target;preset.UpdatedAt=DateTime.Now;}
        WriteAll(all,games);
        return moving.Count;
    }

    internal IList<Preset> LoadAll()
    {
        if(wholeForceUnits) PresetForceMigration.Ensure(filePath);
        if (File.Exists(filePath))
        {
            try
            {
                using (var stream = File.OpenRead(filePath))
                {
                    var serializer = new DataContractJsonSerializer(typeof(PresetStore));
                    var store = serializer.ReadObject(stream) as PresetStore;
                    if (store == null || !IsSupportedFormat(store.FormatVersion) || store.Presets == null) throw new InvalidDataException("Formato JSON de presets inválido.");
                    return store.Presets.Select(Normalize).ToList();
                }
            }
            catch (Exception ex) { throw new InvalidDataException("Não foi possível ler presets.json sem risco de sobrescrever os dados.", ex); }
        }
        IList<Preset> legacy = LoadLegacy();
        if (File.Exists(legacyPath))
        {
            Directory.CreateDirectory(directory);
            string backup = legacyPath + ".migration.bak";
            if (!File.Exists(backup)) File.Copy(legacyPath, backup, false);
            WriteAll(legacy, new[] { UncategorizedGameName });
        }
        return legacy;
    }

    internal IList<Preset> LoadLegacy()
    {
        var result = new List<Preset>();
        if (!File.Exists(legacyPath)) return result;
        foreach (string line in File.ReadAllLines(legacyPath))
        {
            Preset preset;
            if (TryParseLine(line, out preset)) result.Add(preset);
        }
        return result;
    }

    internal void Save(Preset preset)
    {
        var presets = new List<Preset>(LoadAll());
        int index = presets.FindIndex(item => item.Name.Equals(preset.Name, StringComparison.OrdinalIgnoreCase));
        if (index >= 0) { preset.CreatedAt = presets[index].CreatedAt; presets[index] = Normalize(preset); }
        else { preset.CreatedAt = preset.CreatedAt == default(DateTime) ? DateTime.Now : preset.CreatedAt; preset.UpdatedAt = DateTime.Now; presets.Add(Normalize(preset)); }
        WriteAll(presets, LoadGames());
    }
    internal void SaveByIdentity(Preset preset)
    {
        var all = new List<Preset>(LoadAll());
        int index = all.FindIndex(p => IdentityEquals(p, preset));
        if (index >= 0) { preset.CreatedAt = all[index].CreatedAt; all.RemoveAll(p => IdentityEquals(p, preset)); preset.UpdatedAt = DateTime.Now; all.Add(Normalize(preset)); }
        else { preset.CreatedAt = DateTime.Now; preset.UpdatedAt = DateTime.Now; all.Add(Normalize(preset)); }
        WriteAll(all);
    }
    internal void Delete(string name)
    {
        var presets = new List<Preset>(LoadAll());
        presets.RemoveAll(item => item.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
        WriteAll(presets);
    }
    internal void Delete(Preset preset) { ReplaceAll(LoadAll().Where(p => !IdentityEquals(p, preset))); }
    internal void DeleteOne(Preset preset)
    {
        var all=new List<Preset>(LoadAll());
        int index=all.FindIndex(p=>IdentityEquals(p,preset)&&p.PrimaryVertical==preset.PrimaryVertical&&p.PrimaryHorizontal==preset.PrimaryHorizontal&&p.SecondaryVertical==preset.SecondaryVertical&&p.SecondaryHorizontal==preset.SecondaryHorizontal&&p.IntervalMilliseconds==preset.IntervalMilliseconds&&p.SecondaryIntervalMilliseconds==preset.SecondaryIntervalMilliseconds&&p.RequireRightButton==preset.RequireRightButton);
        if(index<0)index=all.FindIndex(p=>IdentityEquals(p,preset));
        if(index>=0){all.RemoveAt(index);ReplaceAll(all);}
    }

    private void WriteAll(IEnumerable<Preset> presets, IEnumerable<string> explicitGames = null)
    {
        Directory.CreateDirectory(directory);
        if(wholeForceUnits) PresetForceMigration.Ensure(filePath);
        List<Preset> list=presets.Select(Normalize).ToList();
        if(wholeForceUnits) foreach(var preset in list) PresetForceUnits.Round(preset);
        IList<string> games=explicitGames==null?LoadGames():explicitGames.ToList();
        var associatedGames=list.Select(p=>p.GameName).Where(g=>!IsUncategorized(g));
        var storedGames=games.Where(g=>!IsUncategorized(g)).Concat(associatedGames);
        var store = new PresetStore { ForceUnitsVersion = wholeForceUnits?PresetForceUnits.Version:0, FormatVersion = FormatFor(list), Presets = list, Games = storedGames.Where(g=>!string.IsNullOrWhiteSpace(g)).Distinct(StringComparer.OrdinalIgnoreCase).ToList() };
        string temporary = filePath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var stream = File.Create(temporary)) new DataContractJsonSerializer(typeof(PresetStore)).WriteObject(stream, store);
            if (File.Exists(filePath)) File.Replace(temporary, filePath, null);
            else
            {
                try { File.Move(temporary, filePath); }
                catch (UnauthorizedAccessException) { File.Copy(temporary, filePath, false); }
            }
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    internal static bool TryParseLine(string line, out Preset preset)
    {
        preset = null;
        string[] p = line.Split('|');
        bool legacy = p.Length == 5, current = p.Length == 7;
        if (!legacy && !current) return false;
        int pv, ph, sv = 0, sh = 0, interval;
        int intervalIndex = legacy ? 3 : 5, rightIndex = legacy ? 4 : 6;
        if (!int.TryParse(p[1], out pv) || !int.TryParse(p[2], out ph) || !int.TryParse(p[intervalIndex], out interval) || (current && (!int.TryParse(p[3], out sv) || !int.TryParse(p[4], out sh)))) return false;
        string name;
        try { name = Uri.UnescapeDataString(p[0]); } catch { return false; }
        if (string.IsNullOrWhiteSpace(name)) return false;
        preset = Normalize(new Preset { Name = name, GameName = "Não categorizado", CharacterName = "Preset", WeaponName = "Arma não definida", PrimaryVertical = pv, PrimaryHorizontal = ph, SecondaryVertical = sv, SecondaryHorizontal = sh, IntervalMilliseconds = interval, RequireRightButton = p[rightIndex] == "1", CreatedAt = DateTime.Now, UpdatedAt = DateTime.Now });
        return true;
    }
    internal static Preset Normalize(Preset p)
    {
        if (p == null) return null;
        p.PrimaryTrajectory=Trajectory.Normalize(p.PrimaryTrajectory);
        p.SecondaryTrajectory=Trajectory.Normalize(p.SecondaryTrajectory);
        if (string.IsNullOrWhiteSpace(p.GameName)) p.GameName = UncategorizedGameName;
        if (string.IsNullOrWhiteSpace(p.CharacterName)) p.CharacterName = "Preset";
        if (string.IsNullOrWhiteSpace(p.WeaponName)) p.WeaponName = "Arma não definida";
        if (p.CreatedAt == default(DateTime)) p.CreatedAt = DateTime.Now;
        if (p.UpdatedAt == default(DateTime)) p.UpdatedAt = p.CreatedAt;
        return p;
    }
    internal static bool IdentityEquals(Preset a, Preset b)
    {
        return Eq(a.GameName,b.GameName) && Eq(a.Name,b.Name);
    }
    private static bool Eq(string a,string b) { return string.Equals(a,b,StringComparison.OrdinalIgnoreCase); }
    internal static bool IsUncategorized(string gameName) { return Eq(gameName,UncategorizedGameName); }
}

