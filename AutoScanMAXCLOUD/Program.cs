using System.Diagnostics;
using System.Net;
using System.Runtime.InteropServices;
using AutoScanMAXCLOUD;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using SharpAdbClient;

var lstDeviceRunning = new List<Thread>();

const string tokenFilePath = "token.txt";

Config? config;

SemaphoreSlim semaphore;


void WriteLog(string message)
{
    Console.WriteLine($"[{DateTime.Now.ToString("h:mm:ss")}] {message}");
}

string GetToken()
{
    if (File.Exists(tokenFilePath))
    {
        string savedToken = File.ReadAllText(tokenFilePath);
        Console.WriteLine($"Found saved token: {savedToken}");
        Console.WriteLine("Do you want to continue with this token? (y/n): ");
        string choice = Console.ReadLine()?.Trim().ToLower();

        if (choice == "y")
            return savedToken;
    }

    Console.WriteLine("Input your token here: ");
    string input = Console.ReadLine();

    if (string.IsNullOrEmpty(input))
        throw new Exception("Token is required");

    File.WriteAllText(tokenFilePath, input);
    return input;
}

void RunDeviceThread(string deviceId)
{
    var adb = new ADB(deviceId);
    int delayTimeout = config.ScanTimeout_Count * 1000;
    int failedStartAttempts = 0;
    const int maxFailedAttempts = 5;

    while (true)
    {
        var statusDevice = adb.sendBroadcastMaxCloud(ADB.AdbCaller.PING_PONG);

        if (string.IsNullOrEmpty(statusDevice))
        {
            if (!adb.ExistPackage(Constrants.MAXCLOUD_PACKAGE, 10))
            {
                semaphore.Wait();
                try
                {
                    WriteLog($"{deviceId}: Installing MaxCloud");
                    adb.InstallApp(Constrants.MAXCLOUD_APK);
                }
                finally
                {
                    semaphore.Release();
                }

                Thread.Sleep(TimeSpan.FromSeconds(5));

                adb.SetupMaxCloud();
            }

            if (!adb.ExistPackage("vn.onox.helper", 3))
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

                Thread.Sleep(TimeSpan.FromSeconds(2));

                var permissions = new List<string>
                {
                    "android.permission.NOTIFICATION_SERVICE",
                    "android.permission.POST_NOTIFICATIONS",
                    "android.permission.WRITE_EXTERNAL_STORAGE",
                    "android.permission.CHANGE_CONFIGURATION",
                };

                foreach (var permission in permissions)
                {
                    adb.RunShell($"pm grant vn.onox.helper {permission}");
                }
            }

            Thread.Sleep(delayTimeout);

            continue;
        }

        try
        {
            var json = JObject.Parse(statusDevice);

            string version = json["APP_VERSION"].ToString();

            if (version != Constrants.MAXCLOUD_VERSION)
            {
                semaphore.Wait();
                try
                {
                    WriteLog($"{deviceId}: Update MaxCloud to {Constrants.MAXCLOUD_VERSION}");
                    adb.InstallApp(Constrants.MAXCLOUD_APK);

                    Thread.Sleep(TimeSpan.FromSeconds(5));

                    continue;
                }
                finally
                {
                    semaphore.Release();
                }
            }

            bool isLogin = json["IS_LOGIN"].ToObject<bool>();

            if (!isLogin)
            {
                adb.SetupMaxCloud();

                WriteLog($"{deviceId}: START LOGIN DEVICE");

                var loginResult = adb.sendBroadcastMaxCloud(ADB.AdbCaller.LOGIN_DEVICE);

                if (string.IsNullOrEmpty(loginResult))
                {
                    WriteLog($"{deviceId}: LOGIN DEVICE failed");
                    Thread.Sleep(delayTimeout);
                    continue;
                }

                try
                {
                    var jsonLogin = JObject.Parse(loginResult);

                    string status = jsonLogin["MESSAGE"].ToString();

                    if (status != "LOGIN_SUCCESS")
                    {
                        WriteLog($"{deviceId}: {status}");
                        Thread.Sleep(delayTimeout);
                        continue;
                    }
                }
                catch (Exception ex)
                {
                }
            }

            bool isRunning = json["IS_RUNNING"].ToObject<bool>();

            if (!isRunning)
            {
                failedStartAttempts++;

                if (failedStartAttempts >= maxFailedAttempts)
                {
                    WriteLog(
                        $"{deviceId}: Service failed to start after {maxFailedAttempts} attempts. Rebooting device...");

                    adb.Reboot();

                    return;
                }

                if (isLogin)
                    adb.SetupMaxCloud();

                adb.RunShell("am force-stop com.maxcloud.app");

                adb.RunShell(
                    "settings put secure enabled_accessibility_services com.maxcloud.app/com.maxcloud.app.Core.MainService");
            }
            else
            {
                failedStartAttempts = 0;


                string productNumber = json["PRODUCT_NUMBER"].ToString();

                string ipAddress = json["IP_ADDRESS"].ToString();

                string serial = adb.RunShell("getprop ro.serialno").Trim();

                if (!string.IsNullOrEmpty(productNumber))
                    DeviceDatabase.SaveDeviceInfo(productNumber, ipAddress, serial);
            }


            Thread.Sleep(delayTimeout);
        }
        catch (Exception ex)
        {
            WriteLog($"{deviceId}: {ex.Message}");
            Thread.Sleep(delayTimeout);
        }
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
    catch
    {
    }
}

void RunIPLookupMode()
{
    Console.Clear();
    Console.WriteLine("===== IP Lookup Mode =====");
    Console.WriteLine("Enter 'exit' to return to the main menu.\n");

    while (true)
    {
        Console.Write("Enter product number, device ID, or serial number: ");
        string searchTerm = Console.ReadLine()?.Trim();

        if (string.IsNullOrEmpty(searchTerm))
        {
            Console.WriteLine("Search term cannot be empty. Please try again.");
            continue;
        }

        if (searchTerm.ToLower() == "exit")
        {
            Console.WriteLine("Returning to main menu...");
            SelectMode();
            return;
        }

        var (ipAddress, productNumber, serial) = DeviceDatabase.GetDeviceInfoBySearch(searchTerm);

        if (string.IsNullOrEmpty(ipAddress))
        {
            Console.WriteLine($"No device found for search term: {searchTerm}");
        }
        else
        {
            Console.WriteLine($"Found device:");
            Console.WriteLine($"IP Address: {ipAddress}");
            Console.WriteLine($"Product Number: {productNumber}");
            Console.WriteLine($"Serial Number: {serial}");
            ADB.RunScrcpy($"{ipAddress}:5555");
        }

        Console.WriteLine();
    }
}

void RunDeviceManagementMode()
{
    Console.Clear();
    Console.WriteLine("===== Device Management Mode =====");

    ADB.TOKEN = GetToken();

    var files = Directory.GetFiles(Directory.GetCurrentDirectory(), "*.apk", SearchOption.AllDirectories)
        .Where(x => x.Contains("MCP")).ToList();

    if (!files.Any())
        throw new Exception("maxcloud.apk not found");


    if (!File.Exists("config.txt"))
    {
        config = new();

        var configJson = JsonConvert.SerializeObject(config);

        File.WriteAllText("config.txt", configJson);
    }

    string configTxt = File.ReadAllText("config.txt");

    config = JsonConvert.DeserializeObject<Config>(configTxt);

    semaphore = new SemaphoreSlim(config.Lock_Count, config.Lock_Count);

    new Thread(() =>
    {
        List<string> baseIPs = File.ReadAllLines("address.txt")
            .Where(x => x.Length > 0)
            .ToList();

        if (!baseIPs.Any())
            throw new Exception("address.txt not found");

        while (true)
        {
            foreach (string ip in baseIPs)
            {
                Parallel.For(1, 255, new ParallelOptions { }, i =>
                {
                    string scanIP = $"{ip}.{i}";
                    ADB.ScanDevice(scanIP, "5555", CancellationToken.None);
                });
            }

            Thread.Sleep(TimeSpan.FromSeconds(30));
        }
    }).Start();

    // ADB.RunAdb("adb kill-server");

    AdbServer server = new AdbServer();
    server.StartServer(RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "adb.exe" : "adb",
        restartServerIfNewer: false);

    var monitor = new DeviceMonitor(new AdbSocket(new IPEndPoint(IPAddress.Loopback, AdbClient.AdbServerPort)));

    monitor.DeviceConnected += OnDeviceConnected;
    monitor.DeviceDisconnected += OnDeviceDisconnected;

    monitor.Start();

    new Thread(() =>
    {
        while (true)
        {
            int lockedCount = config.Lock_Count - semaphore.CurrentCount;

            WriteLog($"Running Device: {lstDeviceRunning.Count} Lock: {lockedCount}/{config.Lock_Count}");
            Thread.Sleep(TimeSpan.FromSeconds(10));
        }
    }).Start();

    new Thread(() =>
    {
        while (true)
        {
            var files = Directory.GetFiles(Directory.GetCurrentDirectory(), "*.apk", SearchOption.AllDirectories)
                .Where(x => x.Contains("MCP")).ToList();

            if (files.Any())
            {
                var latestFile = files.OrderByDescending(x => new FileInfo(x).LastWriteTime).FirstOrDefault();


                Constrants.MAXCLOUD_APK =
                    latestFile.Split(RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "\\" : "/").Last();

                var version = Constrants.MAXCLOUD_APK
                    .Replace("MCP_v", "")
                    .Replace(".apk", "");

                Constrants.MAXCLOUD_VERSION = version;
            }

            if (string.IsNullOrEmpty(Constrants.MAXCLOUD_APK))
                WriteLog("MaxCloud APK not found");

            Thread.Sleep(TimeSpan.FromMinutes(1));
        }
    }).Start();

    Console.ReadKey();
}

void SelectMode()
{
    Console.Clear();
    Console.ForegroundColor = ConsoleColor.Green;

    Console.WriteLine("NEW VERSION 1.0.0");
    Console.WriteLine("1. Device Management Mode");
    Console.WriteLine("2. IP Lookup Mode");
    Console.WriteLine("0. Exit");
    Console.Write("\nSelect mode: ");

    string input = Console.ReadLine()?.Trim();

    if (string.IsNullOrEmpty(input) || !int.TryParse(input, out int mode))
    {
        Console.WriteLine("Invalid selection. Please try again.");
        Thread.Sleep(1500);
        SelectMode();
        return;
    }

    switch (mode)
    {
        case (int)ProgramMode.DeviceManagement:
            RunDeviceManagementMode();
            break;
        case (int)ProgramMode.IPLookup:
            RunIPLookupMode();
            break;
        case 0:
            Environment.Exit(0);
            break;
        default:
            Console.WriteLine("Invalid selection. Please try again.");
            Thread.Sleep(1500);
            SelectMode();
            break;
    }
}

SelectMode();