using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

namespace ApiBindingsGenerator
{
	[Generator]
	public class Generator : ISourceGenerator
	{
		private const string INDENT = "    ";
		private const string TYPE_ACCESS_MODIFIER = "internal ";
		private const string PROPERTY_ACCESS_MODIFIER = "public ";

		public void Initialize(GeneratorInitializationContext context)
		{
		}

		public void Execute(GeneratorExecutionContext context)
		{
			try
			{
				foreach (var text in context.AdditionalFiles.Where(text => text.Path.EndsWith("swagger.json", ignoreCase: true, CultureInfo.InvariantCulture)))
				{
					(var name, var code) = GenerateSourceCodeFromTextFile(text.Path, text.GetText(context.CancellationToken)?.ToString());
					context.AddSource(name, SourceText.From(code, Encoding.UTF8));
				}
			}
			catch (Exception ex)
			{
				context.ReportDiagnostic(Diagnostic.Create(new DiagnosticDescriptor("SG_API_001", "generator-error", ex.Message, "bad", DiagnosticSeverity.Error, isEnabledByDefault: true), location: null));
			}
		}

		private (string name, string code) GenerateSourceCodeFromTextFile(string filePath, string? fileContent)
		{
			string apiName = Path.GetFileNameWithoutExtension(Path.GetFileNameWithoutExtension(filePath));
			string json = fileContent?.ToString() ?? "";
			var rootElement = JObject.Parse(json);
			Dictionary<string, TypeDescriptor> schemaMap = new();
			foreach (var property in rootElement.Properties())
			{
				switch (property.Name)
				{
					case "openapi":
						// version of openapi standard
						break;
					case "info":
						// api title and version
						break;
					case "paths":
						// description of api endpoints
						break;
					case "components":
						// schemas of dto objects
						if (property.Value is JObject components)
						{
							if (components.Property("schemas") is { Value: JObject { HasValues: true } schemas })
							{
								foreach (var schemaProperty in schemas.Properties())
								{
									if (schemaProperty.Value is JObject schemaObject && ParseTypeDescriptor(schemaObject) is { } typeDescriptor)
									{
										schemaMap[schemaProperty.Name] = typeDescriptor;
									}
								}
							}
						}
						break;
					case "security":
						// list of authentication methods
						break;
				}
			}
			var sourceCode = new StringBuilder($@"#nullable enable
namespace {apiName.ToPascalCase()}.DTOs
{{
{INDENT}using System.Text.Json.Serialization;
");

			foreach (var (typeName, typeDescriptor) in schemaMap)
			{
				switch (typeDescriptor.Type)
				{
					case "object":
						sourceCode.AppendLine(GenerateRecord(typeName, typeDescriptor, INDENT));
						break;
					case "integer" when typeDescriptor.EnumValues is not null:
						sourceCode.AppendLine(GenerateEnum(typeName, typeDescriptor, INDENT));
						break;
					case "array":
						sourceCode.AppendLine(GenerateArray(typeName, typeDescriptor, INDENT));
						break;
					default:
						throw new FormatException($"Type '{typeDescriptor.Type}' is not supported as top level type. ({typeName})");
				}
			}

			sourceCode.Append(@"}");
			return (apiName.ToPascalCase(), sourceCode.ToString());
		}

		private static TypeDescriptor? ParseTypeDescriptor(JObject schema)
		{
			string? type = null;
			string? format = null;
			bool? nullable = null;
			TypeDescriptor? items = null;
			string? refType = null;
			Dictionary<string, TypeDescriptor>? properties = null;
			string[]? enumValues = null;

			var typeToken = schema["type"];
			if (typeToken is { Type: JTokenType.String })
				type = typeToken.ToString();
			var formatToken = schema["format"];
			if (formatToken is { Type: JTokenType.String })
				format = formatToken.ToString();
			var nullableToken = schema["nullable"];
			if (nullableToken is { Type: JTokenType.Boolean })
				nullable = (bool)nullableToken;
			var itemsToken = schema["items"];
			if (itemsToken is JObject itemsObject)
				items = ParseTypeDescriptor(itemsObject);
			var refToken = schema["$ref"];
			if (refToken is { Type: JTokenType.String })
				refType = refToken.ToString();
			var propertiesToken = schema["properties"];
			if (propertiesToken is JObject propertiesObject)
			{
				properties = new Dictionary<string, TypeDescriptor>();
				foreach (var jProperty in propertiesObject.Properties())
				{
					if (jProperty.Value is JObject propertyObject && ParseTypeDescriptor(propertyObject) is { } typeDescriptor)
					{
						properties[jProperty.Name] = typeDescriptor;
					}
				}
			}
			var enumToken = schema["enum"];
			if (enumToken is JArray jArray)
			{
				enumValues = jArray.Select(jToken => jToken.ToString()).ToArray();
			}

			return new TypeDescriptor(type, format, nullable, items, enumValues, refType, properties);
		}

		private static string GenerateEnum(string enumName, TypeDescriptor typeDescriptor, string indent)
		{
			if (typeDescriptor.Type != "integer" && typeDescriptor.Type != "string")
				throw new FormatException($"Cannot generate enum '{enumName}' with non-integer or non-string schema ({typeDescriptor.Type}).");
			if (typeDescriptor.EnumValues is null)
				throw new FormatException($"Enum values must not be null ({enumName})");

			var source = new StringBuilder($@"{indent}{TYPE_ACCESS_MODIFIER}enum {enumName}
{indent}{{
");
			switch (typeDescriptor.Type)
			{
				case "integer":
					foreach (string value in typeDescriptor.EnumValues)
					{
						if (int.TryParse(value, out int number))
						{
							source.AppendLine($"{indent}{INDENT}{value.ToPascalCase()} = {number},");
						}
						else
						{
							throw new FormatException($"Cannot generate enum '{enumName}' with non-integer option ({value}).");
						}
					}
					break;
				case "string":
					{
						int index = 0;
						foreach (string value in typeDescriptor.EnumValues)
						{
							source.AppendLine($"{indent}{INDENT}{value.ToPascalCase()} = {index + 1},");
							index += 1;
						}
					}
					break;
			}
			source.AppendLine($"{indent}}}");
			return source.ToString();
		}

		private static string GenerateRecord(string recordName, TypeDescriptor typeDescriptor, string indent)
		{
			if (typeDescriptor.Type != "object")
				throw new FormatException($"Cannot generate record '{recordName}' with non-object schema ({typeDescriptor.Type}).");

			var sourceCode = new StringBuilder($@"{indent}{TYPE_ACCESS_MODIFIER}record {recordName}
{indent}{{
");


			foreach (var (propertyName, propertyType) in typeDescriptor.Properties.EmptyIfNull())
			{
				string typeName;
				bool isReferenceType;
				try
				{
					typeName = GetPropertyCSharpType(propertyType, out isReferenceType);
				}
				catch (FormatException ex)
				{
					throw new FormatException(ex.Message + $" ({propertyName})");
				}
				string initExpression = (isReferenceType && propertyType.Nullable is not true) ? " = default!;" : "";
				string propertyNamePascalCase = propertyName.ToPascalCase();
				sourceCode.AppendLine($"{indent}{INDENT}[JsonPropertyName(\"{propertyName}\")]");
				sourceCode.AppendLine($"{indent}{INDENT}{PROPERTY_ACCESS_MODIFIER}{typeName} {propertyNamePascalCase} {{ get; init; }}{initExpression}");
			}

			sourceCode.AppendLine($"{indent}}}");
			return sourceCode.ToString();

		}
		static string GetPropertyCSharpType(TypeDescriptor typeDescriptor, out bool isReferenceType)
		{
			if (typeDescriptor.RefType is not null)
			{
				isReferenceType = true; // TODO: I don't really know if it is reference type
				return typeDescriptor.RefType.Substring("#/components/schemas/".Length);
			}
			string csTypeName;
			(csTypeName, isReferenceType) = (typeDescriptor.Type, typeDescriptor.Format) switch
			{
				("string", "date") => ("System.DateTime", false),
				("string", "date-time") => ("System.DateTime", false),
				("string", "uuid") => ("System.Guid", false),
				("string", _) => ("string", true),
				("number", "float") => ("float", false),
				("number", "double") => ("double", false),
				("number", "int32") => ("int", false),
				("number", "int64") => ("long", false),
				("number", _) => ("double", false),
				("integer", "int32") => ("int", false),
				("integer", "int64") => ("long", false),
				("integer", _) => ("int", false),
				("boolean", _) => ("bool", false),
				("object", _) => ("object", true), // nested object definition is not supported
				("array", _) => (GetPropertyCSharpType(typeDescriptor.Items ?? throw new FormatException($"array type requires to items type to be defined"), out _) + "[]", true),
				_ => throw new FormatException($"Unrecognized type descriptor (type: {typeDescriptor.Type}, format: {typeDescriptor.Format})")
			};
			return (typeDescriptor.Nullable is true) ? csTypeName + "?" : csTypeName;
		}

		private static string GenerateArray(string arrayName, TypeDescriptor typeDescriptor, string indent)
		{
			if (typeDescriptor.Type != "array")
				throw new FormatException($"Cannot generate array '{arrayName}' with non-array schema ({typeDescriptor.Type}).");
			if (typeDescriptor.Items is null)
				throw new FormatException($"array type requires to items type to be defined ({arrayName})");

			var sourceCode = new StringBuilder($"{indent}{TYPE_ACCESS_MODIFIER}class {arrayName} : System.Collections.Generic.List<{GetPropertyCSharpType(typeDescriptor.Items, out _)}> {{ }}");
			sourceCode.AppendLine();
			return sourceCode.ToString();
		}
	}
}
