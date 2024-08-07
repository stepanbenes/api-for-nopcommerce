﻿namespace Nop.Plugin.Api.Converters
{
    public interface IApiTypeConverter
    {
        DateTime? ToUtcDateTimeNullable(string value);
        int ToInt(string value);
        int? ToIntNullable(string value);
        IList<int> ToListOfInts(string value);
        IList<string> ToListOfStrings(string value);
        bool? ToBoolean(string value);
        object ToEnumNullable(string value, Type type);
    }
}
