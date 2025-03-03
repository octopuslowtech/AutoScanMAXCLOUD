using System.Diagnostics;
using System.Text;

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
        return RunAdb($"adb -s {DeviceID} install -r {apkPath}", 100);
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
        };

        foreach (var permission in permissions)
        {
            RunShell($"pm grant com.maxcloud.app {permission}");
        }

        PushFile(CALLER_NAME, $"/sdcard/{CALLER_NAME}");
        
        RunShell("monkey -p com.maxcloud.app -c android.intent.category.LAUNCHER 1");

        RunShell("ime enable com.maxcloud.app/com.maxcloud.keyboard.latin.LatinIME");
        RunShell("ime set com.maxcloud.app/com.maxcloud.keyboard.latin.LatinIME");
    }

    public static string TOKEN;
    private static string CALLER_NAME = "goodmorning.txt";

    public static void InitLoginCaller()
    {
        File.Delete(CALLER_NAME);

        File.WriteAllText(CALLER_NAME, TOKEN);

        if (!File.Exists("maxcloud.apk"))
            throw new Exception("maxcloud.apk not found");

        if (!File.Exists("helper.apk"))
            throw new Exception("maxcloud.apk not found");
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