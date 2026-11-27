using System.Globalization;
using System.Security.Cryptography;
using System.Transactions;
using LinqToDB.Async;
using Newtonsoft.Json;
using Nop.Core;
using Nop.Core.Domain.Catalog;
using Nop.Core.Domain.Common;
using Nop.Core.Domain.Configuration;
using Nop.Core.Domain.Customers;
using Nop.Core.Domain.Media;
using Nop.Core.Domain.Vendors;
using Nop.Core.Infrastructure;
using Nop.Data;
using Nop.Plugin.Api.DTO.Products;
using Nop.Services.Catalog;
using Nop.Services.Common;
using Nop.Services.Media;
using Nop.Services.Security;
using Nop.Services.Stores;
using SkiaSharp;

namespace Nop.Plugin.Api.Services;

public sealed class ProductPictureSyncService(
    INopDataProvider dataProvider,
    IRepository<Product> products,
    IRepository<ProductPicture> mappings,
    IRepository<GenericAttribute> attributes,
    IGenericAttributeService genericAttributeService,
    IProductService productService,
    IPictureService pictureService,
    IThumbService thumbService,
    MediaSettings mediaSettings,
    INopFileProvider fileProvider,
    IStoreMappingService storeMappingService,
    IAclService aclService)
{
    private const string AttributePrefix = "Api.CatalogSync.Picture.";
    private const string RefreshPrefix = "Api.CatalogSync.PictureRefresh.";
    private const int MaxImageBytes = 4 * 1024 * 1024;
    private static readonly SemaphoreSlim[] WriteLocks = Enumerable.Range(0, 64)
        .Select(_ => new SemaphoreSlim(1, 1)).ToArray();

    public async Task<ProductPictureSyncResult> ExecuteAsync(Customer actor, int storeId, int productId, string key,
        ProductPictureSyncRequest request, CancellationToken cancellationToken,
        int? mappingId = null, int? pictureId = null)
    {
        var writeLock = request == null ? null : WriteLocks[productId % WriteLocks.Length];
        if (writeLock != null)
            await writeLock.WaitAsync(cancellationToken);

        try
        {
            Picture refresh = null;
            GenericAttribute refreshMarker = null;
            int[] sizes = [];
            ProductPictureSyncResult result;
            using (var transaction = request == null ? null : dataProvider.CreateTransactionScope())
            {
                var query = products.Table.Where(product => product.Id == productId && !product.Deleted);
                if (actor.VendorId != 0)
                    query = query.Where(product => product.VendorId == actor.VendorId);
                query = await storeMappingService.ApplyStoreMapping(query, storeId);
                query = await aclService.ApplyAcl(query, actor);
                var product = await query.FirstOrDefaultAsync(cancellationToken);
                if (product == null)
                    return new("not_found");

                var attributeKey = AttributePrefix + key;
                if (key != null)
                {
                    var productKeys = attributes.Table.Where(a =>
                        a.KeyGroup == nameof(Product) && a.EntityId == productId && a.StoreId == 0 &&
                        a.Key.StartsWith(AttributePrefix));
                    var ownership = await productKeys.Where(a => a.Key == attributeKey).ToListAsync(cancellationToken);
                    if (ownership.Count > 1)
                        return new("ambiguous");
                    if (ownership.Count == 1)
                    {
                        var entry = ownership[0];
                        var ownedIds = entry.Value.Split(':');
                        if (ownedIds.Length != 2 ||
                            !int.TryParse(ownedIds[0], NumberStyles.None, CultureInfo.InvariantCulture, out var ownedMappingId) ||
                            !int.TryParse(ownedIds[1], NumberStyles.None, CultureInfo.InvariantCulture, out var ownedPictureId) ||
                            await productKeys.AnyAsync(a => a.Id != entry.Id && a.Value == entry.Value, cancellationToken))
                            return new("ambiguous");
                        mappingId = ownedMappingId;
                        pictureId = ownedPictureId;
                    }
                }

                var current = new ProductPictureSyncResult("missing");
                ProductPicture mapping = null;
                Picture picture = null;
                if (mappingId.HasValue)
                {
                    mapping = await mappings.Table.FirstOrDefaultAsync(
                        m => m.Id == mappingId && m.PictureId == pictureId && m.ProductId == productId, cancellationToken);
                    if (mapping == null)
                        return new(key == null ? "not_found" : "ambiguous");
                    if (await mappings.Table.AnyAsync(m => m.PictureId == mapping.PictureId && m.Id != mapping.Id,
                        cancellationToken))
                        return new("ambiguous");

                    picture = await pictureService.GetPictureByIdAsync(mapping.PictureId);
                    if (picture == null)
                        return new("ambiguous");

                    var storedBytes = await pictureService.LoadPictureBinaryAsync(picture);
                    if (storedBytes.Length == 0)
                        return new("ambiguous");
                    current = new("present", mapping.Id, picture.Id, Digest(storedBytes));

                    if (request != null)
                    {
                        var refreshKey = RefreshPrefix + picture.Id.ToString(CultureInfo.InvariantCulture);
                        var pending = await attributes.Table.Where(a => a.KeyGroup == nameof(Product) &&
                            a.EntityId == productId && a.StoreId == 0 && a.Key == refreshKey).ToListAsync(cancellationToken);
                        if (pending.Count > 1)
                            return new("ambiguous");
                        refreshMarker = pending.SingleOrDefault();
                        if (refreshMarker != null)
                            sizes = JsonConvert.DeserializeObject<int[]>(refreshMarker.Value) ?? [];
                    }
                }

                if (request == null)
                    return current;
                if (current.Sha256 == request.Sha256 && refreshMarker == null)
                    return current with { Status = "unchanged" };
                if (current.Sha256 != request.Sha256 && current.Sha256 != request.ExpectedSha256)
                    return current with { Status = "conflict" };

                if (Environment.GetEnvironmentVariable("NOP_API_PICTURE_SYNC_LOCAL_THUMBS") != "1" ||
                    !BulkPriceStockService.SupportsTransactionalWrites() || thumbService.GetType() != typeof(ThumbService) ||
                    !await pictureService.IsStoreInDbAsync())
                    return new("unsupported_storage");
                if (picture != null && await IsUsedOutsideGalleryAsync(picture.Id, cancellationToken))
                    return new("ambiguous");

                if (current.Sha256 == request.Sha256)
                {
                    refresh = picture;
                    result = current with { Status = "unchanged" };
                }
                else
                {
                    var seoFilename = picture == null ? await pictureService.GetPictureSeNameAsync(product.Name) : picture.SeoFilename;
                    var binary = await ReadImageAsync(request, seoFilename, picture?.MimeType);
                    if (binary == null)
                        return new("invalid_image");

                    if (picture == null)
                    {
                        var lastOrder = await mappings.Table.Where(m => m.ProductId == productId)
                            .MaxAsync(m => (int?)m.DisplayOrder, cancellationToken) ?? 0;
                        picture = await pictureService.InsertPictureAsync(binary, request.MimeType, seoFilename,
                            isNew: false, validateBinary: false);
                        mapping = new ProductPicture
                        {
                            ProductId = productId,
                            PictureId = picture.Id,
                            DisplayOrder = lastOrder == int.MaxValue ? lastOrder : lastOrder + 1
                        };
                        await productService.InsertProductPictureAsync(mapping);
                        await genericAttributeService.SaveAttributeAsync(product, attributeKey,
                            FormattableString.Invariant($"{mapping.Id}:{picture.Id}"));
                        result = new("added", mapping.Id, picture.Id, Digest(await pictureService.LoadPictureBinaryAsync(picture)));
                    }
                    else
                    {
                        sizes = sizes.Concat(await GetWarmSizesAsync(picture)).Distinct().ToArray();
                        var refreshKey = RefreshPrefix + picture.Id.ToString(CultureInfo.InvariantCulture);
                        await genericAttributeService.SaveAttributeAsync(product, refreshKey, JsonConvert.SerializeObject(sizes));
                        refreshMarker = await attributes.Table.SingleAsync(a => a.KeyGroup == nameof(Product) &&
                            a.EntityId == productId && a.StoreId == 0 && a.Key == refreshKey, cancellationToken);

                        refresh = await pictureService.UpdatePictureAsync(picture.Id, binary, picture.MimeType,
                            seoFilename, picture.AltAttribute, picture.TitleAttribute, isNew: false, validateBinary: false);
                        if (refresh == null)
                            return new("ambiguous");
                        result = new("replaced", mapping.Id, refresh.Id, Digest(await pictureService.LoadPictureBinaryAsync(refresh)));
                    }

                    if (request.Sha256 != result.Sha256)
                        throw new InvalidOperationException("nopCommerce stored different picture bytes than were validated.");
                }

                transaction.Complete();
            }

            if (refreshMarker != null)
            {
                try
                {
                    var refreshedSizes = sizes.Concat(await GetWarmSizesAsync(refresh)).Distinct().ToArray();
                    if (refreshedSizes.Length != sizes.Length)
                    {
                        refreshMarker.Value = JsonConvert.SerializeObject(refreshedSizes);
                        await genericAttributeService.UpdateAttributeAsync(refreshMarker);
                    }
                    await RefreshThumbsAsync(refresh, refreshedSizes);
                    await genericAttributeService.DeleteAttributeAsync(refreshMarker);
                }
                catch (Exception exception) when (exception.GetBaseException() is IOException or UnauthorizedAccessException)
                {
                    return result with { Status = "thumbnail_pending" };
                }
            }

            return result;
        }
        catch (Exception exception) when (exception is TransactionAbortedException ||
            BulkPriceStockService.IsConcurrentWrite(exception))
        {
            return new("busy");
        }
        finally
        {
            writeLock?.Release();
        }
    }

    private async Task<bool> IsUsedOutsideGalleryAsync(int pictureId, CancellationToken cancellationToken)
    {
        var value = pictureId.ToString(CultureInfo.InvariantCulture);
        return await dataProvider.GetTable<Category>().AnyAsync(x => x.PictureId == pictureId, cancellationToken) ||
            await dataProvider.GetTable<Manufacturer>().AnyAsync(x => x.PictureId == pictureId, cancellationToken) ||
            await dataProvider.GetTable<Vendor>().AnyAsync(x => x.PictureId == pictureId, cancellationToken) ||
            await dataProvider.GetTable<ProductAttributeValue>().AnyAsync(x => x.ImageSquaresPictureId == pictureId, cancellationToken) ||
            await dataProvider.GetTable<ProductAttributeValuePicture>().AnyAsync(x => x.PictureId == pictureId, cancellationToken) ||
            await dataProvider.GetTable<ProductAttributeCombinationPicture>().AnyAsync(x => x.PictureId == pictureId, cancellationToken) ||
            await dataProvider.GetTable<Product3dObject>().AnyAsync(x => x.PreviewPictureId == pictureId, cancellationToken) ||
            await attributes.Table.AnyAsync(a => a.KeyGroup == nameof(Customer) &&
                a.Key == NopCustomerDefaults.AvatarPictureIdAttribute && a.Value == value, cancellationToken) ||
            await dataProvider.GetTable<Setting>().AnyAsync(x => x.Value == value &&
                (x.Name.ToLower() == "storeinformationsettings.logopictureid" ||
                x.Name.ToLower() == "pdfsettings.logopictureid" ||
                x.Name.ToLower() == "mediasettings.productdefaultimageid"), cancellationToken);
    }

    private static string GetThumbStem(Picture picture) =>
        string.IsNullOrEmpty(picture.SeoFilename)
            ? picture.Id.ToString("0000000", CultureInfo.InvariantCulture)
            : FormattableString.Invariant($"{picture.Id:0000000}_{picture.SeoFilename}");

    private async Task<int[]> GetWarmSizesAsync(Picture picture)
    {
        var id = picture.Id.ToString("0000000", CultureInfo.InvariantCulture);
        var stem = GetThumbStem(picture);
        var extension = await pictureService.GetFileExtensionFromMimeTypeAsync(picture.MimeType);
        var directory = Path.GetDirectoryName(await thumbService.GetThumbLocalPathByFileNameAsync($"{id}.{extension}"));
        if (!fileProvider.DirectoryExists(directory))
            return [];

        var sizes = new List<int>();
        foreach (var path in fileProvider.GetFiles(directory, $"{id}*.{extension}"))
        {
            var name = Path.GetFileNameWithoutExtension(path);
            if (name == stem)
                sizes.Add(0);
            else if (name.StartsWith(stem + "_", StringComparison.Ordinal) &&
                int.TryParse(name.AsSpan(stem.Length + 1), NumberStyles.None, CultureInfo.InvariantCulture, out var size) &&
                size > 0)
                sizes.Add(size);
        }

        return sizes.ToArray();
    }

    private async Task RefreshThumbsAsync(Picture picture, IEnumerable<int> sizes)
    {
        var stem = GetThumbStem(picture);
        var extension = await pictureService.GetFileExtensionFromMimeTypeAsync(picture.MimeType);
        foreach (var size in sizes)
        {
            var name = size == 0 ? $"{stem}.{extension}" : FormattableString.Invariant($"{stem}_{size}.{extension}");
            var path = await thumbService.GetThumbLocalPathByFileNameAsync(name);
            if (fileProvider.FileExists(path))
            {
                // Static file validators have one-second resolution, even when the new file has the same length.
                var previous = fileProvider.GetLastWriteTimeUtc(path);
                var nextSecond = previous.AddTicks(TimeSpan.TicksPerSecond - previous.Ticks % TimeSpan.TicksPerSecond);
                var delay = nextSecond - DateTime.UtcNow;
                if (delay > TimeSpan.Zero && delay <= TimeSpan.FromSeconds(1))
                    await Task.Delay(delay + TimeSpan.FromMilliseconds(2));
            }
            fileProvider.DeleteFile(path);
            await pictureService.GetPictureUrlAsync(picture, targetSize: size, showDefaultPicture: false);
            if (!fileProvider.FileExists(path))
                throw new IOException("nopCommerce could not regenerate a cached product picture thumbnail.");
        }
    }

    private async Task<byte[]> ReadImageAsync(ProductPictureSyncRequest request, string fileName, string existingMimeType)
    {
        var format = request.MimeType switch
        {
            "image/jpeg" => SKEncodedImageFormat.Jpeg,
            "image/png" => SKEncodedImageFormat.Png,
            _ => (SKEncodedImageFormat?)null
        };

        if (format == null || (existingMimeType != null && existingMimeType != request.MimeType) ||
            string.IsNullOrEmpty(request.Base64) || request.Base64.Length > ((MaxImageBytes + 2) / 3) * 4)
            return null;

        try
        {
            var bytes = Convert.FromBase64String(request.Base64);
            if (bytes.Length is 0 or > MaxImageBytes || Digest(bytes) != request.Sha256)
                return null;

            using var stream = new MemoryStream(bytes, writable: false);
            using var codec = SKCodec.Create(stream);
            if (codec == null || codec.EncodedFormat != format ||
                codec.Info.Width > mediaSettings.MaximumImageSize || codec.Info.Height > mediaSettings.MaximumImageSize)
                return null;

            var validated = await pictureService.ValidatePictureAsync(bytes, request.MimeType, fileName);
            return Digest(validated) == request.Sha256 ? validated : null;
        }
        catch (Exception exception) when (exception is FormatException or NopException)
        {
            return null;
        }
    }

    private static string Digest(byte[] bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes));
}
