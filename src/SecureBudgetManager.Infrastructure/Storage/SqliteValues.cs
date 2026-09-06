using System.Globalization;
using Microsoft.Data.Sqlite;
using SecureBudgetManager.Core.Models;

namespace SecureBudgetManager.Infrastructure.Storage;

/// <summary>
/// Conversions between SQLite storage types and domain types.
///
/// Money and other decimals are stored as invariant decimal strings. A REAL column would lose
/// cents on values such as 0.1, which is unacceptable in a budgeting application.
/// </summary>
internal static class SqliteValues
{
    private const string DateFormat = "yyyy-MM-dd";

    public static string ToText(Money value) =>
        value.Amount.ToString(CultureInfo.InvariantCulture);

    public static string ToText(decimal value) =>
        value.ToString(CultureInfo.InvariantCulture);

    public static string ToText(DateOnly value) =>
        value.ToString(DateFormat, CultureInfo.InvariantCulture);

    public static string ToText(Guid value) => value.ToString("D");

    public static object ToNullable(Money? value) =>
        value is { } money ? ToText(money) : DBNull.Value;

    public static object ToNullable(decimal? value) =>
        value is { } number ? ToText(number) : DBNull.Value;

    public static object ToNullable(DateOnly? value) =>
        value is { } date ? ToText(date) : DBNull.Value;

    public static object ToNullable(Guid? value) =>
        value is { } id ? ToText(id) : DBNull.Value;

    public static object ToNullable(string? value) =>
        string.IsNullOrEmpty(value) ? DBNull.Value : value;

    public static object ToNullable(int? value) =>
        value is { } number ? number : DBNull.Value;

    public static Money GetMoney(SqliteDataReader reader, string column) =>
        new(decimal.Parse(reader.GetString(reader.GetOrdinal(column)), CultureInfo.InvariantCulture));

    public static Money? GetNullableMoney(SqliteDataReader reader, string column)
    {
        var index = reader.GetOrdinal(column);
        return reader.IsDBNull(index)
            ? null
            : new Money(decimal.Parse(reader.GetString(index), CultureInfo.InvariantCulture));
    }

    public static Money GetMoneyOrZero(SqliteDataReader reader, string column) =>
        GetNullableMoney(reader, column) ?? Money.Zero;

    public static decimal GetDecimal(SqliteDataReader reader, string column) =>
        decimal.Parse(reader.GetString(reader.GetOrdinal(column)), CultureInfo.InvariantCulture);

    public static decimal? GetNullableDecimal(SqliteDataReader reader, string column)
    {
        var index = reader.GetOrdinal(column);
        return reader.IsDBNull(index)
            ? null
            : decimal.Parse(reader.GetString(index), CultureInfo.InvariantCulture);
    }

    public static decimal GetDecimalOrZero(SqliteDataReader reader, string column) =>
        GetNullableDecimal(reader, column) ?? 0m;

    public static DateOnly GetDate(SqliteDataReader reader, string column) =>
        DateOnly.ParseExact(reader.GetString(reader.GetOrdinal(column)), DateFormat, CultureInfo.InvariantCulture);

    public static DateOnly? GetNullableDate(SqliteDataReader reader, string column)
    {
        var index = reader.GetOrdinal(column);
        return reader.IsDBNull(index)
            ? null
            : DateOnly.ParseExact(reader.GetString(index), DateFormat, CultureInfo.InvariantCulture);
    }

    public static Guid GetGuid(SqliteDataReader reader, string column) =>
        Guid.Parse(reader.GetString(reader.GetOrdinal(column)));

    public static Guid? GetNullableGuid(SqliteDataReader reader, string column)
    {
        var index = reader.GetOrdinal(column);
        return reader.IsDBNull(index) ? null : Guid.Parse(reader.GetString(index));
    }

    public static string? GetNullableString(SqliteDataReader reader, string column)
    {
        var index = reader.GetOrdinal(column);
        return reader.IsDBNull(index) ? null : reader.GetString(index);
    }

    public static bool GetBool(SqliteDataReader reader, string column) =>
        reader.GetInt64(reader.GetOrdinal(column)) != 0;

    public static bool GetBoolOrDefault(SqliteDataReader reader, string column, bool fallback)
    {
        for (var index = 0; index < reader.FieldCount; index++)
        {
            if (string.Equals(reader.GetName(index), column, StringComparison.OrdinalIgnoreCase))
            {
                return reader.IsDBNull(index) ? fallback : reader.GetInt64(index) != 0;
            }
        }

        return fallback;
    }

    public static int GetInt(SqliteDataReader reader, string column) =>
        (int)reader.GetInt64(reader.GetOrdinal(column));

    public static int? GetNullableInt(SqliteDataReader reader, string column)
    {
        var index = reader.GetOrdinal(column);
        return reader.IsDBNull(index) ? null : (int)reader.GetInt64(index);
    }

    public static TEnum GetEnum<TEnum>(SqliteDataReader reader, string column)
        where TEnum : struct, Enum =>
        (TEnum)Enum.ToObject(typeof(TEnum), GetInt(reader, column));

    public static TEnum GetEnumOrDefault<TEnum>(SqliteDataReader reader, string column, TEnum fallback)
        where TEnum : struct, Enum
    {
        for (var index = 0; index < reader.FieldCount; index++)
        {
            if (string.Equals(reader.GetName(index), column, StringComparison.OrdinalIgnoreCase))
            {
                return reader.IsDBNull(index)
                    ? fallback
                    : (TEnum)Enum.ToObject(typeof(TEnum), (int)reader.GetInt64(index));
            }
        }

        return fallback;
    }

    public static TEnum? GetNullableEnum<TEnum>(SqliteDataReader reader, string column)
        where TEnum : struct, Enum
    {
        var value = GetNullableInt(reader, column);
        return value is null ? null : (TEnum)Enum.ToObject(typeof(TEnum), value.Value);
    }
}
