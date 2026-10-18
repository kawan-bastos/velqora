using System;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Shapes;

namespace MacroDoButler
{
    // String labels retain their original content and automation names; rich content keeps its own layout.
    public sealed class ActionButtonTemplateSelector : DataTemplateSelector
    {
        private readonly DataTemplate template;
        public ActionButtonTemplateSelector()
        {
            var row = new FrameworkElementFactory(typeof(StackPanel));
            row.SetValue(StackPanel.OrientationProperty, Orientation.Horizontal);
            row.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
            var icon = new FrameworkElementFactory(typeof(Path));
            icon.SetValue(FrameworkElement.WidthProperty, 18.0);
            icon.SetValue(FrameworkElement.HeightProperty, 18.0);
            icon.SetValue(FrameworkElement.MarginProperty, new Thickness(0,0,10,0));
            icon.SetValue(Shape.StretchProperty, Stretch.Uniform);
            icon.SetValue(Shape.StrokeThicknessProperty, 1.6);
            icon.SetValue(Shape.StrokeStartLineCapProperty, PenLineCap.Round);
            icon.SetValue(Shape.StrokeEndLineCapProperty, PenLineCap.Round);
            icon.SetValue(Shape.StrokeLineJoinProperty, PenLineJoin.Round);
            icon.SetBinding(Shape.StrokeProperty, ButtonBinding("Foreground"));
            icon.SetBinding(Path.DataProperty, new Binding { Converter = new ActionButtonIconConverter() });
            row.AppendChild(icon);
            var label = new FrameworkElementFactory(typeof(TextBlock));
            label.SetBinding(TextBlock.TextProperty, new Binding());
            label.SetBinding(TextBlock.ForegroundProperty, ButtonBinding("Foreground"));
            label.SetBinding(TextBlock.FontFamilyProperty, ButtonBinding("FontFamily"));
            label.SetBinding(TextBlock.FontSizeProperty, ButtonBinding("FontSize"));
            label.SetBinding(TextBlock.FontWeightProperty, ButtonBinding("FontWeight"));
            label.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
            row.AppendChild(label);
            template = new DataTemplate { VisualTree = row };
        }
        private static Binding ButtonBinding(string property)
        {
            return new Binding(property) { RelativeSource = new RelativeSource(RelativeSourceMode.FindAncestor, typeof(Button), 1) };
        }
        public override DataTemplate SelectTemplate(object item, DependencyObject container) { return item is string ? template : null; }
    }
    public sealed class ActionButtonIconConverter : IValueConverter
    {
        public object Convert(object value, Type type, object parameter, CultureInfo culture)
        {
            string label = (value as string ?? string.Empty).ToUpperInvariant();
            string path;
            if (label.Contains("VOLTAR")) path="M20,12 L4,12 M11,5 L4,12 L11,19";
            else if (label.Contains("CANCELAR")) path="M5,5 L19,19 M19,5 L5,19";
            else if (label.Contains("EXCLUIR") || label.Contains("REMOVER")) path="M4,6 L20,6 M9,6 L9,3 L15,3 L15,6 M6,6 L7,21 L17,21 L18,6 M10,10 L10,17 M14,10 L14,17";
            else if (label.Contains("SALVAR")) path="M3,3 L18,3 L21,6 L21,21 L3,21 Z M7,3 L7,10 L17,10 L17,3 M7,21 L7,14 L17,14 L17,21";
            else if (label.Contains("GERENCIAR")) path="M2,8 L6,4 L18,4 L22,8 L23,18 L19,20 L15,15 L9,15 L5,20 L1,18 Z M6,8 L6,14 M3,11 L9,11 M16,9 L16,10 M20,12 L20,13";
            else if (label.Contains("ALTERAR") || label.Contains("EDITAR") || label.Contains("RENOMEAR")) path="M4,16 L15,5 L19,9 L8,20 L3,21 Z M14,6 L18,10 M16,4 L18,2 L22,6 L20,8";
            else if (label.Contains("NOVO") || label.Contains("CRIAR JOGO")) path="M12,3 L12,21 M3,12 L21,12";
            else if (label.Contains("BAIXAR") || label.Contains("IMPORTAR")) path="M12,2 L12,16 M6,10 L12,16 L18,10 M3,16 L3,21 L21,21 L21,16";
            else if (label.Contains("EXPORTAR")) path="M12,16 L12,2 M6,8 L12,2 L18,8 M3,16 L3,21 L21,21 L21,16";
            else if (label.Contains("ATUALIZA") || label.Contains("RESTAURAR") || label.Contains("SUBSTITUIR")) path="M20,8 A9,9 0 1 0 21,15 M20,2 L20,8 L14,8";
            else if (label.Contains("TRANSFER")) path="M3,7 L20,7 M15,2 L20,7 L15,12 M21,17 L4,17 M9,12 L4,17 L9,22";
            else if (label.Contains("BACKUP")) path="M4,6 L12,2 L20,6 L20,18 L12,22 L4,18 Z M4,6 L12,10 L20,6 M12,10 L12,22";
            else if (label.Contains("INSTALAR")) path="M4,3 L20,3 L20,17 L4,17 Z M8,21 L16,21 M12,17 L12,21 M8,10 L11,13 L17,7";
            else if (label == "INICIAR") path="M6,3 L21,12 L6,21 Z";
            else if (label.Contains("PAUSAR")) path="M7,3 L7,21 M17,3 L17,21";
            else if (label.Contains("REINICIAR") || label.Contains("GIRAR")) path="M20,8 A9,9 0 1 0 21,15 M20,2 L20,8 L14,8";
            else if (label.Contains("DESFAZER")) path="M10,4 L3,11 L10,18 M3,11 L15,11 Q22,11 20,20";
            else if (label.Contains("ADICIONAR")) path="M12,3 L12,21 M3,12 L21,12";
            else if (label.Contains("LIMPAR")) path="M3,15 L14,3 L22,11 L12,21 L9,21 Z M8,10 L17,18 M9,21 L22,21";
            else if (label.Contains("AJUSTAR IMAGEM")) path="M9,3 L3,3 L3,9 M15,3 L21,3 L21,9 M3,15 L3,21 L9,21 M21,15 L21,21 L15,21 M7,7 L17,7 L17,17 L7,17 Z";
            else if (label.Contains("IMAGEM")) path="M3,3 L21,3 L21,21 L3,21 Z M3,17 L9,11 L14,16 L17,13 L21,17 M15,7 A2,2 0 1 0 19,7 A2,2 0 1 0 15,7";
            else if (label.Contains("APLICAR DESENHO")) path="M3,17 L14,6 L18,10 L7,21 L3,21 Z M13,7 L17,11 M14,3 L21,3 L21,10";
            else path="M4,12 L10,18 L21,5";
            var geometry = Geometry.Parse(path); geometry.Freeze(); return geometry;
        }
        public object ConvertBack(object value, Type type, object parameter, CultureInfo culture) { throw new NotSupportedException(); }
    }
}
