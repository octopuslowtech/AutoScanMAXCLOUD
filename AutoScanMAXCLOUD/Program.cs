using System.Net;
using System.IO;
using AutoScanMAXCLOUD;
using Newtonsoft.Json.Linq;
using SharpAdbClient;

var lstDeviceRunning = new List<Thread>();
var semaphore = new SemaphoreSlim(3, 3);

const string tokenFilePath = "token.txt";

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
        {
            return savedToken;
        }
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
    int delayTimeout = 60;
    int failedStartAttempts = 0;
    const int maxFailedAttempts = 5;

    while (true)
    {
        var lstPackage = adb.RunShell("pm list packages");
        
        if (!lstPackage.Contains(Constrants.MAXCLOUD_PACKAGE))
        {
            semaphore.Wait();
            try
            {
                WriteLog($"{deviceId}: Installing MaxCloud");
                string result = adb.InstallApp(Constrants.MAXCLOUD_APK);
                WriteLog($"{deviceId} {result}");
            }
            finally
            {
                semaphore.Release();
            }
            continue;
        }
        
        var statusDevice = adb.sendBroadcastMaxCloud(ADB.AdbCaller.PING_PING);

        if (string.IsNullOrEmpty(statusDevice))
        {
            WriteLog($"{deviceId}: PING_PONG failed");
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
                    string result = adb.InstallApp(Constrants.MAXCLOUD_APK);
                    WriteLog($"{deviceId} {result}");
                }
                finally
                {
                    semaphore.Release();
                }
                continue;
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
                
                var jsonLogin = JObject.Parse(loginResult);
                
                string status = jsonLogin["MESSAGE"].ToString();
                
                if (status != "LOGIN_SUCCESS")
                {
                    WriteLog($"{deviceId}: {status}");
                    Thread.Sleep(delayTimeout);
                    continue;
                }
                WriteLog($"{deviceId}: LOGIN DEVICE success");
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
                
                if(isLogin)
                    adb.SetupMaxCloud();

                adb.RunShell("am force-stop com.maxcloud.app");
                
                adb.RunShell(
                    "settings put secure enabled_accessibility_services com.maxcloud.app/com.maxcloud.app.Core.MainService");
            }
            else
            {
                failedStartAttempts = 0;
            }
            
            string productNumber = json["PRODUCT_NUMBER"].ToString();
            string ipAddress = json["IP_ADDRESS"].ToString();
            
            DeviceDatabase.SaveDeviceInfo(productNumber, ipAddress);
            
            Thread.Sleep(delayTimeout);
        }
        catch(Exception ex) 
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


Console.ForegroundColor = ConsoleColor.Green;

ADB.TOKEN = GetToken();

DeviceDatabase.Initialize();

var files = Directory.GetFiles(Directory.GetCurrentDirectory(), "*.apk", SearchOption.AllDirectories)
    .Where(x => x.Contains("MCP")).ToList();

if (!files.Any())
    throw new Exception("maxcloud.apk not found");


ADB.RunAdb("adb kill-server");

AdbServer server = new AdbServer();
server.StartServer("adb", restartServerIfNewer: false);

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


new Thread(() =>
{
    while (true)
    {
        var files = Directory.GetFiles(Directory.GetCurrentDirectory(), "*.apk", SearchOption.AllDirectories)
            .Where(x => x.Contains("MCP")).ToList();

        if (files.Any())
        {
            var latestFile = files.OrderByDescending(x => new FileInfo(x).LastWriteTime).FirstOrDefault();
            
            Constrants.MAXCLOUD_APK = latestFile.Split("/").Last();
            
            var version = Constrants.MAXCLOUD_APK
                .Replace("MCP_v", "")
                .Replace(".apk", "");
            
            Constrants.MAXCLOUD_VERSION = version;
        }
        
        if (string.IsNullOrEmpty(Constrants.MAXCLOUD_APK))
            WriteLog("MaxCloud APK not found");
        
        Thread.Sleep(TimeSpan.FromMinutes(10));
    }
}).Start();


Console.ReadKey();
