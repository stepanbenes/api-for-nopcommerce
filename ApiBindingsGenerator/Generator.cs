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
				context.ReportDiagnostic(Diagnostic.Create(new DiagnosticDescriptor("1", "generator-error", ex.Message, "bad", DiagnosticSeverity.Error, isEnabledByDefault: true), location: null));
			}
		}

		private (string name, string code) GenerateSourceCodeFromTextFile(string filePath, string? fileContent)
		{
			string apiName = Path.GetFileNameWithoutExtension(Path.GetFileNameWithoutExtension(filePath));
			var sourceCode = new StringBuilder($@"#nullable enable
namespace {apiName}
{{
"); // TODO sanitize apiName
			string json = fileContent?.ToString() ?? "";
			var rootElement = JObject.Parse(json);
			foreach (var property in rootElement.Properties())
			{
				switch (property.Name.ToLowerInvariant())
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
								foreach (var schema in schemas.Properties())
								{
									if (schema.Value is JObject schemaObject &&
										schemaObject["type"] is { Type: JTokenType.String } typeToken &&
										typeToken.ToString() == "object" &&
										schemaObject["properties"] is JObject properties)
									{
										sourceCode.Append(GenerateRecordFromJsonSchema(schema.Name, properties.Properties()));
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
			sourceCode.Append(@"}");
			return (apiName, sourceCode.ToString());
		}

		private static string GenerateRecordFromJsonSchema(string schemaName, IEnumerable<JProperty> properties)
		{
			var sourceCode = new StringBuilder($@"{INDENT}record {schemaName}
{INDENT}{{
"); // TODO: sanitize schemaName

			foreach (var property in properties)
			{
				if (property.Value is JObject propertyObject && propertyObject["type"] is { Type: JTokenType.String } typeToken)
				{
					string? format = null;
					var formatToken = propertyObject["format"];
					if (formatToken is { Type: JTokenType.String })
						format = formatToken.ToString();
					bool? nullable = null;
					var nullableToken = propertyObject["nullable"];
					if (nullableToken is { Type: JTokenType.Boolean })
						nullable = (bool)nullableToken;
					//var itemsToken = propertyObject["items"];

					var typeDescriptor = new TypeDescriptor(
						type: typeToken.ToString(),
						format,
						nullable);
					string typeName = getPropertyCSharpType(typeDescriptor);
					sourceCode.AppendLine($"{INDENT}{INDENT}public {typeName} {property.Name} {{ get; set; }}");
				}
			}

			sourceCode.AppendLine($@"{INDENT}}}
");
			return sourceCode.ToString();

			static string getPropertyCSharpType(TypeDescriptor typeDescriptor)
			{
				string csTypeName = typeDescriptor.Type switch
				{
					"string" => "string",
					"integer" => "int",
					"boolean" => "bool",
					"object" => "object",
					"array" => "object[]",
					_ => "object"
				};
				if (typeDescriptor.Nullable ?? false)
					return csTypeName + "?";
				return csTypeName;
			}
		}
	}
}
