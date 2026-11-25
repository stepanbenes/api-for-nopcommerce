using Nop.Core.Infrastructure.Mapper;

namespace Nop.Plugin.Api.MappingExtensions
{
    internal static class BaseMapping
    {
        public static TDestination MapTo<TSource, TDestination>(this TSource source)
        {
            return MapperConfiguration.Mapper.Map<TSource, TDestination>(source);
        }
    }
}
