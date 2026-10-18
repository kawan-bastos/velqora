using System;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;

namespace MacroDoButler
{
    public partial class PresetMovementSummary : UserControl
    {
        public PresetMovementSummary()
        {
            InitializeComponent();
            MiniPlot.StaticPreview=true;
            MiniPlot.Selected=-1;
        }
        internal void ShowSettings(Preset preset, bool secondary, bool macroEnabled, int horizontal, int vertical)
        {
            Visibility=preset==null?Visibility.Collapsed:Visibility.Visible;
            if(preset==null) { MiniPlot.Value=null; return; }
            var trajectory=secondary?preset.SecondaryTrajectory:preset.PrimaryTrajectory;
            bool advanced=trajectory!=null&&trajectory.Enabled;
            string weapon=secondary?"SECUNDÁRIA":"PRIMÁRIA";
            ModeText.Text=(advanced?"AVANÇADA":"BÁSICA")+" • "+weapon;
            ModeBadge.BorderBrush=(System.Windows.Media.Brush)FindResource(advanced?"AccentBrush":"BorderBrush");
            ModeText.Foreground=(System.Windows.Media.Brush)FindResource(advanced?"AccentBrightBrush":"MutedBrush");
            MacroState.Text=macroEnabled?"Macro ligado • esta é a configuração em uso":"Macro desligado • configuração selecionada";
            AdvancedDetails.Visibility=advanced?Visibility.Visible:Visibility.Collapsed;
            VerticalLabel.Text=advanced?"Vertical • faixa da trajetória":"Força vertical";
            HorizontalLabel.Text=advanced?"Horizontal • faixa da trajetória":"Correção lateral";
            VerticalValue.Text=advanced?Range(trajectory,false):Format(vertical)+" de força";
            HorizontalValue.Text=advanced?Range(trajectory,true):Format(horizontal,true)+" de força";
            if(advanced)
            {
                TrajectoryInfo.Text=trajectory.Points.Count+" pontos • "+trajectory.Duration+" ms • interpolação "+trajectory.InterpolationName;
                var last=trajectory.Points[trajectory.Points.Count-1];
                TrajectoryEnding.Text=trajectory.HoldLast?"Ao terminar: mantém H "+Format(last.Horizontal,true)+" / V "+Format(last.Vertical)+" de força":"Ao terminar: zero movimento";
            }
            var displayed=advanced?trajectory:null;
            if(!ReferenceEquals(MiniPlot.Value,displayed)) { MiniPlot.Value=displayed; MiniPlot.Rebuild(); }
        }
        private static string Range(Trajectory trajectory,bool horizontal)
        {
            var values=trajectory.Points.Select(p=>horizontal?p.Horizontal:p.Vertical);
            double min=values.Min(), max=values.Max();
            return (min==max?Format(min,horizontal):Format(min,horizontal)+" a "+Format(max,horizontal))+" de força";
        }
        private static string Format(double value,bool horizontal=false) { return ForceUnits.Format(value,horizontal); }
    }
}
