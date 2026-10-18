using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Runtime.Serialization.Json;
using System.Text;

internal static class PresetForceMigrationTests
{
    private static int checks;
    private static void Check(bool value,string message) { if(!value)throw new Exception(message); checks++; }
    private static string root;
    private static string Folder(string name) { string path=Path.Combine(root,name);Directory.CreateDirectory(path);return path; }
    private static Preset Basic(string name)
    {
        return new Preset { Name=name,GameName="Game",CharacterName="Personagem",WeaponName="Arma",WeaponSlot=WeaponSlot.Secondary,
            PrimaryVertical=400,PrimaryHorizontal=60,SecondaryVertical=50,SecondaryHorizontal=-65,IntervalMilliseconds=7,SecondaryIntervalMilliseconds=13,RequireRightButton=true,
            CreatedAt=new DateTime(2025,1,2,3,4,5,DateTimeKind.Utc),UpdatedAt=new DateTime(2026,10,1,2,3,4,DateTimeKind.Utc) };
    }
    private static Trajectory Curve(bool enabled)
    {
        return new Trajectory { Enabled=enabled,Smooth=true,HoldLast=true,Points=new List<TrajectoryPoint> {
            new TrajectoryPoint { Horizontal=-60.6,Vertical=650.5 },new TrajectoryPoint { TimeMs=1234,Horizontal=75,Vertical=-250 } } };
    }
    private static PresetStore Store(int format=2)
    {
        return new PresetStore { FormatVersion=format,Games=new List<string> {"Game","Jogo sem presets"},Presets=new List<Preset> { Basic("Daimos"),Basic("Aruni") } };
    }
    private static string Write(string folder,PresetStore store)
    {
        string path=Path.Combine(folder,"presets.json");
        using(var stream=File.Create(path))new DataContractJsonSerializer(typeof(PresetStore)).WriteObject(stream,store);
        return path;
    }
    private static PresetStore Read(string path)
    {
        using(var stream=File.OpenRead(path))return (PresetStore)new DataContractJsonSerializer(typeof(PresetStore)).ReadObject(stream);
    }
    private static string[] Backups(string folder)
    {
        string path=Path.Combine(folder,"migration-backups");return Directory.Exists(path)?Directory.GetFiles(path,"presets.json",SearchOption.AllDirectories):new string[0];
    }
    private static void SameMetadata(Preset before,Preset after)
    {
        Check(before.Name==after.Name&&before.GameName==after.GameName&&before.CharacterName==after.CharacterName&&before.WeaponName==after.WeaponName,"Names and metadata changed");
        Check(before.CreatedAt==after.CreatedAt&&before.UpdatedAt==after.UpdatedAt,"Dates changed");
        Check(before.WeaponSlot==after.WeaponSlot&&before.RequireRightButton==after.RequireRightButton&&before.IntervalMilliseconds==after.IntervalMilliseconds&&before.SecondaryIntervalMilliseconds==after.SecondaryIntervalMilliseconds,"Activation, slot or intervals changed");
    }
    private static void FailsWithoutChange(string folder,Action action)
    {
        string path=Path.Combine(folder,"presets.json");byte[] original=File.ReadAllBytes(path);bool failed=false;
        try { action(); }catch(Exception) { failed=true; }
        Check(failed,"Unsafe migration did not fail");Check(File.ReadAllBytes(path).SequenceEqual(original),"Failure overwrote presets");
        Check(Directory.GetFiles(folder,"*.tmp").Length==0,"Temporary file not cleaned");
    }
    private static void Main()
    {
        root=Path.Combine(Environment.CurrentDirectory,"build-check","force-migration-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);
        string basic=Folder("v3.2-basic"),path=Write(basic,Store());byte[] original=File.ReadAllBytes(path);
        var repository=new PresetService(basic);var loaded=repository.LoadAll();
        Check(loaded.Count==2,"3.2 lost presets");
        Check(loaded[0].PrimaryVertical==400&&loaded[0].PrimaryHorizontal==60,"400 vertical or 60 lateral was divided in storage");
        Check(PresetForceUnits.Display(loaded[0].PrimaryVertical)==4&&PresetForceUnits.Display(loaded[0].PrimaryHorizontal,true)==6,"3.2 display scale is wrong");
        Check(loaded[0].SecondaryVertical==100&&loaded[0].SecondaryHorizontal==-70,"Half rounding policy is wrong");
        SameMetadata(Store().Presets[0],loaded[0]);
        Check(repository.LoadGames().SequenceEqual(new[]{"Game","Jogo sem presets"}),"Empty game lost");
        Check(Read(path).ForceUnitsVersion==1&&Read(path).FormatVersion==2,"Basic conversion marker/version missing");
        Check(Backups(basic).Length==1&&File.ReadAllBytes(Backups(basic)[0]).SequenceEqual(original),"Backup is not the exact old file");
        byte[] migrated=File.ReadAllBytes(path);new PresetService(basic).LoadAll();repository.LoadGames();repository.LoadAll();
        Check(File.ReadAllBytes(path).SequenceEqual(migrated)&&Backups(basic).Length==1,"Migration repeats after reopening");

        foreach(int version in new[]{2,3}) {
            string folder=Folder("advanced-"+version);var store=Store(version);store.Presets[0].PrimaryTrajectory=Curve(true);store.Presets[1].SecondaryTrajectory=Curve(false);store.Presets[1].SecondaryTrajectory.Step=true;
            string advancedPath=Write(folder,store);
            string json=File.ReadAllText(advancedPath).Replace("\"presets\":[","\"futureStore\":{\"nested\":[1,2]},\"presets\":[")
                .Replace("\"primaryVertical\":","\"futurePreset\":{\"option\":true},\"primaryVertical\":")
                .Replace("\"points\":[","\"futureTrajectory\":\"keep\",\"points\":[")
                .Replace("\"timeMs\":0","\"futurePoint\":\"keep\",\"timeMs\":0");
            File.WriteAllText(advancedPath,json,new UTF8Encoding(false));byte[] oldAdvanced=File.ReadAllBytes(advancedPath);
            Check(PresetForceMigration.Ensure(advancedPath),"Advanced migration did not run");var converted=Read(advancedPath);
            Check(converted.FormatVersion==3&&converted.ForceUnitsVersion==1,"Old advanced format not protected");
            Check(converted.Presets.Count==store.Presets.Count,"Advanced preset lost");
            for(int i=0;i<store.Presets.Count;i++) {
                SameMetadata(store.Presets[i],converted.Presets[i]);
                var before=i==0?store.Presets[i].PrimaryTrajectory:store.Presets[i].SecondaryTrajectory;
                var after=i==0?converted.Presets[i].PrimaryTrajectory:converted.Presets[i].SecondaryTrajectory;
                Check(before.Enabled==after.Enabled&&before.Smooth==after.Smooth&&before.Step==after.Step&&before.HoldLast==after.HoldLast,"Advanced flags changed");
                Check(after.Points.Count==2&&after.Points[0].TimeMs==0&&after.Points[1].TimeMs==1234,"Points or times changed");
                Check(after.Points[0].Horizontal==-60&&after.Points[0].Vertical==700&&after.Points[1].Horizontal==80&&after.Points[1].Vertical==-300,"Advanced force rounding is wrong");
            }
            foreach(string field in new[]{"futureStore","futurePreset","futureTrajectory","futurePoint"})Check(File.ReadAllText(advancedPath).Contains(field),"Migration dropped unknown field "+field);
            Check(File.ReadAllBytes(Backups(folder).Single()).SequenceEqual(oldAdvanced),"Advanced backup changed");
            byte[] first=File.ReadAllBytes(advancedPath);Check(!PresetForceMigration.Ensure(advancedPath)&&File.ReadAllBytes(advancedPath).SequenceEqual(first),"Advanced migration is not idempotent");
        }
        string sparse=Folder("legacy-empty-trajectory");var sparseStore=Store(3);
        sparseStore.Presets[0].PrimaryTrajectory=new Trajectory { Enabled=true,Points=new List<TrajectoryPoint>() };
        sparseStore.Presets[0].SecondaryTrajectory=new Trajectory { Points=null };
        sparseStore.Presets[1].PrimaryTrajectory=new Trajectory { Points=new List<TrajectoryPoint> { null,new TrajectoryPoint { Vertical=150 } } };
        string sparsePath=Write(sparse,sparseStore);
        Check(PresetForceMigration.Ensure(sparsePath),"Previously supported empty trajectories block migration");
        var sparseRead=Read(sparsePath);
        Check(sparseRead.Presets[0].PrimaryTrajectory.Points.Count==0&&sparseRead.Presets[0].SecondaryTrajectory.Points==null,"Migration replaces empty trajectory structure");
        Check(sparseRead.Presets[1].PrimaryTrajectory.Points[0]==null&&sparseRead.Presets[1].PrimaryTrajectory.Points[1].Vertical==200,"Nullable point is lost during migration");
        Check(!new PresetService(sparse).LoadAll()[0].PrimaryTrajectory.Enabled,"Normal empty trajectory fallback changed");
        string legacy=Folder("cfg");File.WriteAllText(Path.Combine(legacy,"presets.cfg"),"Antigo|350|-65|7|1\n");
        var oldCfg=new PresetService(legacy).LoadAll();Check(oldCfg.Count==1&&oldCfg[0].PrimaryVertical==400&&oldCfg[0].PrimaryHorizontal==-70,"CFG rounding failed");
        Check(File.Exists(Path.Combine(legacy,"presets.cfg.migration.bak"))&&Read(Path.Combine(legacy,"presets.json")).ForceUnitsVersion==1,"CFG backup or marker missing");
        string clean=Folder("clean");var cleanRepo=new PresetService(clean);Check(cleanRepo.LoadAll().Count==0&&!File.Exists(cleanRepo.FilePath),"Clean install invented data");
        cleanRepo.SaveGame("Game");Check(Read(cleanRepo.FilePath).ForceUnitsVersion==1,"New file has no marker");
        Check(Backups(clean).Length==0,"Clean install created a migration backup");
        string empty=Folder("empty");var emptyStore=Store();emptyStore.Presets.Clear();Write(empty,emptyStore);Check(new PresetService(empty).LoadAll().Count==0&&Read(Path.Combine(empty,"presets.json")).ForceUnitsVersion==1,"Empty store migration failed");

        string corrupt=Folder("corrupt");File.WriteAllText(Path.Combine(corrupt,"presets.json"),"{ broken JSON");FailsWithoutChange(corrupt,()=>new PresetService(corrupt).LoadAll());
        foreach(int version in new[]{1,99}) { string folder=Folder("bad-format-"+version);Write(folder,Store(version));FailsWithoutChange(folder,()=>new PresetService(folder).LoadAll()); }
        string future=Folder("future-scale");var futureStore=Store();futureStore.ForceUnitsVersion=2;Write(future,futureStore);FailsWithoutChange(future,()=>new PresetService(future).LoadGames());
        string locked=Folder("locked");Write(locked,Store());using(File.Open(Path.Combine(locked,"presets.json"),FileMode.Open,FileAccess.ReadWrite,FileShare.None)) {
            bool rejected=false;try {new PresetService(locked).LoadAll();}catch(IOException){rejected=true;}Check(rejected,"Locked file accepted");
        }
        Check(Read(Path.Combine(locked,"presets.json")).ForceUnitsVersion==0&&Backups(locked).Length==0,"Locked source marked converted");
        Check(new PresetService(locked).LoadAll().Count==2,"Retry after unlocking failed");
        string blocked=Folder("backup-blocked");Write(blocked,Store());File.WriteAllText(Path.Combine(blocked,"migration-backups"),"block");FailsWithoutChange(blocked,()=>new PresetService(blocked).LoadAll());
        string failure=Folder("interrupted");Write(failure,Store());FailsWithoutChange(failure,()=>PresetForceMigration.Ensure(Path.Combine(failure,"presets.json"),()=>{throw new IOException("Synthetic interruption");}));
        Check(Read(Path.Combine(failure,"presets.json")).ForceUnitsVersion==0,"Failed commit recorded completion");
        Check(Backups(failure).Length==1,"Failed commit lost original backup");Check(new PresetService(failure).LoadAll().Count==2,"Retry after interruption failed");
        string concurrent=Folder("concurrent-edit");string concurrentPath=Write(concurrent,Store());bool conflict=false;
        try { PresetForceMigration.Ensure(concurrentPath,()=>{var newer=Store();newer.Presets[0].Name="Nova edição";Write(concurrent,newer);}); } catch(IOException){conflict=true;}
        Check(conflict&&Read(concurrentPath).Presets[0].Name=="Nova edição"&&Read(concurrentPath).ForceUnitsVersion==0,"Concurrent edit overwritten");
        Check(new PresetService(concurrent).LoadAll()[0].Name=="Nova edição","Concurrent edit lost on retry");

        // Old executables can drop unknown markers; velocities still must never be divided twice.
        var markerLost=Read(path);markerLost.ForceUnitsVersion=0;Write(basic,markerLost);new PresetService(basic).LoadAll();
        Check(Read(path).Presets[0].PrimaryVertical==400&&Read(path).Presets[0].PrimaryHorizontal==60,"Missing marker causes double scaling");
        string transfer=Folder("import-restore");var transferRepo=new PresetService(transfer);transferRepo.SaveGame("Game");
        string external=Folder("external"),externalPath=Write(external,Store(3));var externalStore=Read(externalPath);externalStore.Presets[0].PrimaryVertical=350;externalStore.Presets[0].PrimaryTrajectory=Curve(true);Write(external,externalStore);
        var backup=new PresetBackupService(transferRepo.FilePath);new PresetTransferService(backup).Import(externalPath,transferRepo,null,DuplicatePresetMode.Ignore);
        Check(transferRepo.LoadAll()[0].PrimaryVertical==400&&transferRepo.LoadAll()[0].PrimaryTrajectory.Points[0].Vertical==700,"Import restores fractional force");
        backup.Restore(externalPath,transferRepo);Check(transferRepo.LoadAll()[0].PrimaryVertical==400&&Read(transferRepo.FilePath).ForceUnitsVersion==1,"Restore loses rounding policy");
        Console.WriteLine("Force migration: "+checks+" checks passed. Isolated fixtures: "+root);
    }
}
