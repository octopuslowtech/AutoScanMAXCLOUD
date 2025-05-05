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
                        Serial TEXT PRIMARY KEY,
                        IpAddress TEXT,
                        ProductNumber TEXT
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
    public static void SaveDeviceInfo(string productNumber, string ipAddress, string serial)
    {
        if (string.IsNullOrEmpty(productNumber)) return;
        if (!_initialized) Initialize();

        lock (_lock)
        {
            using var connection = CreateConnection();
            using var command = connection.CreateCommand();
            command.CommandText = @"
                INSERT OR REPLACE INTO Devices (ProductNumber, IpAddress, Serial)
                VALUES (@productNumber, @ipAddress, @serial)";

            command.Parameters.AddWithValue("@productNumber", productNumber);
            command.Parameters.AddWithValue("@ipAddress", ipAddress);
            command.Parameters.AddWithValue("@serial", serial);

            command.ExecuteNonQuery();
        }
    }
    
    public static (string ipAddress, string productNumber, string serial) GetDeviceInfoBySearch(string search)
    {
        if (string.IsNullOrEmpty(search)) return (string.Empty, string.Empty, string.Empty);
        if (!_initialized) Initialize();

        using var connection = CreateConnection();
        using var command = connection.CreateCommand();
        command.CommandText = @"
            SELECT IpAddress, ProductNumber, Serial 
            FROM Devices 
            WHERE ProductNumber = @search 
            OR IpAddress = @search
            OR Serial = @search";
        command.Parameters.AddWithValue("@search", search);

        using var reader = command.ExecuteReader();
        if (reader.Read())
        {
            return (reader.GetString(0), reader.GetString(1), reader.GetString(2));
        }

        return (string.Empty, string.Empty, string.Empty);
    }
}

