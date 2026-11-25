using System.Globalization;

namespace Nop.Plugin.Api.Converters
{
    public class ApiTypeConverter : IApiTypeConverter
    {
        private static readonly string[] DateTimeFormats =
        [
            "yyyy", "yyyy-MM", "yyyy-MM-dd",
            "yyyy-MM-dd'T'HH:mmK", "yyyy-MM-dd'T'HH:mm:ssK", "yyyy-MM-dd'T'HH:mm:ss.FFFFFFFK",
            "yyyy-MM-dd HH:mmK", "yyyy-MM-dd HH:mm:ssK", "yyyy-MM-dd HH:mm:ss.FFFFFFFK"
        ];

        /// <summary>
        /// Converts ISO 8601 and legacy query dates to UTC, assuming UTC for dates without an offset.
        /// </summary>
        public DateTime? ToUtcDateTimeNullable(string value)
        {
            return DateTimeOffset.TryParseExact(value?.Trim(), DateTimeFormats, CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal, out var result) ? result.UtcDateTime : null;
        }

        public int ToInt(string value)
        {
            int result;

            if (int.TryParse(value, out result))
            {
                return result;
            }

            return 0;
        }

        public int? ToIntNullable(string value)
        {
            int result;

            if (int.TryParse(value, out result))
            {
                return result;
            }

            return null;
        }

        public IList<int> ToListOfInts(string value)
        {
            if (!string.IsNullOrEmpty(value))
            {
                var stringIds = value.Split(new[]
                                            {
                                                ','
                                            }, StringSplitOptions.RemoveEmptyEntries).ToList();
                var intIds = new List<int>();

                foreach (var id in stringIds)
                {
                    int intId;
                    if (int.TryParse(id, out intId))
                    {
                        intIds.Add(intId);
                    }
                }

                intIds = intIds.Distinct().ToList();
                return intIds.Count > 0
                           ? intIds
                           : null;
            }

            return null;
        }

        public IList<string> ToListOfStrings(string value)
        {
            if (!string.IsNullOrEmpty(value))
            {
                var stringValues = value.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries).ToList();
                return stringValues;
            }
            return null;
        }

        public bool? ToBoolean(string value)
        {
            if (bool.TryParse(value, out bool result)) // should be case-insensitive
            {
                return result;
            }
            return null;
        }

        public object ToEnumNullable(string value, Type type)
        {
            if (!string.IsNullOrEmpty(value))
            {
                var enumType = Nullable.GetUnderlyingType(type);

                var enumNames = enumType.GetEnumNames();

                if (enumNames.Any(x => x.ToLowerInvariant().Equals(value.ToLowerInvariant())))
                {
                    return Enum.Parse(enumType, value, true);
                }
            }

            return null;
        }
    }
}
