using System.Net;
using AutoScanMAXCLOUD;
using SharpAdbClient;

var lstDeviceRunning = new List<Thread>();
var semaphore = new SemaphoreSlim(1, 1);

void WriteLog(string message)
{
    Console.WriteLine($"[{DateTime.Now.ToString("h:mm:ss")}] {message}");
}



void RunDeviceThread(string deviceId)
{
    var adb = new ADB(deviceId);
    bool isFirst = true;
    int nextCheckDelay = 60; 
    int failedStartAttempts = 0; 
    const int maxFailedAttempts = 5; 

    while (true)
    {
        if (isFirst)
        {
            isFirst = false;
            adb.RunShell("pm list packages");
        }

        var runningAccessibilityServices = adb.RunShell("settings get secure enabled_accessibility_services");

        if (!runningAccessibilityServices.Contains("com.maxcloud.app"))
        {
            failedStartAttempts++; 

            var lstPackage = adb.RunShell("pm list packages");

            if (!lstPackage.Contains("vn.onox.helper"))
            {
                semaphore.Wait();
                try
                {
                    WriteLog($"{deviceId}: Installing Helper");
                    adb.InstallApp("helper.apk");
                }
                finally
                {
                    semaphore.Release();
                }
            }

            bool isInstalled = lstPackage.Contains("com.maxcloud.app");

            if (!isInstalled)
            {
                semaphore.Wait();
                try
                {
                    WriteLog($"{deviceId}: Installing MaxCloud");
                    adb.InstallApp("maxcloud.apk");
                }
                finally
                {
                    semaphore.Release();
                }
                
                adb.RunShell("settings put system screen_off_timeout 2147483647");

                adb.RunShell("settings put secure location_mode 0");
                adb.RunShell("settings put system accelerometer_rotation 0");
                adb.RunShell("settings put global master_sync_enabled 0");
                
            }
            else
            {
                adb.RunShell("am force-stop com.maxcloud.app");
            }

            adb.SetupMaxCloud();

            if (!isInstalled)
            {
                WriteLog($"{deviceId}: Waiting for MaxCloud to stabilize (5s)");
                Thread.Sleep(TimeSpan.FromSeconds(5));
            }

            WriteLog($"{deviceId}: Starting Service");

            adb.RunShell(
                "settings put secure enabled_accessibility_services com.maxcloud.app/com.maxcloud.app.Core.MainService");

            nextCheckDelay = 10;

            if (failedStartAttempts >= maxFailedAttempts)
            {
                WriteLog($"{deviceId}: Service failed to start after {maxFailedAttempts} attempts. Rebooting device...");
                adb.Reboot();
                return; 
            }
        }
        else
        {
            failedStartAttempts = 0;
            nextCheckDelay = 60;
        }

        Thread.Sleep(TimeSpan.FromSeconds(nextCheckDelay));
    }
}



void OnDeviceConnected(object sender, DeviceDataEventArgs e)
{
    try
    {
        string deviceId = e.Device.ToString();

        if (lstDeviceRunning.Any(x => x.Name == deviceId))
            return;

        var thread = new Thread(() => RunDeviceThread(deviceId));
        thread.Name = deviceId;
        lstDeviceRunning.Add(thread);
        thread.Start();
    }
    catch (Exception ex)
    {
        Console.WriteLine(ex);
    }
}

void OnDeviceDisconnected(object sender, DeviceDataEventArgs e)
{
    try
    {
        string deviceId = e.Device.ToString();

        var thread = lstDeviceRunning.FirstOrDefault(x => x.Name == deviceId);
        if (thread != null)
        {
            thread.Abort();
            lstDeviceRunning.Remove(thread);
        }
    }
    catch (Exception ex)
    {
        Console.WriteLine(ex);
    }
}


Console.ForegroundColor = ConsoleColor.Green;

Console.WriteLine("Input your token here: ");

string input = Console.ReadLine();

if (string.IsNullOrEmpty(input))
    throw new Exception("Token is required");

ADB.TOKEN = input;

ADB.InitLoginCaller();

// ADB.RunAdb("adb kill-server");

AdbServer server = new AdbServer();
server.StartServer("adb.exe", restartServerIfNewer: false);

var monitor = new DeviceMonitor(new AdbSocket(new IPEndPoint(IPAddress.Loopback, AdbClient.AdbServerPort)));

monitor.DeviceConnected += OnDeviceConnected;
monitor.DeviceDisconnected += OnDeviceDisconnected;

monitor.Start();

new Thread(() =>
{
    while (true)
    {
        WriteLog($"Running Device: {lstDeviceRunning.Count}");
        Thread.Sleep(TimeSpan.FromSeconds(10));
    }
}).Start();


Console.ReadKey();