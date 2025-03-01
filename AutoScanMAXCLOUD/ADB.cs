using System.Diagnostics;
using System.Text;

namespace AutoScanMAXCLOUD;

public class ADB
{
    public string DeviceID { get; set; }
    private static string TOKEN = "min_8691cfdf73a042078ec5e4a1717cf4f1";
    private static string CALLER_NAME = "goodmorning.txt";

    public ADB(string deviceID)
    {
        DeviceID = deviceID;
    }
    
 
    
    public string InstallApp(string apkPath)
    {
        return RunAdb($"adb -s {DeviceID} install -r {apkPath}");
    }
    
    public void PushFile(string source, string destination)
    {
        string result = RunAdb($"adb -s {DeviceID} push {source} {destination}");
        Console.WriteLine(result);
    }


    public void SetupMaxCloud()
    {
        GrantPermissionMaxCloud();

        PushFile(CALLER_NAME, $"/sdcard/{CALLER_NAME}");
        
        runShell("monkey -p com.maxcloud.app -c android.intent.category.LAUNCHER 1");
    }
    
    public void GrantPermissionMaxCloud()
    {
        var permissions = new List<string>
        {
            "android.permission.NOTIFICATION_SERVICE",
            "android.permission.READ_EXTERNAL_STORAGE",
            "android.permission.WRITE_EXTERNAL_STORAGE",
        };

        foreach (var permission in permissions)
        {
            runShell($"pm grant com.maxcloud.app {permission}");
        }

        runShell("ime enable com.maxcloud.app/com.maxcloud.keyboard.latin.LatinIME");
        runShell("ime set com.maxcloud.app/com.maxcloud.keyboard.latin.LatinIME");
    }
    
    public static List<string> GetDevices()
    {
        var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = "adb",
                Arguments = "devices",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                CreateNoWindow = true
            }
        };
        process.Start();
        process.WaitForExit();
        var output = process.StandardOutput.ReadToEnd();
        var devices = new List<string>();
        foreach (var line in output.Split('\n'))
        {
            if (line.Contains('\t'))
            {
                devices.Add(line.Split('\t')[0]);
            }
        }
        return devices;
    }

    public static void InitLoginCaller()
    {
         if (!File.Exists(CALLER_NAME))
        {
            File.WriteAllText(CALLER_NAME, TOKEN);
        }
    }
    public string runShell(string command)
    {
        return RunAdb($"adb -s {DeviceID} shell {command}");
    }
    
    public static string RunAdb(string cmd, int timeout = 10)
        {
            int countWaitFail = 0;
            int maxWaitFail = 3;
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
                {
                    countWaitFail++;
                    goto Again;
                }

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