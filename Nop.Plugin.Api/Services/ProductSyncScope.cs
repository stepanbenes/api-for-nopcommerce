using Nop.Core.Domain.Catalog;
using Nop.Core.Domain.Customers;
using Nop.Data;
using Nop.Services.Security;
using Nop.Services.Stores;

namespace Nop.Plugin.Api.Services;

public sealed class ProductSyncScope(
    IRepository<Product> productRepository,
    IStoreMappingService storeMappingService,
    IAclService aclService)
{
    public async Task<Product> GetProductByIdAsync(int id, Customer actor, int storeId)
    {
        var products = await GetProductsAsync(actor, storeId);
        return await products.FirstOrDefaultAsync(product => product.Id == id);
    }

    public async Task<IQueryable<Product>> GetProductsAsync(Customer actor, int storeId)
    {
        var products = productRepository.Table.Where(product => !product.Deleted);
        if (actor.VendorId != 0)
            products = products.Where(product => product.VendorId == actor.VendorId);

        products = await storeMappingService.ApplyStoreMapping(products, storeId);
        return await aclService.ApplyAcl(products, actor);
    }
}
