namespace Nop.Plugin.Api.Converters
{
    public interface IObjectConverter
    {
        T ToObject<T>(ICollection<KeyValuePair<string, string>> source, Action<string, string> onError = null)
            where T : class, new();
    }
}
