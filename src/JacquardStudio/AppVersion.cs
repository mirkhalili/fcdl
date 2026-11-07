using System.Reflection;
namespace JacquardStudio;
public static class AppVersion
{
    public static string Display => typeof(AppVersion).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "unknown";
}
