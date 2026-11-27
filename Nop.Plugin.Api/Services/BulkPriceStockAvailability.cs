using System.Data.Common;
using LinqToDB.Data;
using Nop.Data;
using Nop.Plugin.Api.Domain;

namespace Nop.Plugin.Api.Services;

public sealed class BulkPriceStockAvailability(INopDataProvider dataProvider)
{
    internal const string TableName = nameof(BulkPriceStockReceipt);

    public async Task<bool> IsAvailableAsync()
    {
        var query = DataSettingsManager.LoadSettings().DataProvider switch
        {
            DataProviderType.SqlServer => """
                SELECT 1
                FROM sys.indexes AS i
                JOIN sys.index_columns AS k ON k.object_id = i.object_id AND k.index_id = i.index_id
                JOIN sys.columns AS c ON c.object_id = k.object_id AND c.column_id = k.column_id
                WHERE i.object_id = OBJECT_ID(@tableName, 'U') AND i.name = @indexName
                    AND i.is_unique = 1 AND i.is_disabled = 0 AND i.is_hypothetical = 0 AND i.has_filter = 0 AND i.ignore_dup_key = 0
                    AND k.key_ordinal > 0
                GROUP BY i.index_id
                HAVING COUNT(*) = 4 AND COUNT(DISTINCT CASE WHEN c.name IN ('ActorId', 'StoreId', 'OperationId', 'Version') THEN c.name END) = 4
                """,
            DataProviderType.PostgreSQL => """
                SELECT 1
                FROM pg_index AS i
                JOIN pg_class AS t ON t.oid = i.indrelid
                JOIN pg_class AS idx ON idx.oid = i.indexrelid
                JOIN LATERAL unnest(i.indkey) WITH ORDINALITY AS k(attnum, ordinal) ON k.ordinal <= i.indnkeyatts
                JOIN pg_attribute AS a ON a.attrelid = t.oid AND a.attnum = k.attnum
                WHERE t.oid = to_regclass(quote_ident(CAST(@tableName AS text))) AND t.relkind IN ('r', 'p')
                    AND idx.relname = CAST(@indexName AS text)
                    AND i.indisunique AND i.indisvalid AND i.indisready AND i.indislive AND i.indimmediate
                    AND i.indpred IS NULL AND i.indexprs IS NULL
                GROUP BY i.indexrelid
                HAVING COUNT(*) = 4 AND COUNT(DISTINCT a.attname) FILTER (WHERE a.attname IN ('ActorId', 'StoreId', 'OperationId', 'Version')) = 4
                """,
            DataProviderType.MySql => """
                SELECT 1
                FROM information_schema.statistics AS i
                JOIN information_schema.tables AS t ON t.table_schema = i.table_schema AND t.table_name = i.table_name
                WHERE i.table_schema = DATABASE() AND i.index_name = @indexName
                    AND CASE WHEN @@lower_case_table_names = 0 THEN BINARY i.table_name = BINARY @tableName
                        ELSE i.table_name = @tableName END
                    AND i.non_unique = 0 AND t.table_type = 'BASE TABLE' AND t.engine = 'InnoDB'
                GROUP BY i.index_name
                HAVING COUNT(*) = 4 AND COUNT(DISTINCT CASE WHEN i.sub_part IS NULL
                    AND i.column_name IN ('ActorId', 'StoreId', 'OperationId', 'Version') THEN i.column_name END) = 4
                """,
            _ => null
        };

        if (query is null)
            return false;

        try
        {
            var rows = await dataProvider.QueryAsync<int>(query,
                new DataParameter("tableName", TableName),
                new DataParameter("indexName", BulkPriceStockReceipt.OperationIndexName));
            return rows.Count != 0;
        }
        catch (DbException exception)
        {
            throw new BulkPriceStockBusyException("The bulk receipt schema could not be checked.", exception);
        }
    }
}
