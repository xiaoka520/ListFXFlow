using System.Reflection;
namespace ListFXFlow;
public static class BuildInfo
{
    public static string Label {
        get {
            string version=Assembly.GetExecutingAssembly().GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion??"dev";
            return "MAC + WINDOWS\nVERSION "+version.Replace("+build."," · BUILD ");
        }
    }
}
