using Microsoft.OpenApi.Any;
using Microsoft.OpenApi.Models;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace Nop.Plugin.Api.Infrastructure;

internal sealed class NullableEnumSchemaFilter(ISerializerDataContractResolver contracts) : ISchemaFilter, IParameterFilter
{
    public void Apply(OpenApiSchema schema, SchemaFilterContext context)
    {
        if (schema.Properties.Count == 0)
            return;

        foreach (var member in contracts.GetDataContractForType(context.Type).ObjectProperties ?? [])
        {
            if (schema.Properties.TryGetValue(member.Name, out var propertySchema))
                AllowNull(member.MemberType, propertySchema, context.SchemaRepository);
        }
    }

    public void Apply(OpenApiParameter parameter, ParameterFilterContext context)
    {
        var metadata = context.ApiParameterDescription.ModelMetadata;
        var propertyType = context.PropertyInfo?.PropertyType ??
            metadata?.ContainerType?.GetProperty(metadata.PropertyName ?? parameter.Name)?.PropertyType;
        var type = propertyType ?? context.ParameterInfo?.ParameterType ?? metadata?.ModelType;
        AllowNull(type, parameter.Schema, context.SchemaRepository);
    }

    private static void AllowNull(Type type, OpenApiSchema schema, SchemaRepository repository)
    {
        if (type is null || Nullable.GetUnderlyingType(type)?.IsEnum != true ||
            schema?.Reference is not { Id: { } enumId } reference ||
            !repository.Schemas.TryGetValue(enumId, out var enumSchema))
            return;

        // OpenAPI 3.0 ignores siblings of a reference. This separate branch accepts only null.
        schema.Reference = null;
        schema.AnyOf.Add(new OpenApiSchema { Reference = reference });
        schema.AnyOf.Add(new OpenApiSchema { Type = enumSchema.Type, Nullable = true, Enum = [new OpenApiNull()] });
    }
}
