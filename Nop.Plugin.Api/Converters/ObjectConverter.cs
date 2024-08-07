﻿using System.Reflection;

namespace Nop.Plugin.Api.Converters
{
    public class ObjectConverter : IObjectConverter
    {
        private readonly IApiTypeConverter _apiTypeConverter;

        public ObjectConverter(IApiTypeConverter apiTypeConverter)
        {
            _apiTypeConverter = apiTypeConverter;
        }

        public T ToObject<T>(ICollection<KeyValuePair<string, string>> source)
            where T : class, new()
        {
            var someObject = new T();
            var someObjectType = someObject.GetType();

            if (source != null)
            {
                foreach (var item in source)
                {
                    var itemKey = item.Key.Replace("_", string.Empty);
                    var currentProperty = someObjectType.GetProperty(itemKey,
                                                                     BindingFlags.IgnoreCase | BindingFlags.Public | BindingFlags.Instance);

                    if (currentProperty != null)
                    {
                        currentProperty.SetValue(someObject, To(item.Value, currentProperty.PropertyType), null);
                    }
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
