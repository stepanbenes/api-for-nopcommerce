using Microsoft.AspNetCore.Mvc.ModelBinding;
using Nop.Core.Domain.Catalog;
using Nop.Core.Domain.Orders;
using Nop.Plugin.Api.DTO.Orders;
using Nop.Services.Attributes;
using Nop.Services.Media;
using Nop.Services.Stores;
using System.Globalization;

namespace Nop.Plugin.Api.Services
{
    internal static class CheckoutAttributeSelections
    {
        public static async Task<string> BuildAsync(
            IList<CheckoutAttributeSelectionDto> selections, int storeId, bool shippingRequired,
            IAttributeService<CheckoutAttribute, CheckoutAttributeValue> attributeService,
            IAttributeParser<CheckoutAttribute, CheckoutAttributeValue> attributeParser,
            IStoreMappingService storeMappingService, IDownloadService downloadService, ModelStateDictionary modelState,
            string existingXml = null)
        {
            var availableAttributes = await (await attributeService.GetAllAttributesAsync())
                .WhereAwait(async attribute => (!attribute.ShippableProductRequired || shippingRequired) &&
                    await storeMappingService.AuthorizeAsync(attribute, storeId))
                .ToListAsync();
            var attributesById = availableAttributes.ToDictionary(attribute => attribute.Id);
            var submittedIds = new HashSet<int>();
            var choicesByAttribute = new Dictionary<int, List<string>>();
            foreach (var selection in selections)
            {
                if (!submittedIds.Add(selection.AttributeId))
                {
                    modelState.AddModelError("checkout_attributes", $"Send one selection per attribute ({selection.AttributeId}).");
                    continue;
                }

                if (!attributesById.TryGetValue(selection.AttributeId, out var attribute))
                {
                    modelState.AddModelError("checkout_attributes", $"Checkout attribute {selection.AttributeId} is unavailable.");
                    continue;
                }

                var values = selection.Values?.Select(value => value?.Trim()).Distinct().ToList();
                if (values == null || values.Count == 0 || values.Any(string.IsNullOrEmpty))
                {
                    modelState.AddModelError("checkout_attributes", $"Provide a value for checkout attribute {attribute.Id}.");
                    continue;
                }

                if (!await NormalizeValuesAsync(attribute, values, attributeService, downloadService, isSaved: false))
                {
                    modelState.AddModelError("checkout_attributes", $"Invalid or unsupported selection for checkout attribute {attribute.Id}.");
                    continue;
                }

                choicesByAttribute.Add(attribute.Id, values);
            }

            foreach (var attribute in availableAttributes)
            {
                if (attribute.AttributeControlType == AttributeControlType.ReadonlyCheckboxes)
                {
                    choicesByAttribute[attribute.Id] = (await attributeService.GetAttributeValuesAsync(attribute.Id))
                        .Where(choice => choice.IsPreSelected)
                        .Select(choice => choice.Id.ToString(CultureInfo.InvariantCulture))
                        .ToList();
                    continue;
                }

                if (string.IsNullOrEmpty(existingXml) || submittedIds.Contains(attribute.Id))
                    continue;

                var values = attributeParser.ParseValues(existingXml, attribute.Id)
                    .Where(value => !string.IsNullOrWhiteSpace(value)).Distinct().ToList();
                if (values.Count > 0 &&
                    await NormalizeValuesAsync(attribute, values, attributeService, downloadService, isSaved: true))
                    choicesByAttribute.Add(attribute.Id, values);
            }

            // Resolve prerequisites first so stale selections never activate dependents.
            var attributesXml = string.Empty;
            var pending = choicesByAttribute.Keys.ToDictionary(id => id,
                id => DescribeCondition(attributesById[id], attributeParser)?.AttributeId);
            while (pending.Count > 0)
            {
                var ready = pending
                    .Where(item => item.Value == null || !pending.ContainsKey(item.Value.Value))
                    .Select(item => item.Key).ToList();

                if (ready.Count == 0)
                {
                    foreach (var id in pending.Keys.Where(submittedIds.Contains))
                        modelState.AddModelError("checkout_attributes", $"Checkout attribute {id} depends on a circular condition.");
                    break;
                }

                foreach (var id in ready)
                {
                    var attribute = attributesById[id];
                    if (await attributeParser.IsConditionMetAsync(attribute.ConditionAttributeXml, attributesXml) != false)
                    {
                        foreach (var value in choicesByAttribute[id])
                            attributesXml = attributeParser.AddAttribute(attributesXml, attribute, value);
                    }
                    else if (submittedIds.Contains(id))
                    {
                        modelState.AddModelError("checkout_attributes", $"Checkout attribute {id} does not apply to these selections.");
                    }

                    pending.Remove(id);
                }
            }

            return attributesXml;
        }

        public static CheckoutAttributeConditionDto DescribeCondition(CheckoutAttribute attribute,
            IAttributeParser<CheckoutAttribute, CheckoutAttributeValue> parser)
        {
            var id = parser.ParseAttributeIds(attribute.ConditionAttributeXml).FirstOrDefault();
            return id <= 0 ? null : new CheckoutAttributeConditionDto
            {
                AttributeId = id,
                Values = parser.ParseValues(attribute.ConditionAttributeXml, id)
                    .Where(value => !string.IsNullOrEmpty(value)).ToList()
            };
        }

        private static async Task<bool> NormalizeValuesAsync(
            CheckoutAttribute attribute, List<string> values,
            IAttributeService<CheckoutAttribute, CheckoutAttributeValue> attributeService,
            IDownloadService downloadService, bool isSaved)
        {
            if (attribute.AttributeControlType is AttributeControlType.TextBox or AttributeControlType.MultilineTextbox)
                return values.Count == 1;

            if (attribute.AttributeControlType == AttributeControlType.Datepicker)
            {
                if (values.Count != 1)
                    return false;
                if (isSaved)
                    return DateOnly.TryParse(values[0], CultureInfo.CurrentCulture, DateTimeStyles.None, out _);
                if (!DateOnly.TryParseExact(values[0], "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
                    return false;

                values[0] = date.ToString("D", CultureInfo.CurrentCulture);
                return true;
            }

            if (attribute.AttributeControlType == AttributeControlType.FileUpload)
            {
                if (values.Count != 1 || !Guid.TryParse(values[0], out var guid))
                    return false;

                var download = await downloadService.GetDownloadByGuidAsync(guid);
                if (download is not { IsNew: true, UseDownloadUrl: false, DownloadBinary: not null } ||
                    (attribute.ValidationFileMaximumSize.HasValue &&
                     download.DownloadBinary.LongLength > (long)attribute.ValidationFileMaximumSize.Value * 1024))
                    return false;

                if (!string.IsNullOrWhiteSpace(attribute.ValidationFileAllowedExtensions) &&
                    !attribute.ValidationFileAllowedExtensions.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                        .Any(extension => string.Equals(extension.TrimStart('.'), download.Extension?.TrimStart('.'), StringComparison.OrdinalIgnoreCase)))
                    return false;

                values[0] = download.DownloadGuid.ToString();
                return true;
            }

            if (attribute.AttributeControlType is not (AttributeControlType.DropdownList or AttributeControlType.RadioList or
                AttributeControlType.ColorSquares or AttributeControlType.ImageSquares or AttributeControlType.Checkboxes))
                return false;

            var choices = await attributeService.GetAttributeValuesAsync(attribute.Id);
            var choiceIds = choices.Select(choice => choice.Id.ToString(CultureInfo.InvariantCulture)).ToHashSet();
            if (isSaved)
                values.RemoveAll(value => !choiceIds.Contains(value));

            return values.Count > 0 && (values.Count == 1 || attribute.AttributeControlType == AttributeControlType.Checkboxes) &&
                values.All(choiceIds.Contains);
        }
    }
}
