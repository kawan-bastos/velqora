using System;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;

namespace MacroDoButler
{
    // Display units only. Storage and movement continue to use pixels per second.
    internal static class ForceUnits
    {
        internal const double PixelsPerUnit = PresetForceUnits.VerticalPixelsPerUnit;
        internal static double PixelsPerUnitFor(bool horizontal) { return PresetForceUnits.PixelsPerUnit(horizontal); }
        internal static double Display(double pixels, bool horizontal = false) { return PresetForceUnits.Display(pixels,horizontal); }
        internal static int ToPixelsInt(double force, bool horizontal = false) { return PresetForceUnits.ToPixels(force,horizontal); }
        internal static string Format(double pixels, bool horizontal = false) { return Display(pixels, horizontal).ToString("0", CultureInfo.CurrentCulture); }
        internal static bool TryParse(string text, out double displayed)
        {
            int integer;
            bool valid=int.TryParse(text,NumberStyles.Integer,CultureInfo.InvariantCulture,out integer);
            displayed=integer; return valid;
        }
        internal static void RoundTrajectory(Trajectory trajectory) { PresetForceUnits.RoundTrajectory(trajectory); }
        internal static Preset RoundedCopy(Preset preset)
        {
            var result=PresetEditingService.Copy(preset);
            PresetForceUnits.Round(result); return result;
        }
        internal static string RangeError(string field, double min, double max)
        {
            return "Valor " + field + " excedido. Escolha um valor entre " + min.ToString("0.#####", CultureInfo.CurrentCulture)
                + " e " + max.ToString("0.#####", CultureInfo.CurrentCulture) + ".";
        }
    }
    public sealed class ForceValueConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            double pixels = System.Convert.ToDouble(value, CultureInfo.InvariantCulture);
            bool horizontal=string.Equals(parameter as string,"Horizontal",StringComparison.Ordinal);
            return targetType == typeof(string) ? (object)ForceUnits.Format(pixels,horizontal) : ForceUnits.Display(pixels,horizontal);
        }
        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            string text=System.Convert.ToString(value,culture);
            double displayed;
            if (!ForceUnits.TryParse(text, out displayed)) return DependencyProperty.UnsetValue;
            bool horizontal=string.Equals(parameter as string,"Horizontal",StringComparison.Ordinal);
            double pixels = displayed * ForceUnits.PixelsPerUnitFor(horizontal);
            if (targetType != typeof(int)) return pixels;
            if (pixels < int.MinValue || pixels > int.MaxValue) return DependencyProperty.UnsetValue;
            return (int)Math.Round(pixels, MidpointRounding.AwayFromZero);
        }
    }
    public sealed class ForceRangeRule : ValidationRule
    {
        public string Field { get; set; }
        public double Minimum { get; set; }
        public double Maximum { get; set; }
        public override ValidationResult Validate(object value, CultureInfo cultureInfo)
        {
            double number;
            if (!ForceUnits.TryParse(value as string, out number))
                return new ValidationResult(false, "Valor " + Field + " inválido. Digite um número inteiro.");
            return number < Minimum || number > Maximum
                ? new ValidationResult(false, ForceUnits.RangeError(Field, Minimum, Maximum)) : ValidationResult.ValidResult;
        }
    }
}
