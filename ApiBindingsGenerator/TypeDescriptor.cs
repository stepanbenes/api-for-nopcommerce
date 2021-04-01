using System;
using System.Collections.Generic;
using System.Text;

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
}
