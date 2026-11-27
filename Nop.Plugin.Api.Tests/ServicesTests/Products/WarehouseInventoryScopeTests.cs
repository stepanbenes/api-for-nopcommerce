using Moq;
using Nop.Core.Domain.Catalog;
using Nop.Core.Domain.Customers;
using Nop.Data;
using Nop.Plugin.Api.Services;
using Nop.Services.Security;
using Nop.Services.Stores;
using NUnit.Framework;

namespace Nop.Plugin.Api.Tests.ServicesTests.Products;

public class WarehouseInventoryScopeTests
{
    [Test]
    public async Task FiltersDeletedOtherVendorOtherStoreAndAclBeforePaging()
    {
        var actor = new Customer { Id = 8, VendorId = 7 };
        var products = new[]
        {
            new Product { Id = 1, VendorId = 7, Deleted = true },
            new Product { Id = 2, VendorId = 6 },
            new Product { Id = 3, VendorId = 7 },
            new Product { Id = 4, VendorId = 7 },
            new Product { Id = 5, VendorId = 7 },
            new Product { Id = 6, VendorId = 7 }
        };
        var inventories = products.Select(product => new ProductWarehouseInventory
        {
            Id = product.Id + 100,
            ProductId = product.Id,
            WarehouseId = 11
        });
        var productRepository = new Mock<IRepository<Product>>();
        productRepository.SetupGet(repository => repository.Table).Returns(products.AsQueryable());
        var inventoryRepository = new Mock<IRepository<ProductWarehouseInventory>>();
        inventoryRepository.SetupGet(repository => repository.Table).Returns(inventories.AsQueryable());
        var stores = new Mock<IStoreMappingService>();
        stores.Setup(service => service.ApplyStoreMapping(It.IsAny<IQueryable<Product>>(), 9))
            .ReturnsAsync((IQueryable<Product> query, int _) => query.Where(product => product.Id != 3));
        var acl = new Mock<IAclService>();
        acl.Setup(service => service.ApplyAcl(It.IsAny<IQueryable<Product>>(), actor))
            .ReturnsAsync((IQueryable<Product> query, Customer _) => query.Where(product => product.Id != 4));
        var scope = new ProductSyncScope(productRepository.Object, stores.Object, acl.Object);
        var service = new ProductWarehouseInventoriesApiService(inventoryRepository.Object, scope);

        var first = await service.GetMappingsAsync(actor, 9, warehouseId: 11, limit: 1);
        var second = await service.GetMappingsAsync(actor, 9, warehouseId: 11, limit: 1, page: 2);
        Assert.That(first.Select(row => row.Id), Is.EqualTo(new[] { 105 }));
        Assert.That(second.Select(row => row.Id), Is.EqualTo(new[] { 106 }));
        foreach (var id in new[] { 101, 102, 103, 104 })
            Assert.That(await service.GetScopedByIdAsync(id, actor, 9), Is.Null);
        Assert.That((await service.GetScopedByIdAsync(105, actor, 9)).ProductId, Is.EqualTo(5));
    }
}
