using Nop.Data;

namespace Nop.Plugin.Api.DataStructures
{
    public class ApiList<T> : List<T>
    {
        private ApiList(IEnumerable<T> items, int pageIndex, int pageSize) : base(items)
        {
            PageSize = pageSize;
            PageIndex = pageIndex;
        }

        public static ApiList<T> FromPage(IEnumerable<T> source, int pageIndex, int pageSize)
        {
            return new ApiList<T>(source.Skip(pageIndex * pageSize).Take(pageSize), pageIndex, pageSize);
        }

        public static async Task<ApiList<T>> CreateAsync(IQueryable<T> source, int pageIndex, int pageSize)
        {
            var items = await source.Skip(pageIndex * pageSize).Take(pageSize).ToListAsync();
            return new ApiList<T>(items, pageIndex, pageSize);
        }

        public static async Task<ApiList<T>> CreateAsync(IQueryable<T> source)
        {
            return new ApiList<T>(await source.ToListAsync(), 0, 0);
        }

        public int PageIndex { get; }
        public int PageSize { get; }
    }
}
