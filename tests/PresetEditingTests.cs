using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Runtime.Serialization.Json;

internal static class PresetEditingTests
{
    private static int checks;
    private static void Assert(bool value, string message) { if (!value) throw new Exception(message); checks++; }
    private static Preset Make(string name) { return new Preset { Name=name, GameName="Game", CharacterName="Existing metadata", WeaponName="Existing weapon", PrimaryVertical=350, IntervalMilliseconds=7, RequireRightButton=true }; }
    private static void Reject(Action action) { bool rejected=false; try { action(); } catch (ArgumentException) { rejected=true; } catch (InvalidOperationException) { rejected=true; } Assert(rejected,"Invalid save accepted"); }
    private static void Main(string[] args)
    {
        string root = Path.Combine(Path.GetFullPath(args[0]), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var blank = new PresetEditDraft(new Preset { GameName="Game", Name="", IntervalMilliseconds=0, SecondaryIntervalMilliseconds=0 }, true);
        Assert(blank.PrimaryVertical==0 && blank.PrimaryHorizontal==0 && blank.SecondaryVertical==0 && blank.SecondaryHorizontal==0 && blank.PrimaryInterval==2 && blank.SecondaryInterval==2, "New preset must have zero movement and valid 2 ms intervals");
        Assert(!blank.IsDirty && blank.MinimumInterval==2, "Blank draft state is invalid");
        Reject(()=>blank.PrimaryInterval=0);
        Reject(()=>blank.SecondaryInterval=0);
        var invalidSecondary=new PresetEditDraft(new Preset { IntervalMilliseconds=7, SecondaryIntervalMilliseconds=0 });
        Assert(invalidSecondary.SecondaryInterval==7,"Invalid secondary interval is not repaired in editor");
        var repository = new PresetService(root,false);
        repository.SaveGame("Game");
        blank.Name="Empty";
        PresetEditingService.Save(repository,null,blank.ToPreset(),()=>{});
        var emptySaved=repository.LoadAll().Single(p=>p.Name=="Empty");
        Assert(emptySaved.IntervalMilliseconds==2&&emptySaved.SecondaryIntervalMilliseconds==2&&emptySaved.PrimaryVertical==0&&emptySaved.SecondaryVertical==0,"Empty preset cannot be saved with valid defaults");
        repository.Delete(emptySaved);

        repository.SaveByIdentity(Make("Original"));
        repository.SaveByIdentity(Make("Untouched"));
        var original = repository.LoadAll().Single(p=>p.Name=="Original");
        string before = File.ReadAllText(repository.FilePath);
        var draft = new PresetEditDraft(original);
        Assert(!draft.IsDirty,"Fresh legacy draft is dirty");
        Assert(draft.SecondaryInterval==7,"Legacy interval fallback failed");
        int notifications=0; draft.PropertyChanged+=(s,e)=>notifications++;
        draft.PrimaryVertical=620; draft.SecondaryVertical=120; draft.PrimaryHorizontal=-45; draft.SecondaryHorizontal=33;
        draft.PrimaryInterval=9; draft.SecondaryInterval=15; draft.RequireRight=false; draft.Name="Renamed";
        Assert(draft.IsDirty && notifications==8,"Draft synchronization notifications failed");
        var livePrimary=draft.ToMovementSettings(true,false);
        var liveSecondary=draft.ToMovementSettings(true,true);
        Assert(livePrimary.Enabled&&livePrimary.VerticalPixelsPerSecond==620&&livePrimary.HorizontalPixelsPerSecond==-45&&livePrimary.IntervalMilliseconds==9&&!livePrimary.RequireRightButton,"Primary live preview values are incorrect");
        Assert(liveSecondary.Enabled&&liveSecondary.VerticalPixelsPerSecond==120&&liveSecondary.HorizontalPixelsPerSecond==33&&liveSecondary.IntervalMilliseconds==15,"Secondary live preview values are incorrect");
        Assert(original.PrimaryVertical==350 && original.Name=="Original","Draft modified source");
        Assert(before==File.ReadAllText(repository.FilePath),"Editing wrote storage before Save");
        Reject(()=>draft.PrimaryInterval=1);
        Reject(()=>draft.SecondaryHorizontal=6000);
        Assert(draft.PrimaryInterval==9 && draft.SecondaryHorizontal==33,"Rejected input changed draft");
        int backups=0;
        PresetEditingService.Save(repository,original,draft.ToPreset(),()=>backups++);
        var all=repository.LoadAll();
        Assert(all.Count==2 && all.All(p=>p.Name!="Original"),"Rename duplicated preset");
        var saved=all.Single(p=>p.Name=="Renamed");
        Assert(saved.PrimaryVertical==620 && saved.SecondaryVertical==120 && saved.IntervalMilliseconds==9 && saved.SecondaryIntervalMilliseconds==15,"Values did not persist");
        Assert(saved.CharacterName==original.CharacterName && saved.WeaponName==original.WeaponName,"Metadata lost");
        Assert(all.Single(p=>p.Name=="Untouched").PrimaryVertical==350 && backups==1,"Other preset changed");
        before=File.ReadAllText(repository.FilePath);
        var cancelled=new PresetEditDraft(saved); cancelled.PrimaryVertical=999;
        Assert(before==File.ReadAllText(repository.FilePath),"Cancelled draft wrote data");
        var duplicate=PresetEditingService.Copy(saved);duplicate.Name="Untouched";
        Reject(()=>PresetEditingService.Save(repository,saved,duplicate,()=>backups++));
        Assert(before==File.ReadAllText(repository.FilePath),"Duplicate rejection changed storage");
        var stale=PresetEditingService.Copy(saved); stale.PrimaryVertical=1;
        Reject(()=>PresetEditingService.Save(repository,stale,saved,()=>backups++));
        Assert(before==File.ReadAllText(repository.FilePath),"Conflict rejection changed storage");
        var invalid=PresetEditingService.Copy(saved);invalid.Name=" ";
        Reject(()=>PresetEditingService.Save(repository,saved,invalid,()=>backups++));
        var created=Make("New");
        PresetEditingService.Save(repository,null,created,()=>backups++);
        Assert(repository.LoadAll().Count==3,"New preset failed");
        // New optional fields continue to round-trip without requiring legacy data migration.
        using(var stream=new MemoryStream())
        {
            var serializer=new DataContractJsonSerializer(typeof(Preset)); serializer.WriteObject(stream,saved);stream.Position=0;
            var roundtrip=(Preset)serializer.ReadObject(stream);
            Assert(roundtrip.SecondaryIntervalMilliseconds==15,"Secondary interval roundtrip failed");
        }
        Console.WriteLine("PASS: "+checks+" checks. Isolated test data: "+root);
    }
}
