using Microsoft.AspNetCore.Mvc;
using Nop.Plugin.Api.ModelBinders;

namespace Nop.Plugin.Api.Attributes
{
    public class FromQueryJsonAttribute : ModelBinderAttribute
    {
        public FromQueryJsonAttribute()
        {
            BinderType = typeof(JsonQueryModelBinder);
        }

        public FromQueryJsonAttribute(string paramName)
            : this()
        {
            Name = paramName;
        }
    }
}
