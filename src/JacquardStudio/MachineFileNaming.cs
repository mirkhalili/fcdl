using System.IO;
namespace JacquardStudio;
public static class MachineFileNaming
{
    public static string SuggestedName(int format) => format == 4 ? "design6.jc5" : "design.jc5";
    public static bool ValidDestination(string path, int format)
    {
        if (format is not (1 or 4) || !Path.GetExtension(path).Equals(".jc5", StringComparison.OrdinalIgnoreCase)) return false;
        string name = Path.GetFileNameWithoutExtension(path);
        return format != 4 || NumberInput.Normalize(name).Contains('6');
    }
}
