using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;
using Newtonsoft.Json.Linq;

namespace AutoScanMAXCLOUD;

public class ADB
{
    public string DeviceID { get; set; }

    public ADB(string deviceID)
    {
        DeviceID = deviceID;
    }

    public string InstallApp(string apkPath)
    {
        return RunAdb($"adb -s {DeviceID} install -r {apkPath}", 60);
    }

    public void PushFile(string source, string destination)
    {
        string result = RunAdb($"adb -s {DeviceID} push {source} {destination}");
        Console.WriteLine(result);
    }

    public void SetupMaxCloud()
    {
        var permissions = new List<string>
        {
            "android.permission.NOTIFICATION_SERVICE",
            "android.permission.READ_EXTERNAL_STORAGE",
            "android.permission.WRITE_EXTERNAL_STORAGE",
            "android.permission.CHANGE_CONFIGURATION",
        };

        foreach (var permission in permissions)
        {
            RunShell($"pm grant com.maxcloud.app {permission}");
        }
        
        // RunShell("monkey -p com.maxcloud.app -c android.intent.category.LAUNCHER 1");
    }

    public static string TOKEN;
    
    public enum AdbCaller
    {
        PING_PING,
        LOGIN_DEVICE
    }
    
    public string sendBroadcastMaxCloud(AdbCaller action = AdbCaller.PING_PING)
    {
        string shellCommand = $"am broadcast -a com.maxcloud.app.{action.ToString()} -n com.maxcloud.app/.AdbCaller";
     
        if (action == AdbCaller.LOGIN_DEVICE)
            shellCommand += $" -e token {TOKEN}";

        string adbOutput = RunShell(shellCommand);
        
        Match match = Regex.Match(adbOutput, @"data=""({.*?})""");

        if (!match.Success)
            return string.Empty;

        return match.Groups[1].Value ?? string.Empty;
    }
   
    public string RunShell(string command)
    {
        return RunAdb($"adb -s {DeviceID} shell {command}");
    }

    public void Reboot()
    {
        RunAdb($"adb -s {DeviceID} reboot");
    }

    public static string RunAdb(string cmd, int timeout = 10)
    {
        string text = "";
        try
        {
            Again:
            Process process = new Process();

            process.StartInfo.FileName = "cmd.exe";
            process.StartInfo.Arguments = $"/c {cmd}";
            process.StartInfo.Verb = "runas";
            process.StartInfo.CreateNoWindow = true;
            process.StartInfo.UseShellExecute = false;
            process.StartInfo.RedirectStandardError = true;
            process.StartInfo.RedirectStandardOutput = true;
            process.StartInfo.StandardOutputEncoding = Encoding.UTF8;
            process.StartInfo.StandardErrorEncoding = Encoding.UTF8;

            string output = "";
            process.OutputDataReceived += (s, e) =>
            {
                if (!string.IsNullOrEmpty(e.Data))
                    output += (e.Data + "\n");
            };
            string error = "";
            process.ErrorDataReceived += (s, e) =>
            {
                if (!string.IsNullOrEmpty(e.Data))
                    error += (e.Data + "\n");
            };

            process.Start();

            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            bool isWaitFail = !process.WaitForExit(timeout < 0 ? -1 : timeout * 1000);
            process.Close();

            if (isWaitFail && !cmd.StartsWith("scrcpy"))
                goto Again;

            if (error != "")
            {
                if (error.Contains("daemon not running") && !error.Contains("daemon started successfully"))
                    goto Again;
            }

            text = output.Trim();
        }
        catch
        {
        }

        return text;
    }
}