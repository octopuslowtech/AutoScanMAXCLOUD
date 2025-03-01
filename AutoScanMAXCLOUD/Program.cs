 
using System.Net;
using AutoScanMAXCLOUD;
using SharpAdbClient;

var lstDeviceRunning = new List<Thread>();

void RunDeviceThread(string deviceId)
{
    var adb = new ADB(deviceId);
    while (true)
    {
        var runningAccessibilityServices = adb.runShell("settings get secure enabled_accessibility_services");

        if (!runningAccessibilityServices.Contains("com.maxcloud.app"))
        {
            var lstPackage = adb.runShell("pm list packages");

            if (!lstPackage.Contains("com.maxcloud.app"))
            {
                adb.InstallApp("maxcloud.apk");
                Thread.Sleep(3000);
            }
            else
                adb.runShell("am force-stop com.maxcloud.app");

            if (!lstPackage.Contains("vn.onox.helper"))
                adb.InstallApp("helper.apk");
            
            adb.runShell("am force-stop com.maxcloud.app");
            
            adb.SetupMaxCloud();

            adb.runShell("settings put secure enabled_accessibility_services com.maxcloud.app/com.maxcloud.app.Core.MainService");
            adb.runShell("settings put secure accessibility_enabled 1");
        }
        
        Thread.Sleep(TimeSpan.FromMinutes(1)); 
    }
}

void OnDeviceConnected(object sender, DeviceDataEventArgs e)
{
    string deviceId = e.Device.ToString();

    if(lstDeviceRunning.Any(x => x.Name == deviceId))
        return;
       
    var thread = new Thread(() => RunDeviceThread(deviceId));
    thread.Name = deviceId;
    lstDeviceRunning.Add(thread);
    thread.Start();
}

void OnDeviceDisconnected(object sender, DeviceDataEventArgs e)
{
    string deviceId = e.Device.ToString();

    var thread = lstDeviceRunning.FirstOrDefault(x => x.Name == deviceId);
    if (thread != null)
    {
        thread.Abort();
        lstDeviceRunning.Remove(thread);
    }
}

ADB.InitLoginCaller();

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
       Console.WriteLine($"Running Device: {lstDeviceRunning.Count}");
         Thread.Sleep(5000);
    }
}).Start();


Console.WriteLine("Press any key to exit");
Console.ReadKey();