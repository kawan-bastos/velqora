using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
internal static class TrajectoryTests
{
 private static int count;
 private static void Check(bool ok,string message) { if(!ok)throw new Exception(message); count++; }
 private static void Near(double actual,double expected,string message) { Check(Math.Abs(actual-expected)<0.00001,message+": "+actual+" expected "+expected); }
 private static Trajectory Pattern() { return new Trajectory { Enabled=true,Points=new List<TrajectoryPoint> { new TrajectoryPoint { Horizontal=100,Vertical=200 },new TrajectoryPoint { TimeMs=1000,Horizontal=-100,Vertical=0 },new TrajectoryPoint { TimeMs=2000,Horizontal=0,Vertical=0 } } }; }
 private static void Main(string[] args)
 {
  var t=Pattern(); Near(t.Evaluate(500).Horizontal,0,"Linear midpoint"); Near(t.Evaluate(250).Vertical,150,"Linear quarter");
  Near(t.Integrate(0,1000).Horizontal,0,"Direction reversal integral"); Near(t.Integrate(0,1000).Vertical,100,"Linear integral");
  Near(t.Evaluate(2000).Horizontal,0,"Zero ending"); Near(t.Integrate(2000,10000).Vertical,0,"Zero ending integral");
  t.Points[2].Vertical=20; t.HoldLast=true; Near(t.Integrate(2000,3000).Vertical,20,"Hold ending"); Near(t.Evaluate(5000).Vertical,20,"Hold evaluation");
  t.Smooth=true; Near(t.Evaluate(250).Horizontal,68.75,"Smooth easing"); Near(t.Integrate(0,1000).Vertical,100,"Smooth integral");
  foreach(bool smooth in new[]{false,true}) {
   t.Smooth=smooth; var total=t.Integrate(0,3400); double x=0,y=0;
   for(int i=0;i<3400;i+=17) { var d=t.Integrate(i,Math.Min(i+17,3400)); x+=d.Horizontal; y+=d.Vertical; }
   Near(x,total.Horizontal,"Integration independent of interval X"); Near(y,total.Vertical,"Integration independent of interval Y");
  }
  var bad=Trajectory.Normalize(new Trajectory { Enabled=true,Points=new List<TrajectoryPoint> { null,new TrajectoryPoint { TimeMs=-5,Horizontal=double.NaN,Vertical=double.PositiveInfinity },new TrajectoryPoint { TimeMs=100,Horizontal=999999,Vertical=-999999 },new TrajectoryPoint { TimeMs=100,Horizontal=44 },new TrajectoryPoint { TimeMs=999999 } } });
  Check(bad.Points.Count==3&&bad.Points[1].Horizontal==44,"Duplicate uses last value"); Check(bad.Duration==60000,"Duration bounded"); Near(bad.Points[0].Horizontal,0,"NaN safe"); Near(bad.Points[0].Vertical,0,"Infinity safe");
  Check(!Trajectory.Normalize(new Trajectory()).Enabled,"Empty disabled"); Check(Trajectory.Normalize(null)==null,"Legacy null");
  Check(Trajectory.Normalize(new Trajectory { Points=new List<TrajectoryPoint> { new TrajectoryPoint { TimeMs=50 } } }).Points[0].TimeMs==0,"Missing origin inserted");
  foreach(bool rightRequired in new[]{false,true}) {
   var settings=new MovementSettings { Enabled=true,RequireRightButton=rightRequired,Trajectory=Pattern(),Revision=1 };
   var s=new MovementSession(); s.Step(settings,true,!rightRequired,0.01); Near(s.TimeMs,0,"Activation needs required button");
   s.Step(settings,false,true,0.01); s.Step(settings,true,true,0.01); s.Step(settings,true,true,0.05); Near(s.TimeMs,50,"Continuous time");
   s.Step(settings,false,true,0.01); Near(s.TimeMs,0,"Release resets"); s.Step(settings,true,true,0.01); Near(s.TimeMs,0,"Next press starts origin");
   s.Step(settings,true,true,0.02); settings.Revision++; s.Step(settings,true,true,0.02); Near(s.TimeMs,0,"Profile or weapon switch resets");
   s.Step(settings,true,true,0.02); settings.Enabled=false; s.Step(settings,true,true,0.02); Near(s.TimeMs,0,"Disabled resets");
   settings.Enabled=true; s.Step(settings,true,true,0.01); s.Step(settings,true,true,0.8); Near(s.TimeMs,800,"Wall time survives delay");
   if(rightRequired) { s.Step(settings,true,false,0.01); Near(s.TimeMs,0,"Right release resets combination"); }
  }
  var basic=new MovementSettings { Enabled=true,HorizontalPixelsPerSecond=10,IntervalMilliseconds=7 };
  var basicSession=new MovementSession(); basicSession.Step(basic,true,false,0.01); double sum=0;
  for(int i=0;i<100;i++)sum+=basicSession.Step(basic,true,false,0.01).Horizontal;
  Near(sum,10,"Fractional basic movement retained"); basic.Trajectory=Pattern(); basic.Trajectory.Enabled=false;
  basicSession.Step(basic,true,false,0.01);
  Near(basicSession.Step(basic,true,false,0.1).Horizontal,1,"Disabled trajectory uses basic");
  basicSession.Step(basic,true,false,0.02); basic.Trajectory=Pattern(); basicSession.Step(basic,true,false,0.02); Near(basicSession.TimeMs,0,"Trajectory reference change resets");
  basicSession.Step(basic,true,false,0.02); basic.Trajectory.Enabled=false; basicSession.Step(basic,true,false,0.02); Near(basicSession.TimeMs,0,"Trajectory disable resets");
  basicSession.Step(basic,true,false,0.02); basic.RequireRightButton=true; basicSession.Step(basic,true,true,0.02); Near(basicSession.TimeMs,0,"Activation mode change resets");
  var negative=new MovementSession(); var negativeSettings=new MovementSettings { Enabled=true,HorizontalPixelsPerSecond=-10 };
  negative.Step(negativeSettings,true,false,0.01); double negativeSum=0; for(int i=0;i<100;i++)negativeSum+=negative.Step(negativeSettings,true,false,0.01).Horizontal;
  Near(negativeSum,-10,"Negative fractional movement retained");
  string dir=Path.Combine(Path.GetFullPath(args[0]),Guid.NewGuid().ToString("N")); Directory.CreateDirectory(dir);
  var repo=new PresetService(dir,false); repo.SaveGame("Game"); var preset=new Preset { Name="Old",GameName="Game",PrimaryVertical=350,IntervalMilliseconds=7,RequireRightButton=true };
  repo.SaveByIdentity(preset); Check(repo.LoadAll()[0].PrimaryTrajectory==null,"Legacy loads without trajectory");
  var original=repo.LoadAll()[0]; var copy=PresetEditingService.Copy(original); copy.PrimaryTrajectory=Pattern(); copy.SecondaryTrajectory=Pattern(); copy.SecondaryTrajectory.HoldLast=true;
  PresetEditingService.Save(repo,original,copy,()=>{}); var loaded=repo.LoadAll()[0]; Check(loaded.PrimaryVertical==350&&loaded.PrimaryTrajectory.Enabled&&loaded.SecondaryTrajectory.HoldLast,"Additive storage round trip");
  var clone=PresetEditingService.Copy(loaded); clone.PrimaryTrajectory.Points[0].Horizontal=321;
  Check(loaded.PrimaryTrajectory.Points[0].Horizontal==100,"Deep copy isolation"); Check(!PresetEditingService.SameContent(loaded,clone),"Conflict detection includes trajectories");
  var transfer=new PresetTransferService(new PresetBackupService(repo.FilePath)); string export=Path.Combine(dir,"export.json"); transfer.Export(export,repo.LoadAll());
  var imported=transfer.ReadAndValidate(export)[0]; Check(imported.PrimaryTrajectory.Enabled&&imported.SecondaryTrajectory.HoldLast,"Transfer retains both trajectories");
  string malformed=File.ReadAllText(repo.FilePath).Replace("\"timeMs\":1000","\"timeMs\":0"); File.WriteAllText(repo.FilePath,malformed); loaded=repo.LoadAll()[0]; Check(loaded.PrimaryTrajectory.Points.Select(p=>p.TimeMs).Distinct().Count()==loaded.PrimaryTrajectory.Points.Count,"Malformed saved times normalized");
  var drawn=new List<DrawingPoint> { new DrawingPoint { X=200,Y=20 },new DrawingPoint { X=100,Y=100 },new DrawingPoint { X=300,Y=200 },new DrawingPoint { X=200,Y=300 } };
  foreach(bool smoothing in new[]{false,true}) foreach(bool invert in new[]{false,true}) {
   var converted=DrawingTrajectoryConverter.Convert(drawn,2000,0.5,invert,smoothing); double sign=invert?-1:1;
   Check(converted.Points.Count==7&&converted.Enabled&&!converted.HoldLast,"Drawing produces bounded existing trajectory format");
   Check(converted.Duration==2000,"Drawing duration retained");
   for(int i=1;i<drawn.Count;i++) {
    var position=converted.Integrate(0,converted.Points[i*2].TimeMs);
    Near(position.Horizontal,sign*(drawn[i].X-drawn[0].X)*0.5,"Drawing exact X endpoint");
    Near(position.Vertical,sign*(drawn[i].Y-drawn[0].Y)*0.5,"Drawing exact Y endpoint");
   }
   Check(converted.Points.Zip(converted.Points.Skip(1),(a,b)=>a.TimeMs<b.TimeMs).All(x=>x),"Drawing times strictly increase");
   Check(converted.Points.All(p=>Math.Abs(p.Horizontal)<=5000&&Math.Abs(p.Vertical)<=10000),"Drawing speeds bounded");
   Near(converted.Evaluate(3000).Horizontal,0,"Drawing ends at zero X"); Near(converted.Evaluate(3000).Vertical,0,"Drawing ends at zero Y");
  }
  var dense=Enumerable.Range(0,64).Select(i=>new DrawingPoint { X=i*2,Y=i*3 }).ToList();
  Check(DrawingTrajectoryConverter.Convert(dense,60000,1,false,false).Points.Count==127,"Maximum drawing fits keyframe limit");
  Check(DrawingTrajectoryConverter.Convert(dense,200,0.01,false,false).Points.Count==127,"Short drawing keeps valid timestamps");
  foreach(Action invalidDrawing in new Action[] {
   ()=>DrawingTrajectoryConverter.Convert(new List<DrawingPoint>(),1000,1,false,false),
   ()=>DrawingTrajectoryConverter.Convert(drawn,99,1,false,false),
   ()=>DrawingTrajectoryConverter.Convert(drawn,1000,double.NaN,false,false),
   ()=>DrawingTrajectoryConverter.Convert(drawn,100,10,false,false),
   ()=>DrawingTrajectoryConverter.Convert(new List<DrawingPoint> { new DrawingPoint(),new DrawingPoint() },1000,1,false,false),
   ()=>DrawingTrajectoryConverter.Convert(new List<DrawingPoint> { new DrawingPoint(),new DrawingPoint { X=601 } },1000,1,false,false)
  }) { bool rejected=false; try { invalidDrawing(); } catch(ArgumentException) { rejected=true; } Check(rejected,"Invalid drawing rejected safely"); }
  var stepped=new Trajectory { Enabled=true,Step=true,Points=new List<TrajectoryPoint> { new TrajectoryPoint { Vertical=350,Horizontal=-20 },new TrajectoryPoint { TimeMs=1000,Vertical=250,Horizontal=30 },new TrajectoryPoint { TimeMs=2000 } } };
  Near(stepped.Evaluate(999.999).Vertical,350,"Step keeps 350 before boundary");
  Near(stepped.Evaluate(1000).Vertical,250,"Step switches exactly at boundary");
  Near(stepped.Evaluate(1500).Vertical,250,"Step has no ramp");
  Near(stepped.Integrate(0,2000).Vertical,600,"Step exact integral");
  Near(stepped.Integrate(990,1010).Vertical,6,"Step integrates across discontinuity");
  double steppedSum=0; for(int ms=0;ms<2000;ms+=17)steppedSum+=stepped.Integrate(ms,Math.Min(2000,ms+17)).Vertical;
  Near(steppedSum,600,"Step independent of interval");
  foreach(bool invert in new[]{false,true}) {
   var stepDrawing=DrawingTrajectoryConverter.Convert(drawn,2000,0.5,invert,false,true); double sign=invert?-1:1;
   Check(stepDrawing.Step&&stepDrawing.Points.Count==drawn.Count,"Step drawing uses one velocity per segment");
   for(int i=1;i<drawn.Count;i++) {
    var endPosition=stepDrawing.Integrate(0,stepDrawing.Points[i].TimeMs);
    Near(endPosition.Horizontal,sign*(drawn[i].X-drawn[0].X)*0.5,"Step drawing exact X endpoint");
    Near(endPosition.Vertical,sign*(drawn[i].Y-drawn[0].Y)*0.5,"Step drawing exact Y endpoint");
   }
   Check(stepDrawing.Evaluate(1).Horizontal!=0,"Step drawing does not wait until midpoint");
   Near(stepDrawing.Evaluate(stepDrawing.Points[1].TimeMs).Horizontal,stepDrawing.Points[1].Horizontal,"Step drawing changes at vertex");
  }
  var stepPreset=PresetEditingService.Copy(loaded); stepPreset.Name="Step mode"; stepPreset.PrimaryTrajectory=stepped.Copy();
  repo.SaveByIdentity(stepPreset); var stepLoaded=repo.LoadAll().Single(p=>p.Name=="Step mode");
  Check(stepLoaded.PrimaryTrajectory.Step,"Step survives JSON persistence");
  Near(stepLoaded.PrimaryTrajectory.Evaluate(999).Vertical,350,"Loaded step has same behavior");
  var stepClone=PresetEditingService.Copy(stepLoaded); Check(stepClone.PrimaryTrajectory.Step,"Step copied");
  stepClone.PrimaryTrajectory.Step=false; Check(!PresetEditingService.SameContent(stepLoaded,stepClone),"Step changes included in conflict detection");
  transfer.Export(export,new List<Preset> { stepLoaded }); Check(transfer.ReadAndValidate(export)[0].PrimaryTrajectory.Step,"Step survives export/import");
  var flowing=DrawingTrajectoryConverter.Convert(drawn,2000,0.5,false,true);
  for(int i=1;i<drawn.Count-1;i++) {
   int at=flowing.Points[2*i].TimeMs;
   var velocity=flowing.Evaluate(at); var before=flowing.Evaluate(at-0.001); var after=flowing.Evaluate(at+0.001);
   Check(Math.Abs(velocity.Horizontal)+Math.Abs(velocity.Vertical)>0.1,"Smooth drawing does not stop at interior vertex");
   Check(Math.Abs(before.Horizontal-after.Horizontal)<0.001&&Math.Abs(before.Vertical-after.Vertical)<0.001,"Velocity continuous at vertex");
   var farBefore=flowing.Evaluate(at-0.01); var farAfter=flowing.Evaluate(at+0.01);
   double accelerationBefore=(velocity.Vertical-farBefore.Vertical)/0.01,accelerationAfter=(farAfter.Vertical-velocity.Vertical)/0.01;
   Check(Math.Abs(accelerationBefore-accelerationAfter)<0.01,"Acceleration continuous at vertex");
  }
  var rightAngle=new List<DrawingPoint> { new DrawingPoint { X=50,Y=50 },new DrawingPoint { X=150,Y=50 },new DrawingPoint { X=150,Y=150 },new DrawingPoint { X=250,Y=150 } };
  var rounded=DrawingTrajectoryConverter.Convert(rightAngle,3000,1,false,true);
  for(int i=1;i<rightAngle.Count-1;i++) {
   var velocity=rounded.Evaluate(rounded.Points[i*2].TimeMs);
   Check(Math.Abs(velocity.Horizontal)+Math.Abs(velocity.Vertical)>1,"Right-angle turn must not stall");
  }
  // Earlier builds represented every drawing segment as a zero/peak/zero pulse.
  var legacyDrawing=new Trajectory { Enabled=true,Smooth=true,Points=new List<TrajectoryPoint> { new TrajectoryPoint(),new TrajectoryPoint { TimeMs=250,Horizontal=200,Vertical=300 },new TrajectoryPoint { TimeMs=500 },new TrajectoryPoint { TimeMs=750,Horizontal=-100,Vertical=300 },new TrajectoryPoint { TimeMs=1000 },new TrajectoryPoint { TimeMs=1250,Horizontal=100,Vertical=300 },new TrajectoryPoint { TimeMs=1500 } } };
  Check(DrawingTrajectoryConverter.CanSmoothLegacy(legacyDrawing),"Legacy drawing recognized");
  var repairedDrawing=DrawingTrajectoryConverter.SmoothLegacy(legacyDrawing);
  Check(repairedDrawing.Duration==legacyDrawing.Duration&&repairedDrawing.Smooth&&!repairedDrawing.Step,"Repair keeps duration and selects smooth mode");
  foreach(int at in new[]{0,500,1000,1500}) {
   var before=legacyDrawing.Integrate(0,at); var after=repairedDrawing.Integrate(0,at);
   Near(after.Horizontal,before.Horizontal,"Repair preserves spatial X endpoint"); Near(after.Vertical,before.Vertical,"Repair preserves spatial Y endpoint");
  }
  Check(repairedDrawing.Evaluate(500).Vertical>0&&repairedDrawing.Evaluate(1000).Vertical>0,"Repair removes intermediate stops");
  Near(legacyDrawing.Evaluate(500).Vertical,0,"Repair does not mutate original");
  var stationary=legacyDrawing.Copy(); stationary.Points[3].Horizontal=stationary.Points[3].Vertical=0;
  var repairedStationary=DrawingTrajectoryConverter.SmoothLegacy(stationary);
  Near(repairedStationary.Integrate(500,1000).Horizontal,0,"Explicit stationary phase preserved X"); Near(repairedStationary.Integrate(500,1000).Vertical,0,"Explicit stationary phase preserved Y");
  var repairedClone=PresetEditingService.Copy(stepLoaded); repairedClone.Name="Continuous drawing"; repairedClone.PrimaryTrajectory=repairedDrawing;
  repo.SaveByIdentity(repairedClone); var reloadedSmooth=repo.LoadAll().Single(p=>p.Name=="Continuous drawing").PrimaryTrajectory;
  Near(reloadedSmooth.Evaluate(500).Vertical,repairedDrawing.Evaluate(500).Vertical,"Continuous passage persists in existing format");
  var protectedDirectory=Path.Combine(dir,"protected-format");
  var protectedRepo=new PresetService(protectedDirectory,false); protectedRepo.SaveGame("Game");
  var basicOnly=new Preset { GameName="Game",Name="Basic",IntervalMilliseconds=7 };
  protectedRepo.SaveByIdentity(basicOnly);
  Check(File.ReadAllText(protectedRepo.FilePath).Contains("\"formatVersion\":2"),"Basic store stays v2 compatible");
  basicOnly.PrimaryTrajectory=Pattern(); protectedRepo.SaveByIdentity(basicOnly);
  Check(File.ReadAllText(protectedRepo.FilePath).Contains("\"formatVersion\":3"),"Advanced store requires protective v3 format");
  var newBackup=new PresetBackupService(protectedRepo.FilePath); var newBackupPath=newBackup.CreateBackup();
  Check(newBackup.ReadAndValidate(newBackupPath)[0].PrimaryTrajectory.Enabled,"V3 backups retain advanced trajectory");
  string oldAdvanced=File.ReadAllText(protectedRepo.FilePath).Replace("\"formatVersion\":3","\"formatVersion\":2");
  File.WriteAllText(protectedRepo.FilePath,oldAdvanced);
  Check(protectedRepo.LoadAll()[0].PrimaryTrajectory.Enabled,"Legacy v2 advanced store remains readable");
  protectedRepo.SaveByIdentity(protectedRepo.LoadAll()[0]);
  Check(File.ReadAllText(protectedRepo.FilePath).Contains("\"formatVersion\":3"),"Saving v2 advanced upgrades protectively");
  var protectedExport=Path.Combine(protectedDirectory,"advanced-export.json");
  transfer.Export(protectedExport,protectedRepo.LoadAll());
  Check(File.ReadAllText(protectedExport).Contains("\"formatVersion\":3"),"Advanced exports cannot be silently imported by v2-only app");
  Check(transfer.ReadAndValidate(protectedExport)[0].PrimaryTrajectory.Enabled,"V3 export import keeps data");
  var normalBasicCopy=PresetEditingService.Copy(protectedRepo.LoadAll()[0]); normalBasicCopy.PrimaryVertical=123;
  PresetEditingService.Save(protectedRepo,protectedRepo.LoadAll()[0],normalBasicCopy,()=>newBackup.CreateBackup());
  Check(protectedRepo.LoadAll()[0].PrimaryTrajectory.Points.Count==3,"Basic editing preserves advanced data");
  Console.WriteLine("Trajectory: "+count+" checks passed. Fixtures: "+dir);
 }
}
