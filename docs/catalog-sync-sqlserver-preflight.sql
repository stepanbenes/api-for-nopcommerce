-- Run before and after deploying the plugin to the same disposable SQL Server database.
-- Example: sqlcmd -E -S localhost -d YourDisposableClone -b -W -w 240 -i docs\catalog-sync-sqlserver-preflight.sql -o before.txt
-- Compare schema_sha256, migration_sha256 and the receipt flags. The fixture state can change during testing.
-- Set the optional fixture IDs only after identifying the synthetic test records in the clone.
SET NOCOUNT ON;

DECLARE @singleProductId int = NULL;
DECLARE @warehouseInventoryId int = NULL;

IF COALESCE(HAS_PERMS_BY_NAME(DB_NAME(), 'DATABASE', 'VIEW DEFINITION'), 0) <> 1
    THROW 50000, 'The disposable clone account needs VIEW DEFINITION for a complete metadata fingerprint.', 1;

DECLARE @receiptTableId int = OBJECT_ID(N'BulkPriceStockReceipt', N'U');
DECLARE @hasReceiptIndex bit = 0;

IF EXISTS (
    SELECT 1
    FROM sys.indexes AS i
    JOIN sys.index_columns AS k ON k.object_id = i.object_id AND k.index_id = i.index_id
    JOIN sys.columns AS c ON c.object_id = k.object_id AND c.column_id = k.column_id
    WHERE i.object_id = @receiptTableId AND i.name = N'UX_BulkPriceStockReceipt_Operation'
        AND i.is_unique = 1 AND i.is_disabled = 0 AND i.is_hypothetical = 0
        AND i.has_filter = 0 AND i.ignore_dup_key = 0 AND k.key_ordinal > 0
    GROUP BY i.index_id
    HAVING COUNT(*) = 4 AND COUNT(DISTINCT CASE
        WHEN c.name IN (N'ActorId', N'StoreId', N'OperationId', N'Version') THEN c.name END) = 4
)
    SET @hasReceiptIndex = 1;

DECLARE @schema nvarchar(max) = (
    SELECT
        JSON_QUERY((
            SELECT s.name AS schema_name, t.name, t.temporal_type, t.is_memory_optimized
            FROM sys.tables AS t JOIN sys.schemas AS s ON s.schema_id = t.schema_id
            WHERE t.is_ms_shipped = 0
            ORDER BY s.name, t.name FOR JSON PATH
        )) AS [tables],
        JSON_QUERY((
            SELECT s.name AS schema_name, t.name AS table_name, c.column_id, c.name,
                TYPE_NAME(c.user_type_id) AS data_type, c.max_length, c.precision, c.scale,
                c.collation_name, c.is_nullable, c.is_identity, dc.definition AS default_definition,
                cc.definition AS computed_definition
            FROM sys.tables AS t JOIN sys.schemas AS s ON s.schema_id = t.schema_id
            JOIN sys.columns AS c ON c.object_id = t.object_id
            LEFT JOIN sys.default_constraints AS dc ON dc.object_id = c.default_object_id
            LEFT JOIN sys.computed_columns AS cc ON cc.object_id = c.object_id AND cc.column_id = c.column_id
            WHERE t.is_ms_shipped = 0
            ORDER BY s.name, t.name, c.column_id FOR JSON PATH, INCLUDE_NULL_VALUES
        )) AS [columns],
        JSON_QUERY((
            SELECT s.name AS schema_name, t.name AS table_name, i.name AS index_name, i.type_desc,
                i.is_unique, i.is_disabled, i.is_primary_key, i.is_unique_constraint, i.ignore_dup_key,
                i.filter_definition, k.index_column_id, c.name AS column_name, k.key_ordinal,
                k.is_descending_key, k.is_included_column
            FROM sys.tables AS t JOIN sys.schemas AS s ON s.schema_id = t.schema_id
            JOIN sys.indexes AS i ON i.object_id = t.object_id AND i.index_id > 0
            JOIN sys.index_columns AS k ON k.object_id = i.object_id AND k.index_id = i.index_id
            LEFT JOIN sys.columns AS c ON c.object_id = k.object_id AND c.column_id = k.column_id
            WHERE t.is_ms_shipped = 0
            ORDER BY s.name, t.name, i.name, k.index_column_id FOR JSON PATH, INCLUDE_NULL_VALUES
        )) AS [indexes],
        JSON_QUERY((
            SELECT s.name AS schema_name, t.name AS table_name, f.name, f.is_disabled, f.is_not_trusted,
                f.delete_referential_action, f.update_referential_action, k.constraint_column_id,
                pc.name AS parent_column, rs.name AS referenced_schema, rt.name AS referenced_table,
                rc.name AS referenced_column
            FROM sys.foreign_keys AS f JOIN sys.foreign_key_columns AS k ON k.constraint_object_id = f.object_id
            JOIN sys.tables AS t ON t.object_id = k.parent_object_id
            JOIN sys.schemas AS s ON s.schema_id = t.schema_id
            JOIN sys.columns AS pc ON pc.object_id = k.parent_object_id AND pc.column_id = k.parent_column_id
            JOIN sys.tables AS rt ON rt.object_id = k.referenced_object_id
            JOIN sys.schemas AS rs ON rs.schema_id = rt.schema_id
            JOIN sys.columns AS rc ON rc.object_id = k.referenced_object_id AND rc.column_id = k.referenced_column_id
            WHERE t.is_ms_shipped = 0
            ORDER BY s.name, t.name, f.name, k.constraint_column_id FOR JSON PATH
        )) AS [foreign_keys],
        JSON_QUERY((
            SELECT s.name AS schema_name, t.name AS table_name, k.name, k.is_disabled, k.is_not_trusted, k.definition
            FROM sys.check_constraints AS k JOIN sys.tables AS t ON t.object_id = k.parent_object_id
            JOIN sys.schemas AS s ON s.schema_id = t.schema_id
            WHERE t.is_ms_shipped = 0
            ORDER BY s.name, t.name, k.name FOR JSON PATH
        )) AS [checks],
        JSON_QUERY((
            SELECT s.name AS schema_name, t.name AS table_name, tr.name,
                tr.is_disabled, tr.is_instead_of_trigger, OBJECT_DEFINITION(tr.object_id) AS definition
            FROM sys.triggers AS tr JOIN sys.tables AS t ON t.object_id = tr.parent_id
            JOIN sys.schemas AS s ON s.schema_id = t.schema_id
            WHERE t.is_ms_shipped = 0 AND tr.parent_class = 1
            ORDER BY s.name, t.name, tr.name FOR JSON PATH, INCLUDE_NULL_VALUES
        )) AS [triggers]
    FOR JSON PATH, WITHOUT_ARRAY_WRAPPER
);

IF OBJECT_ID(N'MigrationVersionInfo', N'U') IS NULL
    THROW 50001, 'The nopCommerce migration ledger is missing from the disposable clone.', 1;

DECLARE @migrations nvarchar(max) = (
    SELECT [Version], [Description], [AppliedOn] FROM [MigrationVersionInfo]
    ORDER BY [Version], [Description], [AppliedOn] FOR JSON PATH, INCLUDE_NULL_VALUES
);

SELECT
    CONVERT(char(64), HASHBYTES('SHA2_256', @schema), 2) AS schema_sha256,
    CONVERT(char(64), HASHBYTES('SHA2_256', @migrations), 2) AS migration_sha256,
    CONVERT(bit, CASE WHEN @receiptTableId IS NULL THEN 0 ELSE 1 END) AS receipt_table_present,
    @hasReceiptIndex AS required_receipt_index_present,
    CONVERT(bit, CASE WHEN EXISTS (
        SELECT 1 FROM [MigrationVersionInfo] WHERE [Description] = N'API bulk price and stock receipts'
    ) THEN 1 ELSE 0 END) AS receipt_migration_marker_present;

IF @singleProductId IS NOT NULL
BEGIN
    SELECT @singleProductId AS single_product_id,
        CONVERT(bit, CASE WHEN p.Id IS NOT NULL AND p.Deleted = 0
            AND p.ManageInventoryMethodId = 1 AND p.UseMultipleWarehouses = 0
            THEN 1 ELSE 0 END) AS fixture_eligible,
        p.Price, p.StockQuantity, p.Published, p.LowStockActivityId, p.MinStockQuantity
    FROM (VALUES (@singleProductId)) AS requested(id)
    LEFT JOIN [Product] AS p ON p.Id = requested.id;
END;

IF @warehouseInventoryId IS NOT NULL
BEGIN
    SELECT @warehouseInventoryId AS warehouse_inventory_id,
        CONVERT(bit, CASE WHEN i.Id IS NOT NULL AND w.Id IS NOT NULL AND p.Deleted = 0
            AND p.ManageInventoryMethodId = 1 AND p.UseMultipleWarehouses = 1
            THEN 1 ELSE 0 END) AS fixture_eligible,
        i.ProductId, i.WarehouseId, i.StockQuantity, i.ReservedQuantity,
        p.Published, p.LowStockActivityId, p.MinStockQuantity
    FROM (VALUES (@warehouseInventoryId)) AS requested(id)
    LEFT JOIN [ProductWarehouseInventory] AS i ON i.Id = requested.id
    LEFT JOIN [Product] AS p ON p.Id = i.ProductId
    LEFT JOIN [Warehouse] AS w ON w.Id = i.WarehouseId;
END;
