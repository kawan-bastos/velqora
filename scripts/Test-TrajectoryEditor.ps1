param([string]$AssemblyPath)
$ErrorActionPreference='Stop'
Add-Type -AssemblyName PresentationFramework,PresentationCore,WindowsBase
$root=Split-Path -Parent (Split-Path -Parent $PSCommandPath)
if(!$AssemblyPath) { $AssemblyPath=Join-Path $root 'bin\Release\net48\Velqora.exe' }
$assembly=[Reflection.Assembly]::LoadFrom($AssemblyPath)
[Windows.Application]::ResourceAssembly=$assembly
$app=New-Object MacroDoButler.App
$app.InitializeComponent()
$editor=New-Object MacroDoButler.TrajectoryEditor
$flags=[Reflection.BindingFlags]'Instance,NonPublic,Public'
function Field($obj,$name) { $obj.GetType().GetField($name,$flags).GetValue($obj) }
function Put($obj,$name,$value) { $obj.GetType().GetField($name,$flags).SetValue($obj,$value) }
function Invoke($obj,$name,$values) { $obj.GetType().GetMethod($name,$flags).Invoke($obj,$values) }
function Check($value,$message) { if(!$value){throw $message}; $script:checks++ }
$script:checks=0
$trajectoryType=$assembly.GetType('Trajectory')
$pointType=$assembly.GetType('TrajectoryPoint')
$draft=Field $editor 'draft'
$draft.Points.Clear()
foreach($spec in @(@(0,-150,220),@(250,50,270),@(500,200,100),@(750,-160,-140),@(1000,30,0),@(1400,0,0))) {
 $point=[Activator]::CreateInstance($pointType,$true); $point.TimeMs=$spec[0]; $point.Horizontal=$spec[1]; $point.Vertical=$spec[2]; $draft.Points.Add($point)
}
$draft.Enabled=$true
$draft.Smooth=$true
Invoke $editor 'Refresh' @() | Out-Null
$profiles=Field $editor 'Profiles'
Put $editor 'updating' $true
$preset=[Activator]::CreateInstance($assembly.GetType('Preset'),$true)
$preset.GameName='Exemplo'; $preset.Name='Trajetória em S'
$row=[Activator]::CreateInstance($assembly.GetType('MacroDoButler.TrajectoryEditor+ProfileRow'),$true)
$row.Preset=$preset
$profiles.Items.Add($row) | Out-Null
$profiles.SelectedIndex=0
Put $editor 'updating' $false
# Populate a display label only; no user profiles or native input are loaded.
Invoke $editor 'SelectPoint' @(2) | Out-Null
$x=Field $editor 'PointX'
$x.Text='25'
Check ($draft.Points[2].Horizontal -eq 250) 'Live point edit failed'
$x.Text='NaN'
Check (Field $editor 'invalid') 'Nonfinite input accepted'
Invoke $editor 'Refresh' @() | Out-Null
Invoke $editor 'MovePoint' @(2,250,$true) | Out-Null
Check ($draft.Points[2].TimeMs -eq 251) 'Duplicate drag not clamped'
Invoke $editor 'UndoClick' @($null,$null) | Out-Null
$draft=Field $editor 'draft'
Check ($draft.Points[2].TimeMs -eq 500) 'Drag undo failed'
$count=$draft.Points.Count
Invoke $editor 'AddClick' @($null,$null) | Out-Null
$draft=Field $editor 'draft'
Check ($draft.Points.Count -eq ($count+1)) 'Add failed'
Invoke $editor 'RemoveClick' @($null,$null) | Out-Null
Check ($draft.Points.Count -eq $count) 'Remove failed'
Invoke $editor 'SelectPoint' @(2) | Out-Null
Invoke $editor 'PlayClick' @($null,$null) | Out-Null
Check (Field $editor 'playing') 'Playback failed'
Invoke $editor 'RestartClick' @($null,$null) | Out-Null
Check (!(Field $editor 'playing')) 'Restart did not pause'
Check ((Field $editor 'time') -eq 0) 'Restart did not seek'
# Exercise the real ComboBox selection events, including preview after the last point.
$interpolation=Field $editor 'Interpolation'
$scrub=Field $editor 'Scrub'
$scrub.Value=62
$interpolation.SelectedIndex=0
$linear=(Invoke $draft 'Evaluate' @(62.0)).Horizontal
$interpolation.SelectedIndex=1
$smooth=(Invoke $draft 'Evaluate' @(62.0)).Horizontal
Check ([Math]::Abs($linear-$smooth) -gt 1) 'Interpolation choices do not change the evaluated movement'
Check ($draft.Smooth) 'Interpolation selection did not update draft'
Invoke $editor 'SelectPoint' @(($draft.Points.Count-1)) | Out-Null
(Field $editor 'PointX').Text='10'
$duration=$draft.GetType().GetProperty('Duration',$flags).GetValue($draft,$null)
$ending=Field $editor 'Ending'
$ending.SelectedIndex=0
Invoke $editor 'AdvancePreview' @(($duration+500.0)) | Out-Null
Check ((Field $editor 'time') -gt $duration) 'Preview does not demonstrate final phase'
Check ((Invoke $draft 'Evaluate' @(($duration+500.0))).Horizontal -eq 0) 'Zero ending does not stop'
$ending.SelectedIndex=1
Check ($draft.HoldLast) 'Ending selection did not update draft'
Check ((Invoke $draft 'Evaluate' @(($duration+500.0))).Horizontal -eq 100) 'Hold ending does not continue'
Check ((Field $editor 'Velocity').Text -match 'H 1') 'Final velocity display did not update live'
Invoke $editor 'PlayClick' @($null,$null) | Out-Null
Invoke $editor 'AdvancePreview' @(($duration+500.0)) | Out-Null
Check (Field $editor 'playing') 'Playback stops before demonstrating final phase'
Invoke $editor 'AdvancePreview' @(($duration+1000.0)) | Out-Null
Check (!(Field $editor 'playing')) 'Playback does not stop after final demonstration'
(Field $editor 'PointX').Text='0'
Check ((Field $editor 'EndingHelp').Text -match 'mesmo resultado') 'Zero last point equivalence is unexplained'
Invoke $editor 'RestartClick' @($null,$null) | Out-Null
# Test game filtering with 500 synthetic presets, including identical names across games.
$fixtureDirectory=Join-Path $root ('build-check\trajectory-game-filter-'+[Guid]::NewGuid().ToString('N'))
$constructor=$assembly.GetType('PresetService').GetConstructor($flags,$null,[Type[]]@([string]),$null)
$service=$constructor.Invoke([object[]]@([string]$fixtureDirectory))
foreach($game in @('Jogo A','Jogo B','Jogo vazio')) { Invoke $service 'SaveGame' @($game) | Out-Null }
$presets=[Array]::CreateInstance($assembly.GetType('Preset'),500)
for($i=0;$i -lt 500;$i++) {
 $item=[Activator]::CreateInstance($assembly.GetType('Preset'),$true)
 $item.Name='Preset '+($i % 250).ToString('000'); $item.GameName=if($i -lt 250){'Jogo A'}else{'Jogo B'}
 $item.IntervalMilliseconds=7
 $presets.SetValue($item,$i)
}
Invoke $service 'ReplaceAll' (, $presets) | Out-Null
$filterEditor=New-Object MacroDoButler.TrajectoryEditor
Invoke $filterEditor 'Configure' @($service,$null,$null,$presets[321]) | Out-Null
$games=Field $filterEditor 'Games'; $filtered=Field $filterEditor 'Profiles'
Check ($games.SelectedItem -eq 'Jogo B') 'Preferred game not selected'
Check ($filtered.Items.Count -eq 250) 'Presets not filtered by game'
Check (($filtered.SelectedItem).Preset.Name -eq 'Preset 071') 'Preferred preset lost'
Check (@($filtered.Items | Where-Object {$_.Preset.GameName -ne 'Jogo B'}).Count -eq 0) 'Other games leaked into preset list'
$games.SelectedItem='Jogo A'
Check ($filtered.Items.Count -eq 250) 'Changing game did not populate presets'
Check (@($filtered.Items | Where-Object {$_.Preset.GameName -ne 'Jogo A'}).Count -eq 0) 'Changing game left other games in list'
$games.SelectedItem='Jogo vazio'
Check ($filtered.Items.Count -eq 0 -and !$filtered.IsEnabled) 'Empty game shows stale presets'
$games.SelectedItem='Jogo B'
Check ($filtered.Items.Count -eq 250 -and $filtered.IsEnabled) 'Returning from empty game failed'
# Use the actual filtered selectors in the layout render while retaining the sample curve.
Put $editor 'updating' $true
(Field $editor 'Games').Items.Add('Exemplo') | Out-Null
(Field $editor 'Games').SelectedIndex=0
Put $editor 'updating' $false
$background=New-Object Windows.Controls.Border
$background.Background=[Windows.Media.BrushConverter]::new().ConvertFromString('#0C1012')
$background.Child=$editor
foreach($size in @(@(1100,920),@(885,820))) {
 $width=$size[0];$height=$size[1]
 $background.Width=$width; $background.Height=$height
 $background.Measure([Windows.Size]::new($width,$height));$background.Arrange([Windows.Rect]::new(0,0,$width,$height));$background.UpdateLayout()
 Invoke $editor 'Refresh' @() | Out-Null
 $bitmap=[Windows.Media.Imaging.RenderTargetBitmap]::new($width,$height,96,96,[Windows.Media.PixelFormats]::Pbgra32)
 $bitmap.Render($background)
 $encoder=[Windows.Media.Imaging.PngBitmapEncoder]::new();$encoder.Frames.Add([Windows.Media.Imaging.BitmapFrame]::Create($bitmap))
 $file=[IO.File]::Create((Join-Path $root ('build-check\trajectory-'+$width+'.png')));$encoder.Save($file);$file.Dispose()
}
# Verify the details card without constructing MainWindow or starting native input.
$summary=New-Object MacroDoButler.PresetMovementSummary
$summaryPreset=[Activator]::CreateInstance($assembly.GetType('Preset'),$true)
$summaryPreset.PrimaryTrajectory=$draft
$draft.Enabled=$true
Invoke $summary 'ShowSettings' @($summaryPreset,$false,$true,999,888) | Out-Null
Check ((Field $summary 'ModeText').Text -match 'AVANÇADA.*PRIMÁRIA') 'Details omit primary advanced mode'
Check ((Field $summary 'AdvancedDetails').Visibility -eq 'Visible') 'Advanced details are hidden'
Check ((Field $summary 'HorizontalValue').Text -notmatch '999') 'Details display unused basic horizontal value'
Check ((Field $summary 'VerticalValue').Text -notmatch '888') 'Details display unused basic vertical value'
Check ((Field $summary 'TrajectoryInfo').Text -match '1400 ms') 'Details omit duration'
Check ((Field $summary 'MiniPlot').GetType().GetField('Value',$flags).GetValue((Field $summary 'MiniPlot')) -eq $draft) 'Mini plot does not use saved trajectory'
Check (Field (Field $summary 'MiniPlot') 'StaticPreview') 'Summary plot shows a playback marker'
Invoke $summary 'ShowSettings' @($summaryPreset,$true,$true,25,40) | Out-Null
Check ((Field $summary 'ModeText').Text -match 'BÁSICA.*SECUNDÁRIA') 'Weapon switch retains primary mode'
Check ((Field $summary 'AdvancedDetails').Visibility -eq 'Collapsed') 'Basic weapon retains stale graph'
Check ((Field $summary 'HorizontalValue').Text.Replace(',','.') -eq '3 de força') 'Secondary basic horizontal is incorrect'
Check ((Field $summary 'VerticalValue').Text.Replace(',','.') -eq '0 de força') 'Secondary basic vertical is incorrect'
$summaryPreset.SecondaryTrajectory=$draft
Invoke $summary 'ShowSettings' @($summaryPreset,$true,$false,25,40) | Out-Null
Check ((Field $summary 'ModeText').Text -match 'AVANÇADA.*SECUNDÁRIA') 'Secondary advanced mode missing'
Check ((Field $summary 'MacroState').Text -match 'desligado') 'Details falsely claim macro is active'
$draft.Enabled=$false
Invoke $summary 'ShowSettings' @($summaryPreset,$false,$true,12,34) | Out-Null
Check ((Field $summary 'ModeText').Text -match 'BÁSICA') 'Disabled trajectory still shown as advanced'
Check ((Field $summary 'AdvancedDetails').Visibility -eq 'Collapsed') 'Disabled trajectory graph remains visible'
$draft.Enabled=$true
Invoke $summary 'ShowSettings' @($null,$false,$false,0,0) | Out-Null
Check ($summary.Visibility -eq 'Collapsed') 'Empty selection retains old summary'
Invoke $summary 'ShowSettings' @($summaryPreset,$false,$true,12,34) | Out-Null
$summaryBorder=New-Object Windows.Controls.Border
$summaryBorder.Background=[Windows.Media.BrushConverter]::new().ConvertFromString('#111719')
$summaryBorder.Padding=[Windows.Thickness]::new(18)
$summaryBorder.Child=$summary
$summaryBorder.Measure([Windows.Size]::new(680,600))
$summaryBorder.Arrange([Windows.Rect]::new(0,0,680,$summaryBorder.DesiredSize.Height))
$summaryBorder.UpdateLayout()
$bitmap=[Windows.Media.Imaging.RenderTargetBitmap]::new(680,[int][Math]::Ceiling($summaryBorder.ActualHeight),96,96,[Windows.Media.PixelFormats]::Pbgra32)
$bitmap.Render($summaryBorder)
$encoder=[Windows.Media.Imaging.PngBitmapEncoder]::new();$encoder.Frames.Add([Windows.Media.Imaging.BitmapFrame]::Create($bitmap))
$file=[IO.File]::Create((Join-Path $root 'build-check\preset-details-advanced.png'));$encoder.Save($file);$file.Dispose()
# Experimental point drawing: edit, undo, reference image, conversion and parent undo.
$drawingEditor=Field $editor 'Drawing'
$surface=Field $drawingEditor 'Surface'
(Field $editor 'DrawingSection').IsExpanded=$true
Check ($editor.GetType().GetProperty('IsDrawingOpen',$flags).GetValue($editor,$null)) 'Drawing pause state not exposed'
(Field $editor 'DrawingSection').IsExpanded=$false
Check (!$editor.GetType().GetProperty('IsDrawingOpen',$flags).GetValue($editor,$null)) 'Drawing pause state remains after closing'
Invoke $surface 'AddAt' @(200.0,20.0) | Out-Null
Invoke $surface 'AddAt' @(100.0,100.0) | Out-Null
Invoke $surface 'AddAt' @(300.0,200.0) | Out-Null
Invoke $surface 'AddAt' @(200.0,300.0) | Out-Null
Check ((Field $surface 'Points').Count -eq 4) 'Drawing click points not added'
Check ($drawingEditor.HasChanges -or $drawingEditor.GetType().GetProperty('HasChanges',$flags).GetValue($drawingEditor,$null)) 'Drawing does not track unsaved edits'
Invoke $surface 'MoveTo' @(1,150.0,100.0) | Out-Null
Check ((Field $surface 'Points')[1].X -eq 150) 'Drawing drag did not change point'
Invoke $surface 'RemoveAt' @(1) | Out-Null
Check ((Field $surface 'Points').Count -eq 3) 'Drawing removal failed'
Invoke $drawingEditor 'UndoClick' @($null,$null) | Out-Null
Check ((Field $surface 'Points').Count -eq 4) 'Drawing undo failed'
$referencePath=Join-Path $root 'build-check\drawing-reference-test.png'
[IO.File]::Copy((Join-Path $root 'assets\Velqora.png'),$referencePath,$true)
Invoke $drawingEditor 'LoadReferenceImage' @([string]$referencePath) | Out-Null
Check ((Field $surface 'ReferenceImage') -ne $null) 'Reference image did not load'
# OnLoad decoding must release the file immediately.
$file=[IO.File]::Open($referencePath,[IO.FileMode]::Open,[IO.FileAccess]::ReadWrite,[IO.FileShare]::None);$file.Dispose()
Check $true 'Reference file remains locked'
# View operations transform only the background, preserving all traced positions.
$pointSnapshot=@((Field $surface 'Points') | ForEach-Object { "$($_.X),$($_.Y)" }) -join ';'
(Field $drawingEditor 'ImageZoom').Value=2
Check ((Field $surface 'ReferenceZoom') -eq 2) 'Zoom slider did not update image'
Check ((Field $drawingEditor 'ImageZoomText').Text -eq '200%') 'Zoom percentage is wrong'
Invoke $drawingEditor 'ResetImageViewClick' @($null,$null) | Out-Null
Invoke $drawingEditor 'RotateImageClick' @($null,$null) | Out-Null
$matrix=$surface.GetType().GetProperty('ReferenceTransform',$flags).GetValue($surface,$null)
Check ($matrix.M11 -eq -1 -and $matrix.M22 -eq -1) '180 degree image rotation is wrong'
Invoke $surface 'PanReference' @(50.0,-25.0) | Out-Null
Check ((Field $surface 'ReferenceOffsetX') -eq 50 -and (Field $surface 'ReferenceOffsetY') -eq -25) 'Image pan did not update'
Invoke $surface 'SetReferenceZoom' @(20.0) | Out-Null
Check ((Field $surface 'ReferenceZoom') -eq 6) 'Image zoom exceeds safe view range'
Check ((Field $drawingEditor 'ImageZoom').Value -eq 6) 'Wheel zoom did not synchronize slider'
$afterViewSnapshot=@((Field $surface 'Points') | ForEach-Object { "$($_.X),$($_.Y)" }) -join ';'
Check ($pointSnapshot -eq $afterViewSnapshot) 'Image transform moved drawn points'
Invoke $drawingEditor 'ResetImageViewClick' @($null,$null) | Out-Null
Check ((Field $surface 'ReferenceZoom') -eq 1 -and (Field $surface 'ReferenceOffsetX') -eq 0 -and (Field $surface 'ReferenceOffsetY') -eq 0) 'Fit image did not reset zoom and pan'
Check (!(Field $surface 'ReferenceRotated')) 'Fit image did not reset rotation'
Check ((Field $drawingEditor 'Invert').IsChecked) 'Recoil tracing does not default to opposite movement'
Invoke $drawingEditor 'RemoveImageClick' @($null,$null) | Out-Null
Check ((Field $surface 'ReferenceImage') -eq $null) 'Reference image not removed'
$beforeDrawing=Field $editor 'draft'
Invoke $editor 'SaveClick' @($null,$null) | Out-Null
Check ((Field $editor 'Status').Text -match 'ainda não aplicado') 'Saving silently ignores unapplied drawing'
(Field $drawingEditor 'DurationInput').Text='2000'
(Field $drawingEditor 'ScaleInput').Text='1'
Invoke $drawingEditor 'ApplyClick' @($null,$null) | Out-Null
$afterDrawing=Field $editor 'draft'
Check (![object]::ReferenceEquals($beforeDrawing,$afterDrawing)) 'Drawing not applied to parent draft'
Check ($afterDrawing.Points.Count -eq 7 -and $afterDrawing.Enabled) 'Drawing did not generate enabled keyframes'
foreach($point in $afterDrawing.Points) {
 Check ($point.Horizontal % 10 -eq 0 -and $point.Vertical % 100 -eq 0) 'Drawing creates fractional force keyframes'
}
$drawnEnd=Invoke $afterDrawing 'Integrate' @(0.0,2000.0)
Check ($drawnEnd.Vertical -lt 0) 'Compensation does not oppose traced vertical movement'
Check ((Field $editor 'time') -eq 0) 'Applying drawing did not reset preview'
Check (!$drawingEditor.GetType().GetProperty('HasChanges',$flags).GetValue($drawingEditor,$null)) 'Applied drawing remains pending'
Invoke $editor 'UndoClick' @($null,$null) | Out-Null
Check ((Field $editor 'draft').Points.Count -eq $beforeDrawing.Points.Count) 'Parent undo did not restore previous trajectory'
# Invalid conversion must leave the trajectory untouched.
$beforeInvalid=Field $editor 'draft'
(Field $drawingEditor 'DurationInput').Text='bad'
Invoke $drawingEditor 'ApplyClick' @($null,$null) | Out-Null
Check ([object]::ReferenceEquals($beforeInvalid,(Field $editor 'draft'))) 'Invalid drawing replaced trajectory'
(Field $drawingEditor 'DurationInput').Text='2000'
Invoke $drawingEditor 'ApplyClick' @($null,$null) | Out-Null
# Render the actual drawing surface and its controls as a standalone WPF artifact.
$drawContainer=New-Object Windows.Controls.Border
$drawContainer.Background=[Windows.Media.BrushConverter]::new().ConvertFromString('#111719')
$drawContainer.Padding=[Windows.Thickness]::new(18)
# Reparent only in this isolated test process.
$drawParent=[Windows.Media.VisualTreeHelper]::GetParent($drawingEditor)
if($drawParent -is [Windows.Controls.ContentControl]) { $drawParent.Content=$null }
elseif($drawParent -is [Windows.Controls.Decorator]) { $drawParent.Child=$null }
else { (Field $editor 'DrawingSection').Content=$null }
$drawContainer.Child=$drawingEditor
$drawContainer.Measure([Windows.Size]::new(1000,1000))
$drawContainer.Arrange([Windows.Rect]::new(0,0,1000,$drawContainer.DesiredSize.Height));$drawContainer.UpdateLayout()
$bitmap=[Windows.Media.Imaging.RenderTargetBitmap]::new(1000,[int][Math]::Ceiling($drawContainer.ActualHeight),96,96,[Windows.Media.PixelFormats]::Pbgra32)
$bitmap.Render($drawContainer)
$encoder=[Windows.Media.Imaging.PngBitmapEncoder]::new();$encoder.Frames.Add([Windows.Media.Imaging.BitmapFrame]::Create($bitmap))
$file=[IO.File]::Create((Join-Path $root 'build-check\trajectory-drawing.png'));$encoder.Save($file);$file.Dispose()
# Step interpolation is wired through the UI and drawing conversion.
(Field $editor 'Interpolation').SelectedIndex=2
$stepDraft=Field $editor 'draft'
Check ($stepDraft.Step -and !$stepDraft.Smooth) 'Step interpolation selector does not update draft'
Check ((Field $editor 'InterpolationHelp').Text -match 'sem rampa') 'Step explanation missing'
Invoke $drawingEditor 'ApplyClick' @($null,$null) | Out-Null
$stepDraft=Field $editor 'draft'
Check ($stepDraft.Step -and $stepDraft.Points.Count -eq (Field $surface 'Points').Count) 'Drawing ignores selected step mode'
(Field $filterEditor 'Games').SelectedItem='Jogo A'
# The provider-facing snapshot changes before Save and remains immutable between polls.
$livePreset=Field $filterEditor 'original'
$storePath=$service.GetType().GetProperty('FilePath',$flags).GetValue($service,$null)
$beforeLiveFile=[IO.File]::ReadAllText($storePath)
(Field $filterEditor 'UseTrajectory').IsChecked=$true
(Field $filterEditor 'PointX').Text='2'
(Field $filterEditor 'PointY').Text='1'
$liveBefore=Invoke $filterEditor 'GetLiveSettings' @($true,$false)
Check ($liveBefore.Enabled -and $liveBefore.Trajectory.Enabled) 'Live draft is not enabled for execution'
Check ($liveBefore.Trajectory.Points[0].Horizontal -eq 20) 'Live provider ignores unsaved edits'
$liveRepeated=Invoke $filterEditor 'GetLiveSettings' @($true,$false)
Check ([object]::ReferenceEquals($liveBefore.Trajectory,$liveRepeated.Trajectory)) 'Polling recreates trajectory and restarts time'
Check ($liveBefore.Revision -eq $liveRepeated.Revision) 'Polling changes temporal revision'
(Field $filterEditor 'PointX').Text='4'
$liveAdjusted=Invoke $filterEditor 'GetLiveSettings' @($true,$false)
Check ($liveAdjusted.Trajectory.Points[0].Horizontal -eq 40) 'Numeric edit is not live before Save'
Check ($liveAdjusted.Trajectory.Points[0].Vertical -eq 100) 'Horizontal edit changes vertical'
Check ($liveAdjusted.Revision -gt $liveBefore.Revision) 'Live edit does not reset temporal session'
Check ($liveBefore.Trajectory.Points[0].Horizontal -eq 20) 'Live edit mutates in-flight snapshot'
Check ($livePreset.PrimaryTrajectory -eq $null) 'Live edit mutates saved preset object'
Check ([IO.File]::ReadAllText($storePath) -eq $beforeLiveFile) 'Live testing writes to presets file'
Invoke $filterEditor 'UndoClick' @($null,$null) | Out-Null
$liveUndone=Invoke $filterEditor 'GetLiveSettings' @($true,$false)
Check ($liveUndone.Trajectory.Points[0].Horizontal -eq 20) 'Undo does not update live execution'
Check ($liveUndone.Revision -gt $liveAdjusted.Revision) 'Undo does not reset temporal session'
$otherWeapon=Invoke $filterEditor 'GetLiveSettings' @($true,$true)
Check ($otherWeapon.Trajectory -eq $null) 'Editing primary leaks into secondary trajectory'
Check ($otherWeapon.VerticalPixelsPerSecond -eq $livePreset.SecondaryVertical) 'Other weapon ignores saved basic values'
$disabledLive=Invoke $filterEditor 'GetLiveSettings' @($false,$false)
Check (!$disabledLive.Enabled) 'Live preview bypasses macro disabled state'
(Field $filterEditor 'DrawingSection').IsExpanded=$true
Check (!(Invoke $filterEditor 'GetLiveSettings' @($true,$false)).Enabled) 'Live execution moves while drawing'
(Field $filterEditor 'DrawingSection').IsExpanded=$false
(Field $filterEditor 'PointX').Text='invalid'
Check (!(Invoke $filterEditor 'GetLiveSettings' @($true,$false)).Enabled) 'Invalid field does not pause live execution'
(Field $filterEditor 'PointX').Text='2'
Check ((Invoke $filterEditor 'GetLiveSettings' @($true,$false)).Enabled) 'Correcting field does not resume live execution'
Invoke $filterEditor 'RestoreClick' @($null,$null) | Out-Null
$restoredLive=Invoke $filterEditor 'GetLiveSettings' @($true,$false)
Check (!$restoredLive.Trajectory.Enabled) 'Restore does not restore saved basic mode'
Check ([IO.File]::ReadAllText($storePath) -eq $beforeLiveFile) 'Restore changes saved presets'
# Selecting points redraws the graph but must not restart the firing timeline.
$revisionBeforeSelection=$restoredLive.Revision
Invoke $filterEditor 'SelectPoint' @(1) | Out-Null
Check ((Invoke $filterEditor 'GetLiveSettings' @($true,$false)).Revision -eq $revisionBeforeSelection) 'Point selection restarts execution'
# Compact force values preserve the movement in both editors and saved data.
$forceConverter=New-Object MacroDoButler.ForceValueConverter
$invariant=[Globalization.CultureInfo]::InvariantCulture
foreach($axis in @('Vertical','Horizontal')) {
 $unit=if($axis -eq 'Horizontal'){10}else{100}
 $allRounded=$true
 foreach($pixels in -10000..10000) {
  $display=$forceConverter.Convert($pixels,[string],$axis,$invariant)
  $expected=[Math]::Round($pixels/[double]$unit,[MidpointRounding]::AwayFromZero)*$unit
  if($forceConverter.ConvertBack($display,[int],$axis,$invariant) -ne $expected -or $display -match '[.,]') { $allRounded=$false;break }
 }
 Check $allRounded 'Force does not round consistently to whole units'
 foreach($text in @('21.5','21,5','0.4','0,4','2.0','2,0')) {
  Check ([object]::ReferenceEquals($forceConverter.ConvertBack($text,[int],$axis,$invariant),[Windows.DependencyProperty]::UnsetValue)) 'Force converter accepts a decimal'
 }
}
Check ($forceConverter.Convert(3000,[double],$null,$invariant) -eq 30) 'Vertical scale changed'
Check ($forceConverter.Convert(2150,[double],$null,$invariant) -eq 22) 'Positive half is not rounded away from zero'
Check ($forceConverter.Convert(-2150,[double],$null,$invariant) -eq -22) 'Negative half is not rounded away from zero'
Check ($forceConverter.ConvertBack('6',[int],'Horizontal',$invariant) -eq 60) 'Lateral scale changed'
Check ($forceConverter.Convert(5000,[double],'Horizontal',$invariant) -eq 500) 'Lateral limit changed'
# Parse the actual preset editor fields without creating MainWindow or native input.
$app.Resources.Add('ForceValueConverter',$forceConverter)
[xml]$mainMarkup=[IO.File]::ReadAllText((Join-Path $root 'src/UI/MainWindow.xaml'))
$ns=New-Object Xml.XmlNamespaceManager($mainMarkup.NameTable)
$ns.AddNamespace('w','http://schemas.microsoft.com/winfx/2006/xaml/presentation')
$basicPreset=[Activator]::CreateInstance($assembly.GetType('Preset'),$true)
$basicPreset.PrimaryVertical=3000;$basicPreset.PrimaryHorizontal=-2150
$basicPreset.SecondaryVertical=10000;$basicPreset.SecondaryHorizontal=5000
$draftConstructor=$assembly.GetType('PresetEditDraft').GetConstructor($flags,$null,[Type[]]@($basicPreset.GetType(),[bool]),$null)
$basicDraft=$draftConstructor.Invoke([object[]]@($basicPreset,$false))
foreach($property in @('PrimaryVertical','PrimaryHorizontal','SecondaryVertical','SecondaryHorizontal')) {
 $lateral=$property.Contains('Horizontal');$axis=if($lateral){'Horizontal'}else{$null};$unit=if($lateral){10}else{100}
 $bindingNode=$mainMarkup.SelectSingleNode("//w:TextBox/w:TextBox.Text/w:Binding[@Path='$property']",$ns)
 $fieldMarkup=$bindingNode.ParentNode.ParentNode.OuterXml.Replace('clr-namespace:MacroDoButler"','clr-namespace:MacroDoButler;assembly=Velqora"')
 $sliderMarkup=$mainMarkup.SelectSingleNode("//w:Slider[contains(@Value,'Binding $property,')]",$ns).OuterXml
 $panel=[Windows.Markup.XamlReader]::Parse('<StackPanel xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation" xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml" xmlns:local="clr-namespace:MacroDoButler;assembly=Velqora">'+$sliderMarkup+$fieldMarkup+'</StackPanel>')
 $panel.DataContext=$basicDraft
 $slider=$panel.Children[0];$input=$panel.Children[1]
 $panel.Measure([Windows.Size]::new(600,200));$panel.Arrange([Windows.Rect]::new(0,0,600,200));$panel.UpdateLayout()
 $raw=$basicDraft.GetType().GetProperty($property).GetValue($basicDraft,$null)
 Check ($input.Text -eq $forceConverter.Convert($raw,[string],$axis,$invariant)) 'Basic force is not formatted from the original preset'
 Check ($slider.Value -eq $raw/[double]$unit) 'Basic slider uses raw pixels'
 foreach($entry in @('21','-6','0')) {
  if($entry -eq '-6' -and !$lateral){continue}
  $input.Text=$entry
  $input.GetBindingExpression([Windows.Controls.TextBox]::TextProperty).UpdateSource()
  Check (![Windows.Controls.Validation]::GetHasError($input)) 'Basic editor rejects integer force'
  Check ($basicDraft.GetType().GetProperty($property).GetValue($basicDraft,$null) -eq ([int]$entry*$unit)) 'Integer edit has wrong force'
 }
 foreach($entry in @('0.4','0,4','21.5','21,5','2.0')) {
  $beforeDecimal=$basicDraft.GetType().GetProperty($property).GetValue($basicDraft,$null)
  $input.Text=$entry;$input.GetBindingExpression([Windows.Controls.TextBox]::TextProperty).UpdateSource()
  Check ([Windows.Controls.Validation]::GetHasError($input)) 'Basic editor accepts decimal force'
  Check ($basicDraft.GetType().GetProperty($property).GetValue($basicDraft,$null) -eq $beforeDecimal) 'Rejected decimal changes force'
 }
 $beforeInvalid=$basicDraft.GetType().GetProperty($property).GetValue($basicDraft,$null)
 $input.Text='1000000';$input.GetBindingExpression([Windows.Controls.TextBox]::TextProperty).UpdateSource()
 Check ([Windows.Controls.Validation]::GetHasError($input)) 'Basic force limit is not enforced'
 $message=[string][Windows.Controls.Validation]::GetErrors($input)[0].ErrorContent
 Check ($message -match '^Valor (vertical|horizontal) excedido[.] Escolha um valor entre ') 'Basic error is not specific to the field'
 Check ($message -notmatch '5000|10000|Tempo permitido') 'Basic validation exposes raw limits'
 Check ($basicDraft.GetType().GetProperty($property).GetValue($basicDraft,$null) -eq $beforeInvalid) 'Invalid basic edit changes movement'
 $input.Text='21';$input.GetBindingExpression([Windows.Controls.TextBox]::TextProperty).UpdateSource()
 $slider.Value=12;$slider.GetBindingExpression([Windows.Controls.Primitives.RangeBase]::ValueProperty).UpdateSource()
 Check ($basicDraft.GetType().GetProperty($property).GetValue($basicDraft,$null) -eq (12*$unit)) 'Basic slider changes force scale'
}
Check ($basicPreset.PrimaryVertical -eq 3000 -and $basicPreset.PrimaryHorizontal -eq -2150) 'Display editing modifies the original saved preset'
Invoke $filterEditor 'SelectPoint' @(1) | Out-Null
$beforeCompact=Field $filterEditor 'draft'
$verticalBeforeLateral=(Field $filterEditor 'draft').Points[1].Vertical
(Field $filterEditor 'PointX').Text='6'
$lateralLive=Invoke $filterEditor 'GetLiveSettings' @($true,$false)
Check ($lateralLive.Trajectory.Points[1].Horizontal -eq 60) 'Lateral 6 changes original 0.6 physical force in live testing'
Check ($lateralLive.Trajectory.Points[1].Vertical -eq $verticalBeforeLateral) 'Lateral scale changes vertical force'

foreach($fieldName in @('PointX','PointY')) {
 $field=Field $filterEditor $fieldName;$beforeDecimal=Field $filterEditor 'draft'
 foreach($entry in @('0.4','0,4','21.50','30.00')) {
  $field.Text=$entry
  Check (Field $filterEditor 'invalid') 'Advanced editor accepts decimal force'
  Check ((Field $filterEditor 'Status').Text -match 'número inteiro') 'Decimal error does not ask for an integer'
 }
 $field.Text=if($fieldName -eq 'PointX'){'22'}else{'30'}
}
Check ((Field $filterEditor 'draft').Points[1].Horizontal -eq 220) 'Advanced integer lateral has wrong force'
Check ((Field $filterEditor 'draft').Points[1].Vertical -eq 3000) 'Advanced integer vertical has wrong force'
Invoke $filterEditor 'SelectPoint' @(1) | Out-Null
Check ((Field $filterEditor 'PointX').Text -eq '22') 'Advanced field is not integer'
foreach($spec in @(@('PointX','1000000','horizontal','-500','500'),@('PointY','1000000','vertical','-100','100'),@('PointTime','1000000','de tempo','1','60000'))) {
 $field=Field $filterEditor $spec[0];$originalText=$field.Text
 $field.Text=$spec[1]
 Check (Field $filterEditor 'invalid') 'Excess value is accepted'
 $expected='Valor '+$spec[2]+' excedido. Escolha um valor entre '+$spec[3]+' e '+$spec[4]+'.'
 Check ((Field $filterEditor 'Status').Text -eq $expected) 'Advanced error mixes unrelated fields'
 $field.Text=$originalText
}
# Rounded copies preserve metadata, keyframe times and interpolation flags.
$roundType=$assembly.GetType('MacroDoButler.ForceUnits')
$staticFlags=[Reflection.BindingFlags]'Static,NonPublic,Public'
$roundMethod=$roundType.GetMethod('RoundedCopy',$staticFlags)
$roundOriginal=[Activator]::CreateInstance($assembly.GetType('Preset'),$true)
$roundOriginal.Name='Preservar';$roundOriginal.PrimaryVertical=2150;$roundOriginal.PrimaryHorizontal=-65
$roundOriginal.PrimaryTrajectory=Invoke (Field $filterEditor 'draft') 'Copy' @()
$roundOriginal.PrimaryTrajectory.Points[1].Horizontal=-65.8
$roundOriginal.PrimaryTrajectory.Points[1].Vertical=2150.1
$rounded=$roundMethod.Invoke($null,([object[]]@($roundOriginal)))
Check ($rounded.PrimaryVertical -eq 2200 -and $rounded.PrimaryHorizontal -eq -70) 'Basic saved values are not rounded in their own units'
Check ($rounded.PrimaryTrajectory.Points[1].Horizontal -eq -70 -and $rounded.PrimaryTrajectory.Points[1].Vertical -eq 2200) 'Advanced saved values are not rounded'
Check ($roundOriginal.PrimaryVertical -eq 2150 -and $roundOriginal.PrimaryTrajectory.Points[1].Horizontal -eq -65.8) 'Rounding mutates the original before saving'
Check ($rounded.Name -eq $roundOriginal.Name -and $rounded.PrimaryTrajectory.Points[1].TimeMs -eq $roundOriginal.PrimaryTrajectory.Points[1].TimeMs -and $rounded.PrimaryTrajectory.Step -eq $roundOriginal.PrimaryTrajectory.Step) 'Rounding changes metadata or timing'
Check ([IO.File]::ReadAllText($storePath) -eq $beforeLiveFile) 'Compact force conversion rewrites saved data'
Invoke $filterEditor 'RestoreClick' @($null,$null) | Out-Null
# Long decimals from drawings are rounded only for presentation.
$compactSummary=New-Object MacroDoButler.PresetMovementSummary
$compactPreset=[Activator]::CreateInstance($assembly.GetType('Preset'),$true)
$compactTrajectory=[Activator]::CreateInstance($trajectoryType,$true)
$compactTrajectory.Enabled=$true
$compactTrajectory.Points=[Activator]::CreateInstance([System.Collections.Generic.List[object]].GetGenericTypeDefinition().MakeGenericType($pointType))
$firstPoint=[Activator]::CreateInstance($pointType,$true)
$lastPoint=[Activator]::CreateInstance($pointType,$true);$lastPoint.TimeMs=1000
$compactTrajectory.Points.Add($firstPoint);$compactTrajectory.Points.Add($lastPoint)
$compactPreset.PrimaryTrajectory=$compactTrajectory
foreach($case in @(@(-669.028,571.541,'-67 a 57 de força'),@(-130.797,87.816,'-13 a 9 de força'))) {
 $firstPoint.Horizontal=$case[0];$lastPoint.Horizontal=$case[1]
 Invoke $compactSummary 'ShowSettings' @($compactPreset,$false,$true,0,0) | Out-Null
 Check ((Field $compactSummary 'HorizontalValue').Text.Replace(',','.') -eq $case[2]) 'Summary shows long force decimals'
 Check ($firstPoint.Horizontal -eq $case[0] -and $lastPoint.Horizontal -eq $case[1]) 'Formatting changes saved trajectory precision'
}
Invoke $compactSummary 'ShowSettings' @($compactPreset,$true,$true,60,3000) | Out-Null
Check ((Field $compactSummary 'HorizontalValue').Text -eq '6 de força') 'Existing Deimos lateral force does not show as 6'
Check ((Field $compactSummary 'VerticalValue').Text -eq '30 de força') 'Vertical summary scale changed'
# Removed controls cannot return through XAML-generated fields.
foreach($removed in @('Overview','HorizontalAdjustment','VerticalAdjustment','LiveTestStatus')) {
 Check ($editor.GetType().GetField($removed,$flags) -eq $null) "Removed control still exists: $removed"
}
foreach($removed in @('ImageFlipVertical','ImageFlipHorizontal')) {
 Check ($drawingEditor.GetType().GetField($removed,$flags) -eq $null) "Removed image control still exists: $removed"
}
# Explanations remain in hover tooltips rather than taking page space.
foreach($name in @('InterpolationHelp','EndingHelp')) {
 $text=Field $editor $name
 Check ($text.Parent -is [Windows.Controls.ToolTip]) "Help is visible inline: $name"
 Check ($text.Text.Length -gt 0) "Help explanation is empty: $name"
}
Check ((Field $drawingEditor 'DrawingTimingHelp').Parent -is [Windows.Controls.ToolTip]) 'Drawing timing help is visible inline'
# Verify actual tooltip template output, including string help used by other pages.
function VisualTextBlocks($visual) {
 if($visual -is [Windows.Controls.TextBlock]) { $visual }
 for($i=0;$i -lt [Windows.Media.VisualTreeHelper]::GetChildrenCount($visual);$i++) {
  VisualTextBlocks ([Windows.Media.VisualTreeHelper]::GetChild($visual,$i))
 }
}
function HelpToolTips($element) {
 if($element -is [Windows.Controls.Button] -and $element.ToolTip -is [Windows.Controls.ToolTip]) { $element.ToolTip }
 if($element -is [Windows.DependencyObject]) {
  foreach($child in [Windows.LogicalTreeHelper]::GetChildren($element)) {
   if($child -is [Windows.DependencyObject]) { HelpToolTips $child }
  }
 }
}
$helpTips=@(HelpToolTips $editor) + @(HelpToolTips $drawingEditor)
Check ($helpTips.Count -eq 11) 'Expected all eleven advanced and drawing tooltips'
$plainHelp=New-Object Windows.Controls.ToolTip
$plainHelp.Content='Texto de ajuda usado nas abas Presets e Configurações. '
$plainHelp.Content=$plainHelp.Content * 6
$helpTips+=,$plainHelp
$tipIndex=0
foreach($tip in $helpTips) {
 $expected=if($tip.Content -is [Windows.Controls.TextBlock]) { $tip.Content.Text } else { [string]$tip.Content }
 Check (![string]::IsNullOrWhiteSpace($expected)) 'Tooltip content is empty'
 $tip.ApplyTemplate() | Out-Null
 $tip.Measure([Windows.Size]::new(340,[double]::PositiveInfinity))
 $tip.Arrange([Windows.Rect]::new([Windows.Point]::new(0,0),$tip.DesiredSize));$tip.UpdateLayout()
 $rendered=@(VisualTextBlocks $tip | Where-Object { $_.Text -eq $expected -and $_.ActualWidth -gt 0 -and $_.ActualHeight -gt 0 })
 Check ($rendered.Count -eq 1) 'Tooltip template does not render its explanation'
 Check ($tip.ActualWidth -le 340 -and $tip.ActualHeight -gt 20) 'Tooltip layout is clipped or empty'
 if($tip -eq $plainHelp) { Check ($rendered[0].ActualHeight -gt 30) 'Plain string tooltip does not wrap' }
 $bitmap=[Windows.Media.Imaging.RenderTargetBitmap]::new([int][Math]::Ceiling($tip.ActualWidth),[int][Math]::Ceiling($tip.ActualHeight),96,96,[Windows.Media.PixelFormats]::Pbgra32)
 $bitmap.Render($tip)
 $encoder=[Windows.Media.Imaging.PngBitmapEncoder]::new();$encoder.Frames.Add([Windows.Media.Imaging.BitmapFrame]::Create($bitmap))
 $file=[IO.File]::Create((Join-Path $root ('build-check\tooltip-'+$tipIndex+'.png')));$encoder.Save($file);$file.Dispose()
 $tipIndex++
}
Check ((Field $editor 'Play').Content -eq 'Iniciar') 'Play contains duplicate glyph'
Check ((Field $editor 'UseTrajectory').Content -eq 'Usar trajetória avançada') 'Advanced toggle is verbose'
Check ((Field $drawingEditor 'Invert').Content -eq 'Inverter movimento') 'Invert toggle is verbose'
$converter=New-Object MacroDoButler.ActionButtonIconConverter
$fallback=$converter.Convert('Unknown',[object],$null,[Globalization.CultureInfo]::InvariantCulture).ToString()
foreach($label in @('Iniciar','Pausar','Reiniciar','Adicionar ponto','Desfazer','Desfazer desenho','Limpar pontos','Girar 180°','Ajustar imagem','Imagem de referência…','Aplicar desenho')) {
 $icon=$converter.Convert($label,[object],$null,[Globalization.CultureInfo]::InvariantCulture)
 Check ($icon.ToString() -ne $fallback) "Action uses confirmation icon: $label"
}
Check ($converter.Convert('Iniciar',[object],$null,[Globalization.CultureInfo]::InvariantCulture).ToString() -ne $converter.Convert('Reiniciar',[object],$null,[Globalization.CultureInfo]::InvariantCulture).ToString()) 'Restart has play icon'
# Resolve the actual logo declarations with MainWindow's compiled XAML base URI.
# Do not construct MainWindow: its constructor loads user data and starts input.
[xml]$mainMarkup=[IO.File]::ReadAllText((Join-Path $root 'src\UI\MainWindow.xaml'))
$logos=@($mainMarkup.SelectNodes('//*[local-name()="Image" and contains(@Source,"Velqora.png")]'))
Check ($logos.Count -eq 2) 'Sidebar and About logo declarations must both be checked'
$logoIndex=0
foreach($logo in $logos) {
 $context=[Windows.Markup.ParserContext]::new()
 $context.BaseUri=[Uri]::new(('pack://application:,,,/'+$assembly.GetName().Name+';component/src/UI/MainWindow.xaml'))
 $markup='<Image xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation" Source="'+$logo.Source+'" Width="'+$logo.Width+'" Height="'+$logo.Height+'" Stretch="Uniform"/>'
 $image=[Windows.Markup.XamlReader]::Parse($markup,$context)
 Check ($image.Source -is [Windows.Media.Imaging.BitmapSource] -and $image.Source.PixelWidth -gt 0) 'Application logo resource failed to decode'
 $header=[Windows.Controls.StackPanel]::new();$header.Orientation='Horizontal';$header.VerticalAlignment='Center';$header.Margin='22,0,12,0'
 $header.Children.Add($image) | Out-Null
 $label=[Windows.Controls.TextBlock]::new();$label.Text='Velqora';$label.FontSize=20;$label.Foreground=[Windows.Media.Brushes]::White;$label.VerticalAlignment='Center';$label.Margin='12,0,0,0'
 $header.Children.Add($label) | Out-Null
 $badge=[Windows.Controls.Border]::new();$badge.Background=[Windows.Media.BrushConverter]::new().ConvertFromString('#101517');$badge.Child=$header
 $badge.Measure([Windows.Size]::new(232,104));$badge.Arrange([Windows.Rect]::new(0,0,232,104));$badge.UpdateLayout()
 Check ($image.ActualWidth -eq [double]$logo.Width -and $image.ActualHeight -eq [double]$logo.Height) 'Application logo layout is empty'
 $bitmap=[Windows.Media.Imaging.RenderTargetBitmap]::new(232,104,96,96,[Windows.Media.PixelFormats]::Pbgra32);$bitmap.Render($badge)
 $pixels=New-Object byte[] (232*104*4);$bitmap.CopyPixels($pixels,232*4,0);$goldPixels=0
 for($i=0;$i -lt $pixels.Length;$i+=4) { if($pixels[$i+2] -gt 140 -and $pixels[$i+1] -gt 80 -and $pixels[$i] -lt 100) { $goldPixels++ } }
 Check ($goldPixels -gt 20) 'Application logo renders blank instead of the gold symbol'
 $encoder=[Windows.Media.Imaging.PngBitmapEncoder]::new();$encoder.Frames.Add([Windows.Media.Imaging.BitmapFrame]::Create($bitmap))
 $file=[IO.File]::Create((Join-Path $root ('build-check\branding-logo-'+$logoIndex+'.png')));$encoder.Save($file);$file.Dispose();$logoIndex++
}
Write-Output "WPF editor: $script:checks checks passed; rendered 1100px and 885px layouts and application logos."
