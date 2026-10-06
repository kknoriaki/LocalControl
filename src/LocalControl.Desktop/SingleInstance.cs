using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;

namespace LocalControl.Desktop;

internal static class SingleInstance
{
    internal static readonly string Identity = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(Environment.UserDomainName + "\\" + Environment.UserName)))[..16];
    internal static readonly uint ShowMessage = RegisterWindowMessage("LocalControl.Show." + Identity);
    internal static readonly uint ExitMessage = RegisterWindowMessage("LocalControl.Exit." + Identity);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern uint RegisterWindowMessage(string name);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr FindWindow(string? className, string windowName);
    [DllImport("user32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool PostMessage(IntPtr window, uint message, IntPtr wParam, IntPtr lParam);

    internal static bool Request(bool exit)
    {
        // Registered messages only open or close our window; they carry no
        // path, command, credentials, or arbitrary remote action.
        for (var i = 0; i < 20; i++)
        {
            var window = FindWindow(null, "LocalControl");
            if (window != IntPtr.Zero) return PostMessage(window, exit ? ExitMessage : ShowMessage, IntPtr.Zero, IntPtr.Zero);
            Thread.Sleep(100);
        }
        return false;
    }
}
