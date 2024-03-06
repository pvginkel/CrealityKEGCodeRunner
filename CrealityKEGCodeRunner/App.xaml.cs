using Microsoft.Win32;
using System.Net.Http;
using System.Windows;

namespace CrealityKEGCodeRunner;

public partial class App : Application
{
    public static readonly HttpClient HttpClient = new();

    public static RegistryKey CreateBaseKey() => Registry.CurrentUser.CreateSubKey("Creality KE GCode Runner");
}

