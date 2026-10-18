using System;
using System.IO;

// Persisted velocities stay in pixels/second. Only complete display units are configurable.
internal static class PresetForceUnits
{
    internal const int Version = 1;
    internal const int VerticalPixelsPerUnit = 100, HorizontalPixelsPerUnit = 10;
    internal static int PixelsPerUnit(bool horizontal) { return horizontal ? HorizontalPixelsPerUnit : VerticalPixelsPerUnit; }
    internal static double Display(double pixels, bool horizontal = false)
    {
        if(double.IsNaN(pixels)||double.IsInfinity(pixels)) throw new InvalidDataException("A força do preset não é um número válido.");
        return Math.Round(pixels/PixelsPerUnit(horizontal),MidpointRounding.AwayFromZero);
    }
    internal static int ToPixels(double force,bool horizontal = false)
    {
        return checked((int)(Math.Round(force,MidpointRounding.AwayFromZero)*PixelsPerUnit(horizontal)));
    }
    internal static void RoundTrajectory(Trajectory trajectory)
    {
        if(trajectory==null)return;
        if(trajectory.Points==null)return;
        foreach(var point in trajectory.Points)
        {
            if(point==null)continue;
            point.Horizontal=ToPixels(Display(point.Horizontal,true),true);
            point.Vertical=ToPixels(Display(point.Vertical));
        }
    }
    internal static void Round(Preset preset)
    {
        if(preset==null)throw new InvalidDataException("O arquivo contém um preset inválido.");
        preset.PrimaryVertical=ToPixels(Display(preset.PrimaryVertical));
        preset.SecondaryVertical=ToPixels(Display(preset.SecondaryVertical));
        preset.PrimaryHorizontal=ToPixels(Display(preset.PrimaryHorizontal,true),true);
        preset.SecondaryHorizontal=ToPixels(Display(preset.SecondaryHorizontal,true),true);
        RoundTrajectory(preset.PrimaryTrajectory); RoundTrajectory(preset.SecondaryTrajectory);
    }
}
