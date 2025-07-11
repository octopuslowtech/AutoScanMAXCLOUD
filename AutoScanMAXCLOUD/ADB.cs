﻿using System.Diagnostics;
using System.Net.Sockets;
using System.Runtime.InteropServices;
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
        return RunAdb($"adb -s {DeviceID} install -r {apkPath}", 100);
    }

    public string UninstallApp(string apkPath)
    {
        return RunAdb($"adb -s {DeviceID} uninstall {apkPath}", 100);
    }


    public bool ExistPackage(string packageName, int loopCheck = 5)
    {
        for (int i = 0; i < loopCheck; i++)
        {
            var lstPackage = RunShell("pm list packages -3");

            if (lstPackage.Contains(packageName))
                return true;

            Thread.Sleep(1000);
        }

        return false;
    }

    public void PushFile(string source, string destination)
    {
        string result = RunAdb($"adb -s {DeviceID} push {source} {destination}");
        Console.WriteLine(result);
    }

    public string GetState()
    {
        string result = RunAdb($"adb -s {DeviceID} get-state");
        return result;
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
    }

    public static string TOKEN;

    public enum AdbCaller
    {
        PING_PONG,
        LOGIN_DEVICE
    }

    public string sendBroadcastMaxCloud(AdbCaller action = AdbCaller.PING_PONG)
    {
        string shellCommand = $"am broadcast -a com.maxcloud.app.{action.ToString()} -n com.maxcloud.app/.AdbCaller";

        if (action == AdbCaller.LOGIN_DEVICE)
        {
            RunShell("monkey -p com.maxcloud.app -c android.intent.category.LAUNCHER 1");
            shellCommand += $" -e token {TOKEN}";
        }

        string adbOutput = RunShell(shellCommand);

        Match match = Regex.Match(adbOutput, @"data=""({.*?})""");

        if (!match.Success)
        {
            return string.Empty;
        }

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

    public static string RunScrcpy(string deviceID)
    {
        try
        {
            // Xác định nền tảng (Windows hoặc Linux)
            bool isWindows = RuntimeInformation.IsOSPlatform(OSPlatform.Windows);
            bool isLinux = RuntimeInformation.IsOSPlatform(OSPlatform.Linux);

            if (!isWindows && !isLinux)
            {
                return "Error: Unsupported platform. Only Windows and Linux are supported.";
            }

            // Xác định tên file thực thi của scrcpy
            string scrcpyFileName = isWindows ? "scrcpy.exe" : "scrcpy";

            // Đường dẫn tới scrcpy
            string scrcpyPath = Path.Combine(AppContext.BaseDirectory, "scrcpy", scrcpyFileName);

            // Kiểm tra xem file scrcpy có tồn tại không
            if (!File.Exists(scrcpyPath))
            {
                return $"Error: scrcpy executable not found at {scrcpyPath}";
            }

            // Đảm bảo scrcpy có quyền thực thi trên Linux
            if (isLinux)
            {
                try
                {
                    ProcessStartInfo chmodInfo = new ProcessStartInfo
                    {
                        FileName = "chmod",
                        Arguments = $"+x \"{scrcpyPath}\"",
                        UseShellExecute = false,
                        CreateNoWindow = true
                    };
                    using (Process chmodProcess = Process.Start(chmodInfo))
                    {
                        chmodProcess?.WaitForExit();
                    }
                }
                catch (Exception ex)
                {
                    return $"Error setting executable permission for scrcpy: {ex.Message}";
                }
            }

            // Cấu hình lệnh chạy scrcpy
            ProcessStartInfo startInfo = new ProcessStartInfo
            {
                FileName = scrcpyPath,
                Arguments = $"-s {deviceID} --window-title \"{deviceID}\"",
                UseShellExecute = isWindows, // UseShellExecute = true trên Windows để chạy ngầm, false trên Linux
                CreateNoWindow = isWindows, // Ẩn cửa sổ console trên Windows
                RedirectStandardOutput = false, // Không chuyển hướng output
                RedirectStandardError = false // Không chuyển hướng error
            };

            // Chạy scrcpy ngầm
            using (Process process = new Process { StartInfo = startInfo })
            {
                try
                {
                    process.Start();
                    return "scrcpy started successfully in the background";
                }
                catch (Exception ex)
                {
                    return $"Error starting scrcpy: {ex.Message}";
                }
            }
        }
        catch (Exception ex)
        {
            return $"Error running scrcpy: {ex.Message}";
        }
    }

    public static void ScanDevice(string ip, string port, CancellationToken token)
    {
        try
        {
            using (TcpClient client = new TcpClient())
            {
                var connectTask = client.ConnectAsync(ip, int.Parse(port));

                if (!connectTask.Wait(TimeSpan.FromSeconds(5), token))
                    return;

                RunAdb($"adb connect {ip}:{port}", 5);
            }
        }
        catch (Exception)
        {
        }
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