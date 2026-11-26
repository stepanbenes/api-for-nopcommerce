using Nop.Core;

namespace Nop.Plugin.Api.Domain;

public sealed class BulkPriceStockReceipt : BaseEntity
{
    internal const string OperationIndexName = "UX_BulkPriceStockReceipt_Operation";

    public int ActorId { get; set; }
    public int StoreId { get; set; }
    public Guid OperationId { get; set; }
    public long Version { get; set; }
    public string RequestHash { get; set; }
    public string ResponseJson { get; set; }
}
