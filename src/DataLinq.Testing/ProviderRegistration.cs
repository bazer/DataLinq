using DataLinq.MariaDB;
using DataLinq.MySql;
using DataLinq.SQLite;

namespace DataLinq.Testing;

public static class ProviderRegistration
{
    private static readonly object SyncRoot = new();
    private static bool _initialized;

    public static void EnsureRegistered()
    {
        if (_initialized)
            return;

        lock (SyncRoot)
        {
            if (_initialized)
                return;

            MySQLProvider.RegisterProvider();
            MariaDBProvider.RegisterProvider();
            SQLiteProvider.RegisterProvider();
            _initialized = true;
        }
    }
}
