using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace ApiBindingsGenerator
{
    static class KeyValueExtensions
    {
        public static void Deconstruct<TKey, TValue>(this KeyValuePair<TKey, TValue> keyValuePair, out TKey key, out TValue value) => (key, value) = (keyValuePair.Key, keyValuePair.Value);
    }

	static class StringExtensions
	{
        public static string ToPascalCase(this string source)
        {
            Regex invalidCharsRgx = new Regex("[^_a-zA-Z0-9]");
            Regex whiteSpace = new Regex(@"(?<=\s)");
            Regex startsWithLowerCaseChar = new Regex("^[a-z]");
            Regex firstCharFollowedByUpperCasesOnly = new Regex("(?<=[A-Z])[A-Z0-9]+$");
            Regex lowerCaseNextToNumber = new Regex("(?<=[0-9])[a-z]");
            Regex upperCaseInside = new Regex("(?<=[A-Z])[A-Z]+?((?=[A-Z][a-z])|(?=[0-9]))");
            Regex firstCharDigit = new Regex(@"^\d");

            // replace white spaces with undescore, then replace all invalid chars with empty string
            var pascalCase = invalidCharsRgx.Replace(whiteSpace.Replace(source, "_"), string.Empty)
                // split by underscores
                .Split(new char[] { '_' }, StringSplitOptions.RemoveEmptyEntries)
                // set first letter to uppercase
                .Select(w => startsWithLowerCaseChar.Replace(w, m => m.Value.ToUpper()))
                // replace second and all following upper case letters to lower if there is no next lower (ABC -> Abc)
                .Select(w => firstCharFollowedByUpperCasesOnly.Replace(w, m => m.Value.ToLower()))
                // set upper case the first lower case following a number (Ab9cd -> Ab9Cd)
                .Select(w => lowerCaseNextToNumber.Replace(w, m => m.Value.ToUpper()))
                // lower second and next upper case letters except the last if it follows by any lower (ABcDEf -> AbcDef)
                .Select(w => upperCaseInside.Replace(w, m => m.Value.ToLower()));

            string pascalCaseString = string.Concat(pascalCase);

            // prepend with underscore if the first letter is digit
            string pascalCaseValidIdentifier = firstCharDigit.Replace(pascalCaseString, m => "_" + m.Value);

            return pascalCaseValidIdentifier;
        }
    }

    static class EnumerableExtensions
    {
        public static IEnumerable<T> EmptyIfNull<T>(this IEnumerable<T>? source) => source ?? Enumerable.Empty<T>();
    }
}
