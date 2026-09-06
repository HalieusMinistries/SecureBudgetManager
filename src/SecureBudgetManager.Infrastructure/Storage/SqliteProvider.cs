using Microsoft.Data.Sqlite;

namespace SecureBudgetManager.Infrastructure.Storage;

internal static class SqliteProvider
{
    private static readonly object Gate = new();
    private static bool _initialised;

    public static void EnsureInitialised()
    {
        lock (Gate)
        {
            if (_initialised)
            {
                return;
            }

            SQLitePCL.Batteries_V2.Init();
            _initialised = true;
        }
    }
}
