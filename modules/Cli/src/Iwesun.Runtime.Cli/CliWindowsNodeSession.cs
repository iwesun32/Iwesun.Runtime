using System.ComponentModel;
using System.Runtime.InteropServices;

namespace Iwesun.Runtime.Cli;

internal static partial class CliWindowsNodeSession
{
    private const int ResourceTypeDisk = 1;

    public static void Authenticate(string serverName, string userName)
    {
        if (!OperatingSystem.IsWindows())
            throw new CliException("CLI_REMOTE_PLATFORM_UNSUPPORTED", "Windows IPC authentication is available only on Windows.", 4);
        if (Console.IsInputRedirected)
            throw new CliException("CLI_REMOTE_AUTH_INTERACTIVE_REQUIRED", "Secure password input requires an interactive terminal. Preload Windows credentials for automation.", 4);

        Console.Error.Write($"Password for {userName} on {serverName}: ");
        var password = ReadSecret();
        Console.Error.WriteLine();
        try
        {
            var resource = new NetResource { Type = ResourceTypeDisk, RemoteName = $@"\\{serverName}\IPC$" };
            var result = WNetAddConnection2(ref resource, password, userName, 0);
            if (result != 0)
                throw MapWindowsError(result, serverName, "auth");
        }
        finally
        {
            password = string.Empty;
        }
    }

    public static void Logout(string serverName)
    {
        if (!OperatingSystem.IsWindows())
            throw new CliException("CLI_REMOTE_PLATFORM_UNSUPPORTED", "Windows IPC logout is available only on Windows.", 4);
        var result = WNetCancelConnection2($@"\\{serverName}\IPC$", 0, true);
        if (result != 0 && result != 2250)
            throw MapWindowsError(result, serverName, "logout");
    }

    private static string ReadSecret()
    {
        var value = new List<char>();
        while (true)
        {
            var key = Console.ReadKey(intercept: true);
            if (key.Key == ConsoleKey.Enter) return new string(value.ToArray());
            if (key.Key == ConsoleKey.Backspace)
            {
                if (value.Count > 0) value.RemoveAt(value.Count - 1);
                continue;
            }
            if (!char.IsControl(key.KeyChar)) value.Add(key.KeyChar);
        }
    }

    private static CliException MapWindowsError(int error, string serverName, string phase)
    {
        var code = error switch
        {
            5 => "CLI_REMOTE_ACCESS_DENIED",
            53 or 67 or 1231 => "CLI_REMOTE_NODE_UNREACHABLE",
            1219 => "CLI_REMOTE_CREDENTIAL_CONFLICT",
            _ => "CLI_REMOTE_AUTH_REQUIRED"
        };
        return new CliException(code, new Win32Exception(error).Message, 4, data: new { serverName, phase, windowsError = error, retryable = error is 53 or 67 or 1231 });
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct NetResource
    {
        public int Scope;
        public int Type;
        public int DisplayType;
        public int Usage;
        public string? LocalName;
        public string? RemoteName;
        public string? Comment;
        public string? Provider;
    }

    [DllImport("mpr.dll", CharSet = CharSet.Unicode)]
    private static extern int WNetAddConnection2(ref NetResource netResource, string? password, string? userName, int flags);

    [DllImport("mpr.dll", CharSet = CharSet.Unicode)]
    private static extern int WNetCancelConnection2(string name, int flags, bool force);
}
