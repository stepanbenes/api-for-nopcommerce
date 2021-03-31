using System;
using System.Collections.Generic;
using System.Text;

namespace ApiBindingsGenerator
{
	class TypeDescriptor
	{
		public TypeDescriptor(string type, string? format = null, bool? nullable = null, TypeDescriptor? items = null)
		{
			Type = type;
			Format = format;
			Nullable = nullable;
		}
		public string Type { get; }
		public string? Format { get; }
		public bool? Nullable { get; }
		public TypeDescriptor? Items { get; }
	}
}
