using LinqToDB.Async;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Nop.Core;
using Nop.Core.Domain.Catalog;
using Nop.Core.Domain.Customers;
using Nop.Core.Domain.Security;
using Nop.Core.Domain.Stores;
using Nop.Data;
using Nop.Services.Authentication;
using Nop.Services.Customers;

namespace Nop.Plugin.Api.Services;

public sealed class RelationshipWrites<TEntity>(
    INopDataProvider dataProvider,
    IRepository<TEntity> repository) where TEntity : BaseEntity
{
    public IQueryable<TEntity> Table => repository.Table;

    public Task<TEntity> GetByIdAsync(int id) => repository.GetByIdAsync(id);

    public async Task<IActionResult> ExecuteAsync(Func<List<Func<Task>>, Task<IActionResult>> write,
        CancellationToken cancellationToken)
    {
        if (!BulkPriceStockService.SupportsTransactionalWrites())
            return Problem(StatusCodes.Status503ServiceUnavailable, "This route requires transaction enlistment.");

        for (var attempt = 1; ; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var onCommit = new List<Func<Task>>();
            IActionResult result;
            try
            {
                using var transaction = dataProvider.CreateTransactionScope();
                result = await write(onCommit);
                cancellationToken.ThrowIfCancellationRequested();
                transaction.Complete();
            }
            catch (Exception exception) when (BulkPriceStockService.IsConcurrentWrite(exception))
            {
                if (attempt >= 3)
                    return Problem(StatusCodes.Status409Conflict, "A concurrent write changed this relationship. Retry the request.");

                await Task.Delay(TimeSpan.FromMilliseconds(25 * attempt), cancellationToken);
                continue;
            }

            foreach (var action in onCommit)
                await action();

            return result;
        }
    }

    private static ObjectResult Problem(int status, string title) =>
        new(new ProblemDetails { Status = status, Title = title }) { StatusCode = status };
}

public sealed class RelationshipWriteScope(
    IRepository<Product> productRepository,
    IRepository<StoreMapping> storeMappingRepository,
    IRepository<AclRecord> aclRepository,
    IAuthenticationService authenticationService,
    IStoreContext storeContext,
    ICustomerService customerService,
    CatalogSettings catalogSettings)
{
    public async Task<bool> CanWriteProductAsync(int productId)
    {
        var actor = await authenticationService.GetAuthenticatedCustomerAsync();
        var product = await productRepository.GetByIdAsync(productId, includeDeleted: false);
        return actor != null && product != null && (actor.VendorId == 0 || product.VendorId == actor.VendorId) &&
            await IsInScopeAsync(product, actor);
    }

    public async Task<bool> IsInScopeAsync<TEntity>(TEntity entity)
        where TEntity : BaseEntity, IAclSupported, IStoreMappingSupported =>
        await IsInScopeAsync(entity, await authenticationService.GetAuthenticatedCustomerAsync());

    private async Task<bool> IsInScopeAsync<TEntity>(TEntity entity, Customer actor)
        where TEntity : BaseEntity, IAclSupported, IStoreMappingSupported
    {
        if (entity == null || actor == null)
            return false;

        var entityName = typeof(TEntity).Name;
        if (entity.LimitedToStores && !catalogSettings.IgnoreStoreLimitations)
        {
            var store = await storeContext.GetCurrentStoreAsync();
            if (store.Id != 0 && !await storeMappingRepository.Table.AnyAsync(mapping =>
                    mapping.EntityName == entityName && mapping.EntityId == entity.Id && mapping.StoreId == store.Id))
                return false;
        }

        if (!entity.SubjectToAcl || catalogSettings.IgnoreAcl)
            return true;

        var roles = await customerService.GetCustomerRoleIdsAsync(actor);
        return await aclRepository.Table.AnyAsync(record =>
            record.EntityName == entityName && record.EntityId == entity.Id && roles.Contains(record.CustomerRoleId));
    }
}
