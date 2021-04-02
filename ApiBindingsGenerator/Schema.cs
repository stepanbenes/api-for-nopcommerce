using System;
using System.Collections.Generic;
using System.Net.Http;

namespace ApiBindingsGenerator
{
	record TypeDescriptor
	{
		public TypeDescriptor(string? type = null, string? format = null, bool? nullable = null, TypeDescriptor? items = null, string[]? enumValues = null, string? refType = null, Dictionary<string, TypeDescriptor>? properties = null)
		{
			Type = type;
			Format = format;
			Nullable = nullable;
			Items = items;
			EnumValues = enumValues;
			RefType = refType;
			Properties = properties;
		}
		public string? Type { get; }
		public string? Format { get; }
		public bool? Nullable { get; }
		public TypeDescriptor? Items { get; }
		public string[]? EnumValues { get; }
		public string? RefType { get; }
		public Dictionary<string, TypeDescriptor>? Properties { get; }
	}

	record ApiInfo(string Title, string Version)
	{
		public string Title { get; } = Title;
		public string Version { get; } = Version;
	}

	record SecuritySchemeDescriptor(string Type, string? Description, string Name, string In)
	{
		public string Type { get; } = Type;
		public string? Description { get; } = Description;
		public string Name { get; } = Name;
		public string In { get; } = In;
	}



	record ApiEndpoint(HttpMethod Method, string Path, ApiEndpointParameter[] Parameters, string? OperationId = null, string[]? Tags = null)
	{
		public HttpMethod Method { get; } = Method;
		public string Path { get; } = Path;
		public string? OperationId { get; } = OperationId;
		public string[]? Tags { get; } = Tags;
		public ApiEndpointParameter[] Parameters { get; } = Parameters;
	}

	enum ParameterLocation
	{
		Query,
		Path,
		Header
	}

	record ApiEndpointParameter(string Name, ParameterLocation In, TypeDescriptor Schema, bool? Required = null, string? Description = null)
	{
		public string Name { get; } = Name;
		public ParameterLocation In { get; } = In;
		public TypeDescriptor Schema { get; } = Schema;
		public bool? Required { get; } = Required;
		public string? Description { get; } = Description;
	}
}
