using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;

namespace JacquardStudio;

/// <summary>Windows standard color picker. Returns null when cancelled.</summary>
public static class ColorChooser
{
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    private struct ChooseColorData
    {
        public int Size; public IntPtr Owner, Instance; public int Color; public IntPtr CustomColors; public int Flags; public IntPtr CustomData, Hook, Template;
    }
    [DllImport("comdlg32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ChooseColor(ref ChooseColorData data);
    public static string? Pick(Window owner, string current)
    {
        Color c; try { c = FabricSimulation.ParseColor(current); } catch (ArgumentException) { c = Colors.White; }
        IntPtr palette = Marshal.AllocHGlobal(16 * sizeof(int));
        try
        {
            for (int i = 0; i < 16; i++) Marshal.WriteInt32(palette, i * sizeof(int), 0x00FFFFFF);
            var dialog = new ChooseColorData { Size = Marshal.SizeOf<ChooseColorData>(), Owner = new WindowInteropHelper(owner).Handle, Color = c.R | (c.G << 8) | (c.B << 16), CustomColors = palette, Flags = 0x00000001 | 0x00000002 | 0x00000100 };
            if (!ChooseColor(ref dialog)) return null;
            return $"#{dialog.Color & 255:X2}{(dialog.Color >> 8) & 255:X2}{(dialog.Color >> 16) & 255:X2}";
        }
        finally { Marshal.FreeHGlobal(palette); }
    }
}
