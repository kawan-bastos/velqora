using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Win32;

namespace MacroDoButler
{
    public partial class TrajectoryDrawingEditor : UserControl
    {
        internal Action<Trajectory> Apply;
        internal Func<bool> Smooth,Step;
        internal bool HasChanges { get; private set; }
        private readonly Stack<List<DrawingPoint>> history=new Stack<List<DrawingPoint>>();
        private bool ready, updatingImageControls;
        public TrajectoryDrawingEditor()
        {
            InitializeComponent(); ready=true;
            Surface.ReferenceViewChanged=RefreshImageControls;
            Surface.BeforeEdit=Remember;
            Surface.Changed=delegate { HasChanges=Surface.Points.Count>0; UpdateCount(); };
            UpdateCount();
        }
        private void Remember()
        {
            history.Push(Surface.Points.Select(p=>p.Copy()).ToList());
            if(history.Count>100) { var recent=history.Take(100).Reverse().ToArray(); history.Clear(); foreach(var points in recent)history.Push(points); }
        }
        internal void Reset()
        {
            Surface.Points.Clear(); Surface.ReferenceImage=null; Surface.ResetReferenceView(); Surface.InvalidateVisual();
            history.Clear(); HasChanges=false; UpdateCount();
        }
        private void UpdateCount() { DrawingStatus.Text=Surface.Points.Count+" / 64 pontos"; }
        private void SettingsChanged(object sender,RoutedEventArgs e) { if(ready&&Surface.Points.Count>0)HasChanges=true; }
        private void UndoClick(object sender,RoutedEventArgs e)
        {
            if(history.Count==0)return;
            Surface.Points=history.Pop(); HasChanges=Surface.Points.Count>0; Surface.InvalidateVisual(); UpdateCount();
        }
        private void ClearClick(object sender,RoutedEventArgs e)
        {
            if(Surface.Points.Count==0)return; Remember(); Surface.Points.Clear(); HasChanges=false; Surface.InvalidateVisual(); UpdateCount();
        }
        private void RemoveImageClick(object sender,RoutedEventArgs e) { Surface.ReferenceImage=null; Surface.ResetReferenceView(); Surface.InvalidateVisual(); }
        private void LoadImageClick(object sender,RoutedEventArgs e)
        {
            var picker=new OpenFileDialog { Title="Imagem de referência para desenho",Filter="Imagens|*.png;*.jpg;*.jpeg;*.bmp",CheckFileExists=true };
            if(picker.ShowDialog(Window.GetWindow(this))!=true)return;
            try { LoadReferenceImage(picker.FileName); DrawingStatus.Text="Referência carregada."; }
            catch(Exception ex) { DrawingStatus.Text="Não foi possível carregar a imagem: "+ex.Message; }
        }
        internal void LoadReferenceImage(string path)
        {
            if(new FileInfo(path).Length>20*1024*1024)throw new InvalidDataException("Use uma imagem de até 20 MB.");
            var image=new BitmapImage();
            using(var stream=File.OpenRead(path))
            {
                var decoder=BitmapDecoder.Create(stream,BitmapCreateOptions.DelayCreation,BitmapCacheOption.None);
                var frame=decoder.Frames[0];
                int width=frame.PixelWidth, height=frame.PixelHeight;
                stream.Position=0;
                image.BeginInit(); image.CacheOption=BitmapCacheOption.OnLoad;
                if(width>=height)image.DecodePixelWidth=Math.Min(width,1400); else image.DecodePixelHeight=Math.Min(height,1400);
                image.StreamSource=stream; image.EndInit();
            }
            image.Freeze(); Surface.ReferenceImage=image; Surface.ResetReferenceView(); Surface.InvalidateVisual();
        }
        private void RefreshImageControls()
        {
            if(!ready)return;
            updatingImageControls=true;
            ImageZoom.Value=Surface.ReferenceZoom;
            ImageZoomText.Text=(Surface.ReferenceZoom*100).ToString("0",CultureInfo.CurrentCulture)+"%";
            updatingImageControls=false;
        }
        private void ImageViewChanged(object sender,RoutedEventArgs e)
        {
            if(!ready||updatingImageControls)return;
            Surface.SetReferenceZoom(ImageZoom.Value);
        }
        private void RotateImageClick(object sender,RoutedEventArgs e)
        {
            Surface.ReferenceRotated=!Surface.ReferenceRotated; Surface.InvalidateVisual();
            DrawingStatus.Text=Surface.ReferenceRotated?"Imagem girada 180°.":"Orientação original.";
        }
        private void ResetImageViewClick(object sender,RoutedEventArgs e) { Surface.ResetReferenceView(); DrawingStatus.Text="Imagem ajustada."; }
        internal void UpdateTimingHint(bool step)
        {
            DrawingTimingHelp.Text="Tempo total de 100 a 60000 ms, com no mínimo 2 ms por trecho. O tempo é distribuído pelo comprimento dos trechos. "+
                (step?"Em degraus: velocidade constante em cada trecho, troca direta no próximo ponto, sem parar nas junções.":"Gradual: mantém velocidade entre os pontos, sem zerar em cada junção. Suave também faz a aceleração chegar continuamente às junções; a parada fica no final.");
        }
        private void ApplyClick(object sender,RoutedEventArgs e)
        {
            int duration; double scale;
            if(!int.TryParse(DurationInput.Text,out duration)||!ForceUnits.TryParse(ScaleInput.Text,out scale)||scale<1||scale>10)
            { DrawingStatus.Text="Use duração inteira e escala inteira entre 1 e 10."; return; }
            try
            {
                var converted=DrawingTrajectoryConverter.Convert(Surface.Points,duration,scale,Invert.IsChecked==true,Smooth!=null&&Smooth(),Step!=null&&Step());
                Apply?.Invoke(converted); HasChanges=false; DrawingStatus.Text="Desenho aplicado.";
            }
            catch(ArgumentException ex) { DrawingStatus.Text=ex.Message; }
        }
    }

    public sealed class TrajectoryDrawingCanvas : FrameworkElement
    {
        internal List<DrawingPoint> Points=new List<DrawingPoint>();
        internal ImageSource ReferenceImage;
        internal Action BeforeEdit,Changed,ReferenceViewChanged;
        internal double ReferenceZoom=1, ReferenceOffsetX, ReferenceOffsetY;
        internal bool ReferenceRotated;
        private bool panning;
        private Point lastPan;
        internal void SetReferenceZoom(double zoom)
        {
            ReferenceZoom=Math.Max(1,Math.Min(6,zoom)); InvalidateVisual(); ReferenceViewChanged?.Invoke();
        }
        internal void PanReference(double dx,double dy)
        {
            ReferenceOffsetX=Math.Max(-3000,Math.Min(3000,ReferenceOffsetX+dx));
            ReferenceOffsetY=Math.Max(-3000,Math.Min(3000,ReferenceOffsetY+dy)); InvalidateVisual();
        }
        internal void ResetReferenceView()
        {
            ReferenceZoom=1; ReferenceOffsetX=ReferenceOffsetY=0;
            ReferenceRotated=false;
            InvalidateVisual(); ReferenceViewChanged?.Invoke();
        }
        internal Matrix ReferenceTransform
        {
            get
            {
                var matrix=Matrix.Identity;
                double sx=ReferenceZoom*(ReferenceRotated?-1:1);
                double sy=ReferenceZoom*(ReferenceRotated?-1:1);
                matrix.ScaleAt(sx,sy,ActualWidth/2,ActualHeight/2);
                matrix.Translate(ReferenceOffsetX*Scale,ReferenceOffsetY*Scale);
                return matrix;
            }
        }

        private int dragging=-1;
        private static readonly Brush Gold=new SolidColorBrush(Color.FromRgb(232,185,79));
        private double Scale { get { return Math.Max(0.01,Math.Min(ActualWidth/600,ActualHeight/360)); } }
        private double Left { get { return (ActualWidth-600*Scale)/2; } }
        private double Top { get { return (ActualHeight-360*Scale)/2; } }
        private Point Map(DrawingPoint p) { return new Point(Left+p.X*Scale,Top+p.Y*Scale); }
        private DrawingPoint Unmap(Point p) { return new DrawingPoint { X=Math.Max(0,Math.Min(600,(p.X-Left)/Scale)),Y=Math.Max(0,Math.Min(360,(p.Y-Top)/Scale)) }; }
        private int Hit(Point position) { for(int i=Points.Count-1;i>=0;i--)if((Map(Points[i])-position).Length<=10)return i; return -1; }
        internal void AddAt(double x,double y)
        {
            if(Points.Count>=DrawingTrajectoryConverter.MaxDrawingPoints)return;
            BeforeEdit?.Invoke(); Points.Add(new DrawingPoint { X=Math.Max(0,Math.Min(600,x)),Y=Math.Max(0,Math.Min(360,y)) }); Changed?.Invoke(); InvalidateVisual();
        }
        internal void MoveTo(int index,double x,double y)
        {
            if(index<0||index>=Points.Count)return;
            Points[index]=new DrawingPoint { X=Math.Max(0,Math.Min(600,x)),Y=Math.Max(0,Math.Min(360,y)) }; Changed?.Invoke(); InvalidateVisual();
        }
        internal void RemoveAt(int index)
        {
            if(index<0||index>=Points.Count)return;
            BeforeEdit?.Invoke(); Points.RemoveAt(index); Changed?.Invoke(); InvalidateVisual();
        }
        protected override void OnRender(DrawingContext dc)
        {
            dc.DrawRoundedRectangle(new SolidColorBrush(Color.FromRgb(12,16,18)),new Pen(Brushes.DimGray,1),new Rect(RenderSize),8,8);
            var bounds=new Rect(Left,Top,600*Scale,360*Scale);
            dc.PushClip(new RectangleGeometry(bounds));
            if(ReferenceImage!=null)
            {
                double imageScale=Math.Min(bounds.Width/ReferenceImage.Width,bounds.Height/ReferenceImage.Height);
                var rectangle=new Rect(bounds.Left+(bounds.Width-ReferenceImage.Width*imageScale)/2,bounds.Top+(bounds.Height-ReferenceImage.Height*imageScale)/2,ReferenceImage.Width*imageScale,ReferenceImage.Height*imageScale);
                dc.PushOpacity(0.45); dc.PushTransform(new MatrixTransform(ReferenceTransform)); dc.DrawImage(ReferenceImage,rectangle); dc.Pop(); dc.Pop();
            }
            var grid=new Pen(new SolidColorBrush(Color.FromArgb(90,65,78,83)),1);
            for(int x=0;x<=600;x+=30)dc.DrawLine(grid,Map(new DrawingPoint { X=x }),Map(new DrawingPoint { X=x,Y=360 }));
            for(int y=0;y<=360;y+=30)dc.DrawLine(grid,Map(new DrawingPoint { Y=y }),Map(new DrawingPoint { X=600,Y=y }));
            for(int i=0;i<Points.Count;i++)
            {
                Point point=Map(Points[i]);
                if(i>0)
                {
                    Point previous=Map(Points[i-1]); var pen=new Pen(Gold,2); dc.DrawLine(pen,previous,point);
                    Vector direction=point-previous;
                    if(direction.Length>20)
                    {
                        Point middle=previous+direction*0.5; direction.Normalize(); Vector side=new Vector(-direction.Y,direction.X);
                        dc.DrawLine(pen,middle-direction*7+side*4,middle); dc.DrawLine(pen,middle-direction*7-side*4,middle);
                    }
                }
                dc.DrawEllipse(i==0?Brushes.White:Gold,new Pen(Brushes.Black,1),point,5,5);
                var label=new FormattedText((i+1).ToString()+(i==0?" INÍCIO":i==Points.Count-1?" FIM":""),CultureInfo.CurrentCulture,FlowDirection.LeftToRight,new Typeface("Segoe UI"),11,Brushes.White,VisualTreeHelper.GetDpi(this).PixelsPerDip);
                dc.DrawText(label,new Point(Math.Min(bounds.Right-label.Width,point.X+8),Math.Max(bounds.Top,point.Y-18)));
            }
            dc.Pop();
        }
        protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
        {
            base.OnMouseLeftButtonDown(e); Focus(); var position=e.GetPosition(this);
            if((Keyboard.Modifiers&ModifierKeys.Shift)!=0)
            {
                if(ReferenceImage!=null)BeginPan(position); e.Handled=true; return;
            }
            int hit=Hit(position);
            if(hit>=0) { BeforeEdit?.Invoke(); dragging=hit; CaptureMouse(); }
            else if(new Rect(Left,Top,600*Scale,360*Scale).Contains(position)) { var p=Unmap(position); AddAt(p.X,p.Y); }
            e.Handled=true;
        }
        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if(panning)
            {
                var position=e.GetPosition(this); var delta=position-lastPan; lastPan=position;
                PanReference(delta.X/Scale,delta.Y/Scale); e.Handled=true; return;
            }
            if(dragging<0||e.LeftButton!=MouseButtonState.Pressed)return;
            var p=Unmap(e.GetPosition(this)); MoveTo(dragging,p.X,p.Y); e.Handled=true;
        }
        protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e) { base.OnMouseLeftButtonUp(e); dragging=-1; panning=false; ReleaseMouseCapture(); }
        protected override void OnLostMouseCapture(MouseEventArgs e) { dragging=-1; panning=false; base.OnLostMouseCapture(e); }
        private void BeginPan(Point position) { dragging=-1; panning=true; lastPan=position; CaptureMouse(); }
        protected override void OnMouseDown(MouseButtonEventArgs e)
        {
            base.OnMouseDown(e);
            if(e.ChangedButton==MouseButton.Middle&&ReferenceImage!=null) { BeginPan(e.GetPosition(this)); e.Handled=true; }
        }
        protected override void OnMouseUp(MouseButtonEventArgs e)
        {
            base.OnMouseUp(e);
            if(e.ChangedButton==MouseButton.Middle&&panning) { panning=false; ReleaseMouseCapture(); e.Handled=true; }
        }
        protected override void OnMouseWheel(MouseWheelEventArgs e)
        {
            if(ReferenceImage==null) { base.OnMouseWheel(e); return; }
            SetReferenceZoom(ReferenceZoom*(e.Delta>0?1.15:1/1.15)); e.Handled=true;
        }
        protected override void OnMouseRightButtonDown(MouseButtonEventArgs e) { base.OnMouseRightButtonDown(e); RemoveAt(Hit(e.GetPosition(this))); e.Handled=true; }
    }
}
