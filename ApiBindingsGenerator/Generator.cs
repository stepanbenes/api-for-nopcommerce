using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
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
		private const string TYPE_ACCESS_MODIFIER = "public ";
		private const string PROPERTY_ACCESS_MODIFIER = "public ";
		private const string JSON_MEDIA_TYPE = "application/json";
		private const string REQUEST_BODY_PARAMETER_NAME = "requestBody";

		enum ApiClientType
		{
			NormalClass = 0,
			FakeClass,
			Interface
		}

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

		#region Generating

		private static IEnumerable<(string filename, string code)> GenerateCommonFiles()
		{
			string code = $"namespace {BASE_NAMESPACE}" + @"
{
    using System;
    using System.Net;
    using System.Net.Http;
    using System.Net.Http.Json;
    using System.Text.Json;
    using System.Text.Json.Serialization;
    using System.Threading.Tasks;

    " + $"{TYPE_ACCESS_MODIFIER}abstract class ApiClientBase" + @"
    {
        private readonly HttpClient httpClient;
        private JsonSerializerOptions? serializerOptions = null;

        public ApiClientBase(HttpClient httpClient)
        {
            this.httpClient = httpClient;
        }

        protected JsonSerializerOptions SerializerOptions => serializerOptions ?? (serializerOptions = new JsonSerializerOptions { IgnoreNullValues = true, Converters = { new JsonStringEnumConverter() } });
        
        protected async Task<HttpResponseMessage> Send(HttpMethod httpMethod, string requestEndpoint, HttpContent? content = null)
        {
            var request = new HttpRequestMessage(httpMethod, requestEndpoint);
            request.Content = content;
            return await httpClient.SendAsync(request);
        }

        public void SetAuthorizationHeader(string scheme, string? parameter)
        {
            httpClient.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue(scheme, parameter);
        }

        public void RemoveAuthorizationHeader()
        {
            httpClient.DefaultRequestHeaders.Authorization = null;
        }
    }

    public class ApiException : Exception
    {
        public HttpStatusCode StatusCode { get; }
        public virtual object? Error { get; }
        public ApiException(HttpStatusCode statusCode, string message, object? error) : base(message)
        {
            StatusCode = statusCode;
            Error = error;
        }
    }

    public class ApiException<TError> : ApiException where TError : class
    {
        public override TError? Error { get; }
        public ApiException(HttpStatusCode statusCode, string message, TError? error) : base(statusCode, message, error) { }
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
			yield return (filename: $"I{apiName}Client", GenerateApiClient(ApiClientType.Interface, apiName, apiEndpoints, schema, securitySchemes));
			yield return (filename: $"{apiName}Client", GenerateApiClient(ApiClientType.NormalClass, apiName, apiEndpoints, schema, securitySchemes));
			yield return (filename: $"Fake{apiName}Client", GenerateApiClient(ApiClientType.FakeClass, apiName, apiEndpoints, schema, securitySchemes));
		}

		private static string GenerateDTOs(string apiName, Dictionary<string, TypeDescriptor> schema)
		{
			var sourceCode = new StringBuilder($@"#nullable enable
namespace {BASE_NAMESPACE}.{apiName}.DTOs
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
					case "integer" or "string" when typeDescriptor.EnumValues is not null:
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

		private static string GenerateApiClient(ApiClientType apiClientType, string apiName, IReadOnlyList<ApiEndpoint> apiEndpoints, Dictionary<string, TypeDescriptor> typeMap, Dictionary<string, SecuritySchemeDescriptor>? securitySchemes)
		{
			string className = apiName + "Client";
			var sourceCode = new StringBuilder();

			switch (apiClientType)
			{
				case ApiClientType.NormalClass:
					writeNormalClassBeginning(sourceCode, apiName, className);
					break;
				case ApiClientType.FakeClass:
					writeFakeClassBeginning(sourceCode, apiName, className);
					break;
				case ApiClientType.Interface:
					writeInterfaceBeginning(sourceCode, apiName, className);
					break;
				default:
					break;
			}

			foreach (var apiEndpoint in apiEndpoints)
			{
				string endpointLabel = $"{apiEndpoint.Method.Method.ToUpperInvariant()} {apiEndpoint.Path}";
				TypeDescriptor? returnType = apiEndpoint.Responses.TryGetValue(HttpStatusCode.OK, out var okResponse)
					? getTypeDescriptorForResponse(okResponse, JSON_MEDIA_TYPE)
					: null;
				string taskReturnTypeName = returnType is null ? "Task" : $"Task<{GenerateCSharpType(returnType.MakeNullable(), out _)}>";

				string? requestBodyTypeName = null;

				// TODO: add support for multi-form media type (and other media types?)

				if (apiEndpoint.RequestBody is not null && apiEndpoint.RequestBody.Content.TryGetValue(JSON_MEDIA_TYPE, out TypeDescriptor requestBodyTypeDescriptor))
				{
					requestBodyTypeName = GenerateCSharpType(requestBodyTypeDescriptor, out _);
				}

				var parameters = apiEndpoint.Parameters.OrderByDescending(p => p.Required ?? false).Select(p => GenerateApiEndpointParameter(p));
				if (requestBodyTypeName is not null)
				{
					parameters = parameters.Prepend($"{requestBodyTypeName} {REQUEST_BODY_PARAMETER_NAME}");
				}


				string operationName = apiEndpoint.OperationId ?? (apiEndpoint.Method.Method + "_" + apiEndpoint.Path.Replace('/', '_')).ToPascalCase();
				sourceCode.AppendLine($"{____}{____}/// <summary>");
				sourceCode.AppendLine($"{____}{____}/// {endpointLabel}");
				sourceCode.AppendLine($"{____}{____}/// </summary>");

				switch (apiClientType)
				{
					case ApiClientType.NormalClass:
						sourceCode.Append($@"{____}{____}public async {taskReturnTypeName} {operationName}({string.Join(", ", parameters)})");
						writeNormalActionBody(sourceCode, apiEndpoint, returnType, requestBodyTypeName, typeMap);
						break;
					case ApiClientType.FakeClass:
						sourceCode.Append($@"{____}{____}public virtual {taskReturnTypeName} {operationName}({string.Join(", ", parameters)})");
						writeFakeActionBody(sourceCode, returnType);
						break;
					case ApiClientType.Interface:
						sourceCode.AppendLine($@"{____}{____}{taskReturnTypeName} {operationName}({string.Join(", ", parameters)});");
						break;
				}
			} // end of foreach

			// write footer
			sourceCode.Append($@"{____}}}
}}");
			return sourceCode.ToString();

			static TypeDescriptor? getTypeDescriptorForResponse(Response response, string mediaType)
			{
				if (response.Content is null || !response.Content.TryGetValue(mediaType, out var typeDescriptor))
					return null;
				return typeDescriptor;
			}

			static void writeNormalActionBody(StringBuilder sourceCode, ApiEndpoint apiEndpoint, TypeDescriptor? returnType, string? requestBodyTypeName, Dictionary<string, TypeDescriptor> typeMap)
			{
				string? returnTypeName = GenerateCSharpType(returnType, out _);
				string returnNothingToken = $"return{(returnTypeName is not null ? " null" : "")};";
				var queryParamDescriptions = GenerateApiEndpointQuery(apiEndpoint, typeMap);
				sourceCode.AppendLine($@"
        {{
            HttpContent? content = {(requestBodyTypeName is not null ? $"JsonContent.Create({REQUEST_BODY_PARAMETER_NAME}, options: SerializerOptions)" : "null")};
			var queryParams = new List<string>();");

				if (queryParamDescriptions.Count > 0)
				{
					sourceCode.AppendLine($@"{____}{____}{____}string paramValueString;
");
				}

				foreach (var (paramName, paramValue) in queryParamDescriptions)
				{
					sourceCode.AppendLine($@"{____}{____}{____}paramValueString = $""{paramValue}"";
            if (!string.IsNullOrEmpty(paramValueString)) queryParams.Add($""{paramName}={{paramValueString}}"");");
				}

				sourceCode.AppendLine($@"
            string query = (queryParams.Count > 0) ? ""?"" + string.Join(""&"", queryParams) : """";");

				sourceCode.AppendLine($@"
            var response = await Send(HttpMethod.{apiEndpoint.Method.Method.ToPascalCase()}, requestEndpoint: $""{apiEndpoint.Path}{{query}}"", content);
            switch ((int)response.StatusCode)
            {{
                case 200: // OK");
				if (returnTypeName is not null)
				{
					if (returnTypeName is "string")
						sourceCode.AppendLine($@"                    return await response.Content.ReadAsStringAsync();");
					else
						sourceCode.AppendLine($@"                    return await response.Content.ReadFromJsonAsync<{returnTypeName}>(SerializerOptions);");
				}
				else
				{
					sourceCode.AppendLine($@"                    return;");
				}

				foreach (var key in apiEndpoint.Responses.Keys.Except(new[] { (HttpStatusCode)200 }))
				{
					var response = apiEndpoint.Responses[key];
					sourceCode.AppendLine($"{____}{____}{____}{____}case {(int)response.StatusCode}:");
					if ((int)response.StatusCode is > 200 and <= 299)
					{
						sourceCode.AppendLine($"{____}{____}{____}{____}{____}return{(returnTypeName is not null ? " null" : "")};");
					}
					else
					{
						if (getTypeDescriptorForResponse(response, JSON_MEDIA_TYPE) is { } responseTypeDescriptor)
						{
							string errorTypeName = GenerateCSharpType(responseTypeDescriptor, out _);
							if (errorTypeName is "string")
								sourceCode.AppendLine($"{____}{____}{____}{____}{____}throw new ApiException<{errorTypeName}>(response.StatusCode, \"{response.Description ?? ""}\", await response.Content.ReadAsStringAsync());");
							else
								sourceCode.AppendLine($"{____}{____}{____}{____}{____}throw new ApiException<{errorTypeName}>(response.StatusCode, \"{response.Description ?? ""}\", await response.Content.ReadFromJsonAsync<{errorTypeName}>(SerializerOptions));");
						}
						else
						{
							sourceCode.AppendLine($"{____}{____}{____}{____}{____}throw new ApiException(response.StatusCode, \"{response.Description ?? ""}\", await response.Content.ReadAsStringAsync());");
						}
					}
				}

				sourceCode.AppendLine($@"                case > 200 and <= 299: // no content
                    {returnNothingToken}
                case >= 400 and <= 499: // request error
                    throw new ApiException(response.StatusCode, $""{{(int)response.StatusCode}} ({{response.StatusCode}}) Request error"",  await response.Content.ReadAsStringAsync());
                case >= 500 and <= 599: // server error
                    throw new ApiException(response.StatusCode, ""Server error"", await response.Content.ReadAsStringAsync());
                default: // unexpected error
                    throw new ApiException(response.StatusCode, ""Unexpected status code"", await response.Content.ReadAsStringAsync());
            }}
        }}
");
			}

			static void writeFakeActionBody(StringBuilder sourceCode, TypeDescriptor? returnType)
			{
				string? returnTypeName = GenerateCSharpType(returnType?.MakeNullable(), out _);
				string returnNothingToken = $" => {(returnTypeName is not null ? $"Task.FromResult(({returnTypeName})null)" : "Task.CompletedTask")};";
				sourceCode.AppendLine(returnNothingToken);
			}

			static void writeNormalClassBeginning(StringBuilder sourceCode, string apiName, string className)
			{
				sourceCode.Append($@"#nullable enable
namespace {BASE_NAMESPACE}.{apiName}
{{
    using System;
	using System.Linq;
    using System.Collections.Generic;
    using System.Net.Http;
    using System.Net.Http.Json;
    using System.Text.Json;
    using System.Threading.Tasks;
    using {BASE_NAMESPACE};
    using {BASE_NAMESPACE}.{apiName}.DTOs;

    {TYPE_ACCESS_MODIFIER}class {className} : ApiClientBase, I{className}
    {{
        private readonly HttpClient httpClient;

        public {className}(HttpClient httpClient) : base(httpClient)
        {{
            this.httpClient = httpClient;
        }}

");
			}

			static void writeFakeClassBeginning(StringBuilder sourceCode, string apiName, string className)
			{
				sourceCode.Append($@"#nullable enable
namespace {BASE_NAMESPACE}.{apiName}
{{
    using System;
    using System.Threading.Tasks;
    using {BASE_NAMESPACE};
    using {BASE_NAMESPACE}.{apiName}.DTOs;

    {TYPE_ACCESS_MODIFIER}class Fake{className} : I{className}
    {{
        public virtual void SetAuthorizationHeader(string scheme, string? parameter) {{ }}
        public virtual void RemoveAuthorizationHeader() {{ }}
");
			}

			static void writeInterfaceBeginning(StringBuilder sourceCode, string apiName, string className)
			{
				sourceCode.Append($@"#nullable enable
namespace {BASE_NAMESPACE}.{apiName}
{{
    using System;
    using System.Threading.Tasks;
    using {BASE_NAMESPACE};
    using {BASE_NAMESPACE}.{apiName}.DTOs;

    {TYPE_ACCESS_MODIFIER}interface I{className}
    {{
        /// <summary>
        /// Sets default authorization header for all requests of underlying http client. Scheme is usually ""Bearer"" and parameter is JWT token.
        /// </summary>
        void SetAuthorizationHeader(string scheme, string? parameter);
        /// <summary>
        /// Sets default authorization header for all requests of underlying http client to null;
        /// </summary>
        void RemoveAuthorizationHeader();
");
			}
		}

		private static List<(string paramName, string paramValue)> GenerateApiEndpointQuery(ApiEndpoint endpoint, Dictionary<string, TypeDescriptor> typeMap)
		{
			return endpoint.Parameters.Where(p => p.In == ParameterLocation.Query).Select(p => getParameterValue(p.Name, p.Schema)).ToList();

			(string paramName, string paramValue) getParameterValue(string paramName, TypeDescriptor paramSchema) =>
				paramSchema.Type switch
				{
					"string" or "number" or "integer" or "boolean" => (paramName, $@"{{{paramName}}}"),
					"array" => (paramName, $@"{{string.Join(',', ({paramName} as System.Collections.IEnumerable)?.Cast<object>().Select(o => o.ToString()) ?? Enumerable.Empty<string>())}}"),
					_ => (paramName, string.Empty)
				};
		}

		private static string GenerateApiEndpointParameter(ApiEndpointParameter parameter)
		{
			bool isOptional = parameter.Required is not true;
			string initExpression = isOptional ? " = default" : "";
			return $"{GenerateCSharpType(isOptional ? parameter.Schema.MakeNullable() : parameter.Schema, out _)} {parameter.Name}{initExpression}";
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
						foreach (string value in typeDescriptor.EnumValues)
						{
							source.AppendLine($"{indent}{____}{value},");
						}
					}
					break;
			}
			source.AppendLine($"{indent}}}");
			return source.ToString();
		}

		private static string GenerateRecordPrimaryParameter(string name, TypeDescriptor type)
		{
			return $"{GenerateCSharpType(type, out _)} {name}";
		}

		private static string GenerateRecord(string recordName, TypeDescriptor typeDescriptor, string indent)
		{
			if (typeDescriptor.Type != "object")
				throw new FormatException($"Cannot generate record '{recordName}' with non-object schema ({typeDescriptor.Type}).");

			var propertyMap = typeDescriptor.Properties ?? new Dictionary<string, TypeDescriptor>();
			var requiredProperties = typeDescriptor.RequiredProperties ?? new HashSet<string>();

			// generate primary constructor parameters from typeDescriptor.RequiredProperties set
			var primaryParameters = requiredProperties.Where(rp => propertyMap.ContainsKey(rp)).Select(rp => GenerateRecordPrimaryParameter(name: rp.ToPascalCase(), type: propertyMap[rp])).ToList();

			var sourceCode = new StringBuilder($@"{indent}{TYPE_ACCESS_MODIFIER}record {recordName}({string.Join(", ", primaryParameters)})
{indent}{{
");


			foreach (var (propertyName, propertyType) in typeDescriptor.Properties.EmptyIfNull())
			{
				string typeName;
				bool isReferenceType;
				try
				{
					typeName = GenerateCSharpType(propertyType, out isReferenceType);
				}
				catch (FormatException ex)
				{
					throw new FormatException(ex.Message + $" ({propertyName})");
				}
				string propertyNamePascalCase = propertyName.ToPascalCase();
				string initExpression;
				if (requiredProperties.Contains(propertyName))
				{
					initExpression = $" = {propertyNamePascalCase};";
				}
				else if (isReferenceType && propertyType.Nullable is not true)
				{
					initExpression = " = default!;";
				}
				else
				{
					initExpression = "";
				}
				sourceCode.AppendLine($"{indent}{____}[JsonPropertyName(\"{propertyName}\")]");
				sourceCode.AppendLine($"{indent}{____}{PROPERTY_ACCESS_MODIFIER}{typeName} {propertyNamePascalCase} {{ get; init; }}{initExpression}");
			}

			sourceCode.AppendLine($"{indent}}}");
			return sourceCode.ToString();

		}

		[return: NotNullIfNotNull("typeDescriptor")]
		private static string? GenerateCSharpType(TypeDescriptor? typeDescriptor, out bool isReferenceType)
		{
			if (typeDescriptor is null)
			{
				isReferenceType = false;
				return null;
			}
			string csTypeName;
			if (typeDescriptor.RefType is not null)
			{
				isReferenceType = true; // WARNING: I don't really know if it is reference type
				csTypeName = typeDescriptor.RefType.Substring("#/components/schemas/".Length);
			}
			else
			{
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
					("number", "decimal") => ("decimal", false), // currency type
					("number", _) => ("double", false),
					("integer", "int32") => ("int", false),
					("integer", "int64") => ("long", false),
					("integer", _) => ("int", false),
					("boolean", _) => ("bool", false),
					("object", _) => ("object", true), // nested object definition is not supported
					("array", _) => (GenerateCSharpType(typeDescriptor.Items ?? throw new FormatException($"array type requires to items type to be defined"), out _) + "[]", true),
					_ => throw new FormatException($"Unrecognized type descriptor (type: {typeDescriptor.Type}, format: {typeDescriptor.Format})")
				};
			}
			return (typeDescriptor.Nullable is true) ? csTypeName + "?" : csTypeName;
		}

		private static string GenerateArray(string arrayName, TypeDescriptor typeDescriptor, string indent)
		{
			if (typeDescriptor.Type != "array")
				throw new FormatException($"Cannot generate array '{arrayName}' with non-array schema ({typeDescriptor.Type}).");
			if (typeDescriptor.Items is null)
				throw new FormatException($"array type requires to items type to be defined ({arrayName})");

			var sourceCode = new StringBuilder($"{indent}{TYPE_ACCESS_MODIFIER}class {arrayName} : System.Collections.Generic.List<{GenerateCSharpType(typeDescriptor.Items, out _)}> {{ }}");
			sourceCode.AppendLine();
			return sourceCode.ToString();
		}

		#endregion

		#region Parsing

		private static TypeDescriptor ParseTypeDescriptor(JObject schema)
		{
			string? type = null;
			string? format = null;
			bool? nullable = null;
			TypeDescriptor? items = null;
			string? refType = null;
			Dictionary<string, TypeDescriptor>? properties = null;
			string[]? enumValues = null;
			HashSet<string>? requiredProperties = null;

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
			if (enumToken is JArray enumArray)
			{
				enumValues = enumArray.Select(jToken => jToken.ToString()).ToArray();
			}
			var requiredToken = schema["required"];
			if (requiredToken is JArray requiredArray)
			{
				requiredProperties = new HashSet<string>(requiredArray.Select(requiredToken => requiredToken.ToString()));
			}

			return new TypeDescriptor(type, format, nullable, items, enumValues, refType, properties, requiredProperties);
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
						RequestBody? requestBody = null;
						Dictionary<HttpStatusCode, Response>? responses = null;

						// parse requestBody
						if (httpMethodObject["requestBody"] is JObject requestBodyObject && requestBodyObject["content"] is JObject requestBodyContentObject)
						{
							requestBody = new RequestBody(parseContent(requestBodyContentObject));
						}
						// parse responses
						if (httpMethodObject["responses"] is JObject responsesObject)
						{
							responses = new();
							foreach (var property in responsesObject.Properties())
							{
								string statusCodeString = property.Name;
								if (Enum.TryParse(statusCodeString, out HttpStatusCode statusCode) && property.Value is JObject responseObject)
								{
									string? description = null;
									Dictionary<string, TypeDescriptor>? content = null;
									if (responseObject["description"] is { Type: JTokenType.String } descriptionToken)
										description = descriptionToken.ToString();
									if (responseObject["content"] is JObject responseContentObject)
										content = parseContent(responseContentObject);
									responses[statusCode] = new Response(statusCode, content, description);
								}
							}
						}

						yield return new ApiEndpoint(Method: new HttpMethod(httpMethodProperty.Name),
							Path: pathProperty.Name,
							parameters,
							responses ?? throw new FormatException($"Responses for api endpoint '{httpMethodProperty.Name} {pathProperty.Name}' were not provided."),
							requestBody, operationId, tags);

						static Dictionary<string, TypeDescriptor> parseContent(JObject contentObject)
						{
							Dictionary<string, TypeDescriptor> content = new();
							foreach (var property in contentObject.Properties())
							{
								string mediaType = property.Name;
								if ((property.Value as JObject)?["schema"] is JObject schemaObject)
								{
									content[mediaType] = ParseTypeDescriptor(schemaObject);
								}
								else
								{
									throw new FormatException("Schema for content is not defined.");
								}
							}
							return content;
						}
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

		#endregion
	}
}
