using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace AutoScanMAXCLOUD
{
    public static class DeviceDatabase
    {
        private static readonly string DatabaseFilePath = "devices.txt";
        private static readonly Dictionary<string, string> DeviceCache = new Dictionary<string, string>();
        private static readonly object FileLock = new object();

        static DeviceDatabase()
        {
            LoadDeviceData();
        }

        private static void LoadDeviceData()
        {
            lock (FileLock)
            {
                if (File.Exists(DatabaseFilePath))
                {
                    try
                    {
                        var lines = File.ReadAllLines(DatabaseFilePath);
                        DeviceCache.Clear();
                        
                        foreach (var line in lines)
                        {
                            if (string.IsNullOrWhiteSpace(line)) continue;
                            
                            var parts = line.Split(',');
                            if (parts.Length >= 2)
                            {
                                DeviceCache[parts[0].Trim()] = parts[1].Trim();
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"Error loading device database: {ex.Message}");
                    }
                }
            }
        }

        public static void SaveDeviceInfo(string productNumber, string ipAddress)
        {
            if (string.IsNullOrWhiteSpace(productNumber) || string.IsNullOrWhiteSpace(ipAddress))
                return;

            lock (FileLock)
            {
                try
                {
                    DeviceCache[productNumber] = ipAddress;
                    
                    var deviceEntries = DeviceCache.Select(kvp => $"{kvp.Key},{kvp.Value}");
                    File.WriteAllLines(DatabaseFilePath, deviceEntries);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error saving device info: {ex.Message}");
                }
            }
        }

        public static string GetIPAddressByProductNumber(string productNumber)
        {
            if (string.IsNullOrWhiteSpace(productNumber))
                return null;

            // Reload the database to ensure we have the latest data
            LoadDeviceData();
            
            // Check if the product number exists in our cache
            if (DeviceCache.TryGetValue(productNumber, out string ipAddress))
            {
                return ipAddress;
            }
            
            return null;
        }
    }
}
