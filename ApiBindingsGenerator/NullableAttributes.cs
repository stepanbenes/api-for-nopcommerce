// workaround; target is .NET Standard 2.0 which does not define special nullability attrubutes recognized by C#8 compiler

namespace System.Diagnostics.CodeAnalysis
{
	//
	// Summary:
	//     Specifies that the output will be non-null if the named parameter is non-null.
	[AttributeUsage(AttributeTargets.Property | AttributeTargets.Parameter | AttributeTargets.ReturnValue, AllowMultiple = true, Inherited = false)]
	public sealed class NotNullIfNotNullAttribute : Attribute
	{
		//
		// Summary:
		//     Initializes the attribute with the associated parameter name.
		//
		// Parameters:
		//   parameterName:
		//     The associated parameter name. The output will be non-null if the argument to
		//     the parameter specified is non-null.
		public NotNullIfNotNullAttribute(string parameterName) => ParameterName = parameterName;

		//
		// Summary:
		//     Gets the associated parameter name.
		//
		// Returns:
		//     The associated parameter name. The output will be non-null if the argument to
		//     the parameter specified is non-null.
		public string ParameterName { get; }
	}
}