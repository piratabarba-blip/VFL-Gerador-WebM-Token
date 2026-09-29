using System.Runtime.InteropServices;

namespace VFL.GeradorWebMToken;

internal static class Program
{
    private const string AppUserModelId = "VFL.GeradorWebMToken";

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SetCurrentProcessExplicitAppUserModelID(string appId);

    [STAThread]
    private static void Main()
    {
        SetCurrentProcessExplicitAppUserModelID(AppUserModelId);
        ApplicationConfiguration.Initialize();
        Application.Run(new MainForm());
    }
}
