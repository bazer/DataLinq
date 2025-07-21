namespace DataLinq.Query;

public enum SqlFunctionType
{
    // Date and Time Functions
    DatePartYear,
    DatePartMonth,
    DatePartDay,
    DatePartDayOfYear,
    DatePartDayOfWeek, // Sunday=1, Saturday=7 for MySQL; Sunday=0, Saturday=6 for SQLite

    // Time Functions
    TimePartHour,
    TimePartMinute,
    TimePartSecond,
    TimePartMillisecond,

    // String Functions
    StringLength
}