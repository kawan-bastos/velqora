using System;
using System.Runtime.Serialization;

[DataContract]
internal enum WeaponSlot { Primary, Secondary }

[DataContract]
internal sealed class Preset : IExtensibleDataObject
{
    public ExtensionDataObject ExtensionData { get; set; }
    [DataMember(Name="primaryTrajectory", EmitDefaultValue=false)] public Trajectory PrimaryTrajectory { get; set; }
    [DataMember(Name="secondaryTrajectory", EmitDefaultValue=false)] public Trajectory SecondaryTrajectory { get; set; }
    [DataMember(Name="gameName", EmitDefaultValue=false)]
    public string GameName { get; set; }
    [DataMember(Name="characterName", EmitDefaultValue=false)]
    public string CharacterName { get; set; }
    [DataMember(Name="weaponName", EmitDefaultValue=false)]
    public string WeaponName { get; set; }
    [DataMember(Name="weaponSlot")] 
    public WeaponSlot WeaponSlot { get; set; }
    [DataMember(Name="name")]
    public string Name { get; set; }
    [DataMember(Name="primaryVertical")]
    public int PrimaryVertical { get; set; }
    [DataMember(Name="primaryHorizontal")]
    public int PrimaryHorizontal { get; set; }
    [DataMember(Name="secondaryVertical")]
    public int SecondaryVertical { get; set; }
    [DataMember(Name="secondaryHorizontal")]
    public int SecondaryHorizontal { get; set; }
    [DataMember(Name="intervalMilliseconds")]
    public int IntervalMilliseconds { get; set; }
    [DataMember(Name="secondaryIntervalMilliseconds", EmitDefaultValue=false)]
    public int? SecondaryIntervalMilliseconds { get; set; }
    [DataMember(Name="requireRightButton")]
    public bool RequireRightButton { get; set; }
    [DataMember(Name="createdAt")]
    public DateTime CreatedAt { get; set; }
    [DataMember(Name="updatedAt")]
    public DateTime UpdatedAt { get; set; }
}
