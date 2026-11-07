namespace JacquardStudio;
public static class ProjectSavePolicy
{
    public static bool RequiresDialog(string? existingPath, bool saveAs) => saveAs || string.IsNullOrEmpty(existingPath);
}
