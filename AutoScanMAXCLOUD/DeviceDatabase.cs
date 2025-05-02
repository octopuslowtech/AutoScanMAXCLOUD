using Microsoft.Data.Sqlite;

namespace AutoScanMAXCLOUD;

public class DeviceDatabase
{
    private const string DATABASE_PATH = "devices.db";
    private static bool _initialized = false;
    private static readonly object _lock = new object();

    public static void Initialize()
    {
        lock (_lock)
        {
            if (_initialized) return;

            using var connection = CreateConnection();
            using var command = connection.CreateCommand();
            command.CommandText = @"
                    CREATE TABLE IF NOT EXISTS Devices (
                        ProductNumber TEXT PRIMARY KEY,
                        IpAddress TEXT,
                        DeviceId TEXT,
                        Serial TEXT,
                        LastUpdated TEXT
                    )";
            command.ExecuteNonQuery();
            _initialized = true;
        }
    }

    private static SqliteConnection CreateConnection()
    {
        var connection = new SqliteConnection($"Data Source={DATABASE_PATH}");
        connection.Open();
        return connection;
    }

    public static void SaveDeviceInfo(string productNumber, string ipAddress, string deviceId, string serial)
    {
        if (string.IsNullOrEmpty(productNumber)) return;
        if (!_initialized) Initialize();

        using var connection = CreateConnection();
        using var command = connection.CreateCommand();
        command.CommandText = @"
                INSERT OR REPLACE INTO Devices (ProductNumber, IpAddress, DeviceId, Serial, LastUpdated)
                VALUES (@productNumber, @ipAddress, @deviceId, @serial, @lastUpdated)";

        command.Parameters.AddWithValue("@productNumber", productNumber);
        command.Parameters.AddWithValue("@ipAddress", ipAddress);
        command.Parameters.AddWithValue("@deviceId", deviceId);
        command.Parameters.AddWithValue("@serial", serial);
        command.Parameters.AddWithValue("@lastUpdated", DateTime.Now.ToString("o"));

        command.ExecuteNonQuery();
    }
    
    public static string GetIPAddressByProductNumber(string productNumber)
    {
        if (string.IsNullOrEmpty(productNumber)) return string.Empty;
        if (!_initialized) Initialize();

        using var connection = CreateConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT IpAddress FROM Devices WHERE ProductNumber = @productNumber";
        command.Parameters.AddWithValue("@productNumber", productNumber);

        return command.ExecuteScalar()?.ToString() ?? string.Empty;
    }

    public static (string ipAddress, string deviceId, string productNumber, string serial) GetDeviceInfoBySearch(string search)
    {
        if (string.IsNullOrEmpty(search)) return (string.Empty, string.Empty, string.Empty, string.Empty);
        if (!_initialized) Initialize();

        using var connection = CreateConnection();
        using var command = connection.CreateCommand();
        command.CommandText = @"
            SELECT IpAddress, DeviceId, ProductNumber, Serial 
            FROM Devices 
            WHERE ProductNumber = @search 
            OR DeviceId = @search
            OR IpAddress = @search
            OR Serial = @search";
        command.Parameters.AddWithValue("@search", search);

        using var reader = command.ExecuteReader();
        if (reader.Read())
        {
            return (reader.GetString(0), reader.GetString(1), reader.GetString(2), reader.GetString(3));
        }

        return (string.Empty, string.Empty, string.Empty, string.Empty);
    }
}

