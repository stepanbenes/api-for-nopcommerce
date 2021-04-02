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
using System.Net;
using System.Net.Http;
using System.Text;

namespace ApiBindingsGenerator
{
	[Generator]
	public class Generator : ISourceGenerator
	{
		private const string BASE_NAMESPACE = "ApiBindings";
		private const string ____ = "    ";
		private const string TYPE_ACCESS_MODIFIER = "internal ";
		private const string PROPERTY_ACCESS_MODIFIER = "public ";

		public void Initialize(GeneratorInitializationContext context)
		{
		}

		public void Execute(GeneratorExecutionContext context)
		{
			try
			{
				bool commonFilesGenerated = false;
				foreach (var text in context.AdditionalFiles.Where(text => text.Path.EndsWith("swagger.json", ignoreCase: true, CultureInfo.InvariantCulture)))
				{
					foreach (var (filename, code) in GenerateSourceCodeFromTextFile(text.Path, text.GetText(context.CancellationToken)?.ToString()))
					{
						if (!commonFilesGenerated)
						{
							foreach (var (commonFilename, commonFileCode) in GenerateCommonFiles())
								context.AddSource(commonFilename, commonFileCode);
							commonFilesGenerated = true;
						}
						context.AddSource(filename, SourceText.From(code, Encoding.UTF8));
					}
				}
			}
			catch (Exception ex)
			{
				context.ReportDiagnostic(Diagnostic.Create(new DiagnosticDescriptor("SG_API_001", "generator-error", ex.Message, "bad", DiagnosticSeverity.Error, isEnabledByDefault: true), location: null));
			}
		}

		private static IEnumerable<(string filename, string code)> GenerateCommonFiles()
		{
			string code = $"namespace {BASE_NAMESPACE}" + @"
{
	using System;
	using System.Net.Http;
	using System.Net.Http.Json;
	using System.Text.Json;
	using System.Threading.Tasks;

	" + $"{TYPE_ACCESS_MODIFIER}abstract class ApiClientBase" + @"
	{
		protected record Token(string AccessToken, string TokenType);

		private readonly HttpClient httpClient;
		protected Lazy<Task<Token?>> AccessToken { get; }

		public ApiClientBase(HttpClient httpClient)
		{
			this.httpClient = httpClient;
			this.AccessToken = new Lazy<Task<Token?>>(Authenticate);
		}

		protected abstract Task<Token?> Authenticate();

		protected async Task<T?> Send<T>(HttpMethod httpMethod, string requestEndpoint, bool authenticate = true, HttpContent? content = null, JsonSerializerOptions? responseDeserializerOptions = null) where T : class
		{
			var response = await Send(httpMethod, requestEndpoint, authenticate, content);
			if (!response.IsSuccessStatusCode)
			{
				return null;
			}
			return await response.Content.ReadFromJsonAsync<T>(responseDeserializerOptions);
		}

		protected async Task<HttpResponseMessage> Send(HttpMethod httpMethod, string requestEndpoint, bool authenticate = true, HttpContent? content = null)
		{
			var request = new HttpRequestMessage(httpMethod, requestEndpoint);
			if (authenticate && await AccessToken.Value is { } accessToken)
			{
				request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue(accessToken.TokenType, accessToken.AccessToken);
			}
			request.Content = content;
			return await httpClient.SendAsync(request);
		}
	}
}
";
			yield return (filename: "ApiClientBase", code);
		}

		private IEnumerable<(string filename, string code)> GenerateSourceCodeFromTextFile(string filePath, string? fileContent)
		{
			string fileName = Path.GetFileNameWithoutExtension(Path.GetFileNameWithoutExtension(filePath));
			string json = fileContent?.ToString() ?? "";
			string? openApiVersion = null;
			ApiInfo? apiInfo = null;
			var rootElement = JObject.Parse(json);
			Dictionary<string, TypeDescriptor> schema = new();
			Dictionary<string, SecuritySchemeDescriptor>? securitySchemes = null;
			List<ApiEndpoint> apiEndpoints = new();
			foreach (var property in rootElement.Properties())
			{
				switch (property.Name)
				{
					case "openapi":
						openApiVersion = property.Value.ToString();
						break;
					case "info":
						apiInfo = property.Value.ToObject<ApiInfo>();
						break;
					case "paths":
						// description of api endpoints
						if (property.Value is JObject paths)
						{
							foreach (var pathProperty in paths.Properties())
							{
								apiEndpoints.AddRange(ParseApiEndpoints(pathProperty));
							}
						}
						break;
					case "components":
						// schemas of dto objects
						if (property.Value is JObject components)
						{
							if (components.Property("schemas") is { Value: JObject { HasValues: true } schemasObject })
							{
								foreach (var schemaProperty in schemasObject.Properties())
								{
									if (schemaProperty.Value is JObject schemaObject && ParseTypeDescriptor(schemaObject) is { } typeDescriptor)
									{
										schema[schemaProperty.Name] = typeDescriptor;
									}
								}
							}
							if (components.Property("securitySchemes") is { Value: JObject securitySchemesObject })
							{
								securitySchemes = securitySchemesObject.ToObject<Dictionary<string, SecuritySchemeDescriptor>>();
							}
						}
						break;
					case "security":
						// list of authentication methods
						break;
				}
			}

			string apiName = apiInfo?.Title?.ToPascalCase() ?? fileName.ToPascalCase();

			yield return (filename: $"{apiName}.DTOs", GenerateDTOs(apiName, schema));
			yield return (filename: $"{apiName}.ApiClient", GenerateApiClientClass(apiName, apiEndpoints, securitySchemes));
		}

		private static string GenerateDTOs(string apiName, Dictionary<string, TypeDescriptor> schema)
		{
			var sourceCode = new StringBuilder($@"#nullable enable
namespace {BASE_NAMESPACE}.{apiName}
{{
{____}using System.Text.Json.Serialization;

");

			foreach (var (typeName, typeDescriptor) in schema)
			{
				switch (typeDescriptor.Type)
				{
					case "object":
						sourceCode.AppendLine(GenerateRecord(typeName, typeDescriptor, ____));
						break;
					case "integer" when typeDescriptor.EnumValues is not null:
						sourceCode.AppendLine(GenerateEnum(typeName, typeDescriptor, ____));
						break;
					case "array":
						sourceCode.AppendLine(GenerateArray(typeName, typeDescriptor, ____));
						break;
					default:
						throw new FormatException($"Type '{typeDescriptor.Type}' is not supported as top level type. ({typeName})");
				}
			}

			sourceCode.Append(@"}");
			return sourceCode.ToString();
		}

		private static string GenerateApiClientClass(string apiName, IReadOnlyList<ApiEndpoint> apiEndpoints, Dictionary<string, SecuritySchemeDescriptor>? securitySchemes)
		{
			string className = apiName + "Client";
			var sourceCode = new StringBuilder($@"#nullable enable
namespace {BASE_NAMESPACE}.{apiName}
{{
{____}using System;
{____}using System.Net.Http;
{____}using System.Net.Http.Json;
{____}using System.Text.Json;
{____}using System.Threading.Tasks;
{____}using {BASE_NAMESPACE};

{____}{TYPE_ACCESS_MODIFIER}class {className} : ApiClientBase
{____}{{
{____}{____}private readonly HttpClient httpClient;

{____}{____}public {className}(HttpClient httpClient) : base(httpClient)
{____}{____}{{
{____}{____}{____}this.httpClient = httpClient;
{____}{____}}}

{____}{____}protected override async Task<Token?> Authenticate()
{____}{____}{{
{____}{____}{____}return null; // TODO: implement this
{____}{____}}}

");

			foreach (var apiEndpoint in apiEndpoints)
			{
				string returnTypeName = "void"; // TODO: parse from OK response
				string operationName = apiEndpoint.OperationId ?? (apiEndpoint.Method.ToString() + "_" + apiEndpoint.Path.Replace('/', '_')).ToPascalCase();
				sourceCode.AppendLine($"{____}{____}/// {apiEndpoint.Method.Method.ToUpperInvariant()} {apiEndpoint.Path}");
				sourceCode.AppendLine($@"{____}{____}public {returnTypeName} {operationName}({string.Join(", ", apiEndpoint.Parameters.OrderByDescending(p => p.Required ?? false).Select(p => GenerateParameterSource(p)))})
{____}{____}{{
{____}{____}{____}
{____}{____}}}
");
			}

			sourceCode.Append($@"{____}}}
}}");
			return sourceCode.ToString();
		}

		private static string GenerateParameterSource(ApiEndpointParameter parameter)
		{
			try
			{
				string initExpression = (parameter.Required is not true) ? " = default" : "";
				string attribute = parameter.In switch
				{
					ParameterLocation.Query => "[FromQuery] ",
					ParameterLocation.Path => "[FromRoute] ",
					ParameterLocation.Header => "[FromHeader] ",
					_ => throw new FormatException($"Unsupported parameter location '{parameter.In}'")
				};
				return $"{attribute}{GetPropertyCSharpType(parameter.Schema, out _)} {parameter.Name}{initExpression}";
			}
			catch (NullReferenceException ex)
			{
				throw new InvalidOperationException($"Parameter {parameter}; {ex.StackTrace}");
			}
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
							source.AppendLine($"{indent}{____}{value.ToPascalCase()} = {number},");
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
							source.AppendLine($"{indent}{____}{value.ToPascalCase()} = {index + 1},");
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
				sourceCode.AppendLine($"{indent}{____}[JsonPropertyName(\"{propertyName}\")]");
				sourceCode.AppendLine($"{indent}{____}{PROPERTY_ACCESS_MODIFIER}{typeName} {propertyNamePascalCase} {{ get; init; }}{initExpression}");
			}

			sourceCode.AppendLine($"{indent}}}");
			return sourceCode.ToString();

		}
		private static string GetPropertyCSharpType(TypeDescriptor typeDescriptor, out bool isReferenceType)
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

		private static IEnumerable<ApiEndpoint> ParseApiEndpoints(JProperty pathProperty)
		{
			if (pathProperty.Value is JObject pathObject)
			{
				foreach (var httpMethodProperty in pathObject.Properties())
				{
					if (httpMethodProperty.Value is JObject httpMethodObject)
					{
						ApiEndpointParameter[] parameters;
						if (httpMethodObject["parameters"] is JArray paramsArray)
						{
							try
							{
								parameters = paramsArray.OfType<JObject>().Select(obj => ParseApiEndpointParameter(obj)).ToArray();
							}
							catch (FormatException ex)
							{
								throw new FormatException($"{ex.Message} ({httpMethodProperty.Name} {pathProperty.Name})");
							}
						}
						else
						{
							parameters = Array.Empty<ApiEndpointParameter>();
						}
						string? operationId = httpMethodObject["operationId"]?.ToString();
						string[]? tags = (httpMethodObject["tags"] as JArray)?.Select(token => token.ToString()).ToArray();

						// TODO: parse responses
						// httpMethodObject["responses"] -> object (dictionary where keys are strings representing http status codes, e.g. "200")

						yield return new ApiEndpoint(Method: new HttpMethod(httpMethodProperty.Name), Path: pathProperty.Name, parameters, operationId, tags);
					}
				}
			}
		}

		private static ApiEndpointParameter ParseApiEndpointParameter(JObject jObject)
		{
			string? name = null;
			ParameterLocation? paramLocation = null;
			bool? required = null;
			TypeDescriptor? schema = null;
			string? description = null;

			var nameToken = jObject["name"];
			if (nameToken is { Type: JTokenType.String })
				name = nameToken.ToString();
			var inToken = jObject["in"];
			if (inToken is { Type: JTokenType.String } && Enum.TryParse<ParameterLocation>(inToken.ToString(), ignoreCase: true, out var inParam))
				paramLocation = inParam;
			var requiredToken = jObject["required"];
			if (requiredToken is { Type: JTokenType.Boolean })
				required = (bool)requiredToken;
			var descriptionToken = jObject["description"];
			if (descriptionToken is { Type: JTokenType.String })
				description = descriptionToken.ToString();
			var schemaToken = jObject["schema"];
			if (schemaToken is JObject schemaObject)
				schema = ParseTypeDescriptor(schemaObject);

			return new ApiEndpointParameter(
				Name: name ?? throw new FormatException("'name' value not provided for api endpoint parameter."),
				In: paramLocation ?? throw new FormatException("'in' value not provided for api endpoint parameter."),
				Schema: schema ?? throw new FormatException("'schema' value not provided for api endpoint parameter."),
				required,
				description);
		}
	}
}
