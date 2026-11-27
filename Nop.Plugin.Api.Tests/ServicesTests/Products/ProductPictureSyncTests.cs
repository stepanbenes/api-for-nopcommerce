using Moq;
using Nop.Core;
using Nop.Core.Domain.Catalog;
using Nop.Core.Domain.Common;
using Nop.Core.Domain.Customers;
using Nop.Core.Domain.Media;
using Nop.Data;
using Nop.Plugin.Api.Services;
using Nop.Services.Media;
using Nop.Services.Security;
using Nop.Services.Stores;
using NUnit.Framework;

namespace Nop.Plugin.Api.Tests.ServicesTests.Products;

public class ProductPictureSyncTests
{
    [TestCase("present")]
    [TestCase("unowned")]
    [TestCase("linked")]
    [TestCase("linked_wrong_picture")]
    [TestCase("duplicate_key")]
    [TestCase("shared_picture")]
    [TestCase("deleted_mapping")]
    [TestCase("repointed_mapping")]
    [TestCase("out_of_vendor_scope")]
    [TestCase("out_of_store_scope")]
    [TestCase("out_of_acl_scope")]
    public async Task ReadUsesStoredBytesOnlyForOneOwnedPicture(string scenario)
    {
        var product = new Product { Id = 10 };
        var picture = new Picture { Id = 20, SeoFilename = "hero", IsNew = true };
        var mapping = new ProductPicture { Id = 30, ProductId = product.Id, PictureId = scenario == "repointed_mapping" ? picture.Id + 1 : picture.Id };
        var maps = new List<ProductPicture>();
        if (scenario != "deleted_mapping")
            maps.Add(mapping);
        if (scenario == "shared_picture")
            maps.Add(new ProductPicture { Id = 31, ProductId = 11, PictureId = picture.Id });

        var ownership = new GenericAttribute
        {
            Id = 40, EntityId = product.Id, KeyGroup = nameof(Product), Key = "Api.CatalogSync.Picture.hero", Value = "30:20"
        };
        var linked = scenario is "linked" or "linked_wrong_picture";
        var present = scenario is "present" or "linked";
        var keys = new List<GenericAttribute>();
        if (!linked && scenario != "unowned")
            keys.Add(ownership);
        if (scenario == "duplicate_key")
            keys.Add(new GenericAttribute
            {
                Id = 41, EntityId = product.Id, KeyGroup = nameof(Product), Key = ownership.Key, Value = "32:20"
            });

        var pictures = new Mock<IPictureService>(MockBehavior.Strict);
        if (present)
        {
            pictures.Setup(service => service.GetPictureByIdAsync(picture.Id)).ReturnsAsync(picture);
            pictures.Setup(service => service.LoadPictureBinaryAsync(picture)).ReturnsAsync("abc"u8.ToArray());
        }

        var stores = new Mock<IStoreMappingService>(MockBehavior.Strict);
        stores.Setup(service => service.ApplyStoreMapping(It.IsAny<IQueryable<Product>>(), 2))
            .ReturnsAsync((IQueryable<Product> query, int _) =>
                scenario == "out_of_store_scope" ? query.Where(row => row.LimitedToStores) : query);
        var acl = new Mock<IAclService>(MockBehavior.Strict);
        var actor = new Customer { Id = 3, VendorId = scenario == "out_of_vendor_scope" ? 5 : 0 };
        acl.Setup(service => service.ApplyAcl(It.IsAny<IQueryable<Product>>(), actor))
            .ReturnsAsync((IQueryable<Product> query, Customer _) =>
                scenario == "out_of_acl_scope" ? query.Where(row => row.SubjectToAcl) : query);

        var database = new Mock<INopDataProvider>(MockBehavior.Strict);
        var keyRepository = linked ? new Mock<IRepository<GenericAttribute>>(MockBehavior.Strict).Object : Repository(keys);
        var sut = new ProductPictureSyncService(database.Object, Repository<Product>([product]), Repository(maps),
            keyRepository, null, null, pictures.Object, null, null, null, stores.Object, acl.Object);
        var result = linked
            ? await sut.ExecuteAsync(actor, 2, product.Id, null, null, CancellationToken.None, mapping.Id,
                scenario == "linked_wrong_picture" ? picture.Id + 1 : picture.Id)
            : await sut.ExecuteAsync(actor, 2, product.Id, "hero", null, CancellationToken.None);

        Assert.That(result.Status, Is.EqualTo(scenario switch
        {
            "present" or "linked" => "present",
            "unowned" => "missing",
            "linked_wrong_picture" or "out_of_vendor_scope" or "out_of_store_scope" or "out_of_acl_scope" => "not_found",
            _ => "ambiguous"
        }));
        if (present)
        {
            Assert.That(result.MappingId, Is.EqualTo(mapping.Id));
            Assert.That(result.PictureId, Is.EqualTo(picture.Id));
            Assert.That(result.Sha256, Is.EqualTo("ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad"));
        }
        database.VerifyNoOtherCalls();
    }

    private static IRepository<T> Repository<T>(IEnumerable<T> rows) where T : BaseEntity
    {
        var repository = new Mock<IRepository<T>>(MockBehavior.Strict);
        repository.SetupGet(service => service.Table).Returns(rows.AsQueryable());
        return repository.Object;
    }
}
