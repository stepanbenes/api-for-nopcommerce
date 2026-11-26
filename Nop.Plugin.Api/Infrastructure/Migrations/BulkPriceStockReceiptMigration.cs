using FluentMigrator;
using Nop.Data.Extensions;
using Nop.Data.Migrations;
using Nop.Plugin.Api.Domain;

namespace Nop.Plugin.Api.Infrastructure.Migrations;

[NopMigration("2026-11-25 10:00:00", "API bulk price and stock receipts")]
public sealed class BulkPriceStockReceiptMigration : Migration
{
    public override void Up()
    {
        this.CreateTableIfNotExists<BulkPriceStockReceipt>();
        if (Schema.Table(nameof(BulkPriceStockReceipt)).Index(BulkPriceStockReceipt.OperationIndexName).Exists())
            return;

        Create.Index(BulkPriceStockReceipt.OperationIndexName)
            .OnTable(nameof(BulkPriceStockReceipt))
            .OnColumn(nameof(BulkPriceStockReceipt.ActorId)).Ascending()
            .OnColumn(nameof(BulkPriceStockReceipt.StoreId)).Ascending()
            .OnColumn(nameof(BulkPriceStockReceipt.OperationId)).Ascending()
            .OnColumn(nameof(BulkPriceStockReceipt.Version)).Ascending()
            .WithOptions().Unique();
    }

    public override void Down()
    {
        Delete.Table(nameof(BulkPriceStockReceipt));
    }
}
