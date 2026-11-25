using Newtonsoft.Json;
using System.Reflection;

namespace Nop.Plugin.Api.Converters
{
    public class ObjectConverter : IObjectConverter
    {
        private readonly IApiTypeConverter _apiTypeConverter;

        public ObjectConverter(IApiTypeConverter apiTypeConverter)
        {
            _apiTypeConverter = apiTypeConverter;
        }

        public T ToObject<T>(ICollection<KeyValuePair<string, string>> source, Action<string, string> onError = null)
            where T : class, new()
        {
            var someObject = new T();
            var someObjectType = someObject.GetType();

            if (source != null)
            {
                var dateProperties = new HashSet<PropertyInfo>();
                foreach (var item in source)
                {
                    var itemKey = item.Key.Replace("_", string.Empty);
                    var property = someObjectType.GetProperty(itemKey, BindingFlags.IgnoreCase | BindingFlags.Public | BindingFlags.Instance);
                    if (property == null)
                        continue;

                    var value = To(item.Value, property.PropertyType);
                    if (onError != null && property.PropertyType == typeof(DateTime?))
                    {
                        var key = property.GetCustomAttribute<JsonPropertyAttribute>()?.PropertyName ?? property.Name;
                        if (!dateProperties.Add(property))
                        {
                            onError(key, "Supply this date filter only once.");
                            continue;
                        }

                        if (value == null)
                        {
                            onError(key, "Supply a valid date or timestamp, such as 2008-12-31 03:00 or 2008-12-31T03:00:00Z.");
                            continue;
                        }
                    }

                    property.SetValue(someObject, value);
                }
            }

            return someObject;
        }

        private object To(string value, Type type)
        {
            if (type == typeof(DateTime?))
            {
                return _apiTypeConverter.ToUtcDateTimeNullable(value);
            }
            if (type == typeof(int?))
            {
                return _apiTypeConverter.ToIntNullable(value);
            }
            if (type == typeof(int))
            {
                return _apiTypeConverter.ToInt(value);
            }
            if (type == typeof(List<int>))
            {
                return _apiTypeConverter.ToListOfInts(value);
            }
            if (type == typeof(List<string>))
            {
                return _apiTypeConverter.ToListOfStrings(value);
            }
            if (type == typeof(bool?))
            {
                return _apiTypeConverter.ToBoolean(value);
            }
            if (IsNullableEnum(type))
            {
                return _apiTypeConverter.ToEnumNullable(value, type);
            }

            // It should be the last resort, because it is not exception safe.
            return Convert.ChangeType(value, type);
        }

        private bool IsNullableEnum(Type t)
        {
            var u = Nullable.GetUnderlyingType(t);
            return u != null && u.IsEnum;
        }
    }
}
