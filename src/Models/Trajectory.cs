using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Serialization;

[DataContract]
internal sealed class TrajectoryPoint : IExtensibleDataObject
{
    public ExtensionDataObject ExtensionData { get; set; }
    [DataMember(Name="timeMs")] public int TimeMs { get; set; }
    [DataMember(Name="horizontal")] public double Horizontal { get; set; }
    [DataMember(Name="vertical")] public double Vertical { get; set; }
    internal TrajectoryPoint Copy() { return (TrajectoryPoint)MemberwiseClone(); }
}

// Components are velocities in pixels/second, matching the existing basic settings.
[DataContract]
internal sealed class Trajectory : IExtensibleDataObject
{
    public ExtensionDataObject ExtensionData { get; set; }
    internal const int MaxTime = 60000, MaxPoints = 128;
    [DataMember(Name="enabled")] public bool Enabled { get; set; }
    [DataMember(Name="step", EmitDefaultValue=false)] public bool Step { get; set; }
    internal string InterpolationName { get { return Step?"em degraus":Smooth?"suave":"linear"; } }
    [DataMember(Name="smooth")] public bool Smooth { get; set; }
    [DataMember(Name="holdLast")] public bool HoldLast { get; set; }
    [DataMember(Name="points")] public List<TrajectoryPoint> Points { get; set; }
    internal int Duration { get { return Points[Points.Count - 1].TimeMs; } }
    internal Trajectory Copy() { return new Trajectory { Enabled=Enabled, Step=Step, Smooth=Smooth, HoldLast=HoldLast, Points=Points.Select(p=>p.Copy()).ToList() }; }
    internal static Trajectory Create() { return new Trajectory { Points=new List<TrajectoryPoint> { new TrajectoryPoint(), new TrajectoryPoint { TimeMs=1000 } } }; }
    internal static Trajectory Normalize(Trajectory value)
    {
        if(value==null)return null;
        var points=(value.Points ?? new List<TrajectoryPoint>()).Where(p=>p!=null).Take(MaxPoints)
            .Select(p=>new TrajectoryPoint { TimeMs=Math.Max(0,Math.Min(MaxTime,p.TimeMs)), Horizontal=Finite(p.Horizontal,5000), Vertical=Finite(p.Vertical,10000) })
            .OrderBy(p=>p.TimeMs).GroupBy(p=>p.TimeMs).Select(g=>g.Last()).ToList();
        if(points.Count==0) { var empty=Create(); empty.Enabled=false; return empty; }
        if(points[0].TimeMs!=0)points.Insert(0,new TrajectoryPoint { Horizontal=points[0].Horizontal, Vertical=points[0].Vertical });
        if(points.Count==1)points.Add(new TrajectoryPoint { TimeMs=1000, Horizontal=points[0].Horizontal, Vertical=points[0].Vertical });
        return new Trajectory { Enabled=value.Enabled, Step=value.Step, Smooth=value.Smooth, HoldLast=value.HoldLast, Points=points.Take(MaxPoints).ToList() };
    }
    private static double Finite(double v,double limit) { return double.IsNaN(v)||double.IsInfinity(v)?0:Math.Max(-limit,Math.Min(limit,v)); }
    internal TrajectoryPoint Evaluate(double timeMs)
    {
        if(timeMs>=Duration)return HoldLast?Points[Points.Count-1].Copy():new TrajectoryPoint();
        if(timeMs<=0)return Points[0].Copy();
        int i=1; while(Points[i].TimeMs<timeMs)i++;
        var a=Points[i-1]; var b=Points[i]; double t=(timeMs-a.TimeMs)/(b.TimeMs-a.TimeMs);
        if(Step)return (timeMs>=b.TimeMs?b:a).Copy();
        if(Smooth)t=t*t*(3-2*t); // Bounded easing, with no overshoot of safe component limits.
        return new TrajectoryPoint { Horizontal=a.Horizontal+(b.Horizontal-a.Horizontal)*t, Vertical=a.Vertical+(b.Vertical-a.Vertical)*t };
    }
    // Exact integral of linear, smoothstep or held velocity, shared by runtime and preview.
    internal TrajectoryPoint Integrate(double fromMs,double toMs)
    {
        var result=new TrajectoryPoint(); fromMs=Math.Max(0,fromMs); toMs=Math.Max(fromMs,toMs);
        for(int i=1;i<Points.Count;i++)
        {
            var a=Points[i-1]; var b=Points[i]; double lo=Math.Max(fromMs,a.TimeMs), hi=Math.Min(toMs,b.TimeMs);
            if(hi<=lo)continue;
            double span=b.TimeMs-a.TimeMs, u=(lo-a.TimeMs)/span, v=(hi-a.TimeMs)/span;
            double weight=Primitive(v)-Primitive(u), dt=(hi-lo)/1000;
            result.Horizontal+=a.Horizontal*dt+(b.Horizontal-a.Horizontal)*span/1000*weight;
            result.Vertical+=a.Vertical*dt+(b.Vertical-a.Vertical)*span/1000*weight;
        }
        if(HoldLast&&toMs>Duration) { double dt=(toMs-Math.Max(fromMs,Duration))/1000; var last=Points[Points.Count-1]; result.Horizontal+=last.Horizontal*dt; result.Vertical+=last.Vertical*dt; }
        return result;
    }
    private double Primitive(double t) { return Step?0:Smooth?t*t*t-0.5*t*t*t*t:t*t/2; }
}

// No native input dependency: temporal state is directly testable.
internal sealed class MovementSession
{
    private bool active, requireRight, trajectoryEnabled; private Trajectory previousTrajectory;
    private long revision; private double timeMs, remainderX, remainderY;
    internal double TimeMs { get { return timeMs; } }
    internal TrajectoryPoint Step(MovementSettings settings,bool left,bool right,double elapsedSeconds)
    {
        bool moving=settings.Enabled&&left&&(!settings.RequireRightButton||right);
        bool usesTrajectory=settings.Trajectory!=null&&settings.Trajectory.Enabled;
        if(!moving||revision!=settings.Revision||!ReferenceEquals(previousTrajectory,settings.Trajectory)||requireRight!=settings.RequireRightButton||trajectoryEnabled!=usesTrajectory)
        { active=false; timeMs=remainderX=remainderY=0; revision=settings.Revision; }
        previousTrajectory=settings.Trajectory; requireRight=settings.RequireRightButton; trajectoryEnabled=usesTrajectory;
        if(!moving)return new TrajectoryPoint();
        if(!active) { active=true; if(usesTrajectory)return new TrajectoryPoint(); }
        double dt=Math.Max(0,elapsedSeconds);
        var trajectory=settings.Trajectory;
        var delta=trajectory!=null&&trajectory.Enabled?trajectory.Integrate(timeMs,timeMs+dt*1000):new TrajectoryPoint { Horizontal=settings.HorizontalPixelsPerSecond*dt, Vertical=settings.VerticalPixelsPerSecond*dt };
        if(usesTrajectory)timeMs+=dt*1000;
        remainderX+=delta.Horizontal; remainderY+=delta.Vertical;
        int x=(int)Math.Truncate(remainderX+Math.Sign(remainderX)*1e-9), y=(int)Math.Truncate(remainderY+Math.Sign(remainderY)*1e-9); remainderX-=x; remainderY-=y;
        return new TrajectoryPoint { Horizontal=x, Vertical=y };
    }
}
