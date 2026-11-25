using System.Globalization;
using System.Text;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Moq;
using Nop.Core.Domain.Catalog;
using Nop.Core.Domain.Orders;
using Nop.Core.Domain.Media;
using Nop.Core.Infrastructure;
using Nop.Plugin.Api.Delta;
using Nop.Plugin.Api.DTO.Orders;
using Nop.Plugin.Api.Helpers;
using Nop.Plugin.Api.Maps;
using Nop.Plugin.Api.Services;
using Nop.Plugin.Api.Validators;
using Nop.Services.Attributes;
using Nop.Services.Media;
using Nop.Services.Stores;
using NUnit.Framework;

namespace Nop.Plugin.Api.Tests.ServicesTests.Orders;

[TestFixture]
public class CheckoutAttributeSelectionsTests
{
    [Test]
    [NonParallelizable]
    public void DeltaRetainsAllCheckboxChoices()
    {
        var engine = new Mock<IEngine>();
        engine.Setup(instance => instance.Resolve<IJsonPropertyMapper>(null)).Returns(new JsonPropertyMapper());
        var previous = EngineContext.Current;
        EngineContext.Replace(engine.Object);
        try
        {
            using var stream = new MemoryStream(Encoding.UTF8.GetBytes("""
                {"order":{"checkout_attributes":[{"id":12,"values":["13","14"]}]}}
                """));
            var helper = new JsonHelper(null, null);
            var request = helper.GetRequestJsonDictionaryFromStream(stream, true);
            var delta = new Delta<OrderDto>((Dictionary<string, object>)request["order"]);

            var selection = delta.Dto.CheckoutAttributes.Single();
            Assert.That(selection.AttributeId, Is.EqualTo(12));
            Assert.That(selection.Values, Is.EqualTo(new[] { "13", "14" }));
        }
        finally
        {
            EngineContext.Replace(previous);
        }
    }

    [TestCase("\"13\"")]
    [TestCase("{\"first\":\"13\"}")]
    [TestCase("[null]")]
    public void InvalidCheckboxValueShapesFailDeltaValidation(string valuesJson)
    {
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes($$$"""
            {"order":{"checkout_attributes":[{"id":12,"values":{{{valuesJson}}}}]}}
            """));
        var request = new JsonHelper(null, null).GetRequestJsonDictionaryFromStream(stream, true);
        var validator = new TypeValidator<OrderDto>();

        Assert.That(validator.IsValid((Dictionary<string, object>)request["order"]), Is.False);
    }

    [TestCase("3", false)]
    [TestCase("4", true)]
    public async Task ConditionUsesFinalSubmittedChoices(string parentChoice, bool expectedValid)
    {
        var attributeService = new Mock<IAttributeService<CheckoutAttribute, CheckoutAttributeValue>>();
        var parser = new AttributeParser<CheckoutAttribute, CheckoutAttributeValue>(attributeService.Object, null);
        var parent = new CheckoutAttribute { Id = 2, AttributeControlType = AttributeControlType.DropdownList };
        var conditional = new CheckoutAttribute
        {
            Id = 12,
            AttributeControlType = AttributeControlType.Checkboxes,
            ConditionAttributeXml = parser.AddAttribute(string.Empty, parent, "4")
        };
        attributeService.Setup(service => service.GetAllAttributesAsync()).ReturnsAsync(new[] { parent, conditional });
        var condition = CheckoutAttributeSelections.DescribeCondition(conditional, parser);
        Assert.That(condition.AttributeId, Is.EqualTo(parent.Id));
        Assert.That(condition.Values, Is.EqualTo(new[] { "4" }));
        attributeService.Setup(service => service.GetAttributeByIdAsync(parent.Id)).ReturnsAsync(parent);
        attributeService.Setup(service => service.GetAttributeByIdAsync(conditional.Id)).ReturnsAsync(conditional);
        attributeService.Setup(service => service.GetAttributeValuesAsync(parent.Id))
            .ReturnsAsync(new[] { new CheckoutAttributeValue { Id = 3 }, new CheckoutAttributeValue { Id = 4 } });
        attributeService.Setup(service => service.GetAttributeValuesAsync(conditional.Id))
            .ReturnsAsync(new[] { new CheckoutAttributeValue { Id = 13 }, new CheckoutAttributeValue { Id = 14 } });

        var storeMappingService = new Mock<IStoreMappingService>();
        storeMappingService.Setup(service => service.AuthorizeAsync(It.IsAny<CheckoutAttribute>(), 1)).ReturnsAsync(true);
        var modelState = new ModelStateDictionary();
        var selections = new CheckoutAttributeSelectionDto[]
        {
            new() { AttributeId = conditional.Id, Values = new() { "13", "14" } },
            new() { AttributeId = parent.Id, Values = new() { parentChoice } }
        };

        var xml = await CheckoutAttributeSelections.BuildAsync(selections, 1, true, attributeService.Object,
            parser, storeMappingService.Object, null, modelState);

        Assert.That(modelState.IsValid, Is.EqualTo(expectedValid));
        if (expectedValid)
            Assert.That(parser.ParseValues(xml, conditional.Id), Is.EqualTo(new[] { "13", "14" }));
        else
            Assert.That(modelState["checkout_attributes"].Errors.Single().ErrorMessage, Does.Contain("does not apply"));
    }

    [Test]
    public async Task DateFileAndReadonlySelectionsUseNativeValues()
    {
        var attributeService = new Mock<IAttributeService<CheckoutAttribute, CheckoutAttributeValue>>();
        var parser = new AttributeParser<CheckoutAttribute, CheckoutAttributeValue>(attributeService.Object, null);
        var date = new CheckoutAttribute { Id = 10, AttributeControlType = AttributeControlType.Datepicker };
        var file = new CheckoutAttribute
        {
            Id = 20,
            AttributeControlType = AttributeControlType.FileUpload,
            ValidationFileAllowedExtensions = "pdf",
            ValidationFileMaximumSize = 1
        };
        var readOnly = new CheckoutAttribute { Id = 30, AttributeControlType = AttributeControlType.ReadonlyCheckboxes };
        attributeService.Setup(service => service.GetAllAttributesAsync()).ReturnsAsync(new[] { date, file, readOnly });
        attributeService.Setup(service => service.GetAttributeByIdAsync(date.Id)).ReturnsAsync(date);
        attributeService.Setup(service => service.GetAttributeByIdAsync(file.Id)).ReturnsAsync(file);
        attributeService.Setup(service => service.GetAttributeByIdAsync(readOnly.Id)).ReturnsAsync(readOnly);
        attributeService.Setup(service => service.GetAttributeValuesAsync(readOnly.Id)).ReturnsAsync(new[]
        {
            new CheckoutAttributeValue { Id = 31, IsPreSelected = true },
            new CheckoutAttributeValue { Id = 32 }
        });
        var uploadGuid = Guid.NewGuid();
        var downloadService = new Mock<IDownloadService>();
        downloadService.Setup(service => service.GetDownloadByGuidAsync(uploadGuid)).ReturnsAsync(new Download
        {
            DownloadGuid = uploadGuid,
            IsNew = true,
            DownloadBinary = new byte[] { 1 },
            Extension = ".pdf"
        });
        var storeMappingService = new Mock<IStoreMappingService>();
        storeMappingService.Setup(service => service.AuthorizeAsync(It.IsAny<CheckoutAttribute>(), 1)).ReturnsAsync(true);
        var selections = new CheckoutAttributeSelectionDto[]
        {
            new() { AttributeId = date.Id, Values = new() { "2026-11-25" } },
            new() { AttributeId = file.Id, Values = new() { uploadGuid.ToString("N") } }
        };
        var modelState = new ModelStateDictionary();
        var xml = await CheckoutAttributeSelections.BuildAsync(selections, 1, false, attributeService.Object,
            parser, storeMappingService.Object, downloadService.Object, modelState);

        Assert.That(modelState.IsValid, Is.True);
        Assert.That(parser.ParseValues(xml, date.Id), Is.EqualTo(new[] { new DateOnly(2026, 11, 25).ToString("D", CultureInfo.CurrentCulture) }));
        Assert.That(parser.ParseValues(xml, file.Id), Is.EqualTo(new[] { uploadGuid.ToString() }));
        Assert.That(parser.ParseValues(xml, readOnly.Id), Is.EqualTo(new[] { "31" }));

        var invalidSelections = new CheckoutAttributeSelectionDto[]
        {
            new() { AttributeId = date.Id, Values = new() { "11/25/2026" } },
            new() { AttributeId = file.Id, Values = new() { Guid.NewGuid().ToString() } },
            new() { AttributeId = readOnly.Id, Values = new() { "32" } }
        };
        var invalidModel = new ModelStateDictionary();
        await CheckoutAttributeSelections.BuildAsync(invalidSelections, 1, false, attributeService.Object,
            parser, storeMappingService.Object, downloadService.Object, invalidModel);
        Assert.That(invalidModel["checkout_attributes"].Errors, Has.Count.EqualTo(3));
    }

    [Test]
    public async Task SavedSelectionsAreRevalidatedAndUseRefreshedReadonlyPrerequisites()
    {
        var attributeService = new Mock<IAttributeService<CheckoutAttribute, CheckoutAttributeValue>>();
        var parser = new AttributeParser<CheckoutAttribute, CheckoutAttributeValue>(attributeService.Object, null);
        var paidShippingOption = new CheckoutAttribute
        {
            Id = 1,
            AttributeControlType = AttributeControlType.DropdownList,
            ShippableProductRequired = true
        };
        var otherStoreOption = new CheckoutAttribute { Id = 2, AttributeControlType = AttributeControlType.DropdownList };
        var readOnly = new CheckoutAttribute { Id = 3, AttributeControlType = AttributeControlType.ReadonlyCheckboxes };
        var dependent = new CheckoutAttribute
        {
            Id = 4,
            AttributeControlType = AttributeControlType.Checkboxes,
            ConditionAttributeXml = parser.AddAttribute(string.Empty, readOnly, "32")
        };
        var inapplicable = new CheckoutAttribute
        {
            Id = 5,
            AttributeControlType = AttributeControlType.DropdownList,
            ConditionAttributeXml = parser.AddAttribute(string.Empty, readOnly, "31")
        };
        var attributes = new[] { paidShippingOption, otherStoreOption, dependent, inapplicable, readOnly };
        attributeService.Setup(service => service.GetAllAttributesAsync()).ReturnsAsync(attributes);
        foreach (var attribute in attributes)
            attributeService.Setup(service => service.GetAttributeByIdAsync(attribute.Id)).ReturnsAsync(attribute);
        attributeService.Setup(service => service.GetAttributeValuesAsync(readOnly.Id)).ReturnsAsync(new[]
        {
            new CheckoutAttributeValue { Id = 31 },
            new CheckoutAttributeValue { Id = 32, IsPreSelected = true }
        });
        attributeService.Setup(service => service.GetAttributeValuesAsync(dependent.Id))
            .ReturnsAsync(new[] { new CheckoutAttributeValue { Id = 41 } });
        attributeService.Setup(service => service.GetAttributeValuesAsync(inapplicable.Id))
            .ReturnsAsync(new[] { new CheckoutAttributeValue { Id = 51 } });
        var mappingService = new Mock<IStoreMappingService>();
        mappingService.Setup(service => service.AuthorizeAsync(It.IsAny<CheckoutAttribute>(), 1))
            .ReturnsAsync((CheckoutAttribute attribute, int _) => attribute.Id != otherStoreOption.Id);
        var savedXml = parser.AddAttribute(string.Empty, paidShippingOption, "11");
        savedXml = parser.AddAttribute(savedXml, otherStoreOption, "21");
        savedXml = parser.AddAttribute(savedXml, readOnly, "31");
        savedXml = parser.AddAttribute(savedXml, dependent, "41");
        savedXml = parser.AddAttribute(savedXml, dependent, "42");
        savedXml = parser.AddAttribute(savedXml, inapplicable, "51");
        var state = new ModelStateDictionary();

        var resolvedXml = await CheckoutAttributeSelections.BuildAsync([], 1, false, attributeService.Object,
            parser, mappingService.Object, null, state, savedXml);

        Assert.That(state.IsValid, Is.True);
        Assert.That(parser.ParseAttributeIds(resolvedXml), Is.EquivalentTo(new[] { readOnly.Id, dependent.Id }));
        Assert.That(parser.ParseValues(resolvedXml, readOnly.Id), Is.EqualTo(new[] { "32" }));
        Assert.That(parser.ParseValues(resolvedXml, dependent.Id), Is.EqualTo(new[] { "41" }));
    }
}
