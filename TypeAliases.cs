using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Unity.ReferenceRewriter
{
	static class TypeAliases
	{
		private static readonly SortedSet<Tuple<string, string>> _aliases;

		static TypeAliases()
		{
			_aliases = new SortedSet<Tuple<string, string>>
			{
				new Tuple<string, string>(
					"System.Collections.Generic.IReadOnlyList",									"Windows.Foundation.Collections.IVectorView"),
				new Tuple<string, string>(
					"System.Collections.Generic.IEnumerable",									"Windows.Foundation.Collections.IIterable"),
				new Tuple<string, string>(
					"System.Collections.Generic.KeyValuePair",									"Windows.Foundation.Collections.IKeyValuePair"),
				new Tuple<string, string>(
					"System.Collections.Generic.IDictionary",									"Windows.Foundation.Collections.IMap"),
				new Tuple<string, string>(
					"System.Collections.Generic.IReadOnlyDictionary",							"Windows.Foundation.Collections.IMapView"),
				new Tuple<string, string>(
					"System.Collections.Generic.IList",											"Windows.Foundation.Collections.IVector"),
				new Tuple<string, string>(
					"System.Runtime.InteropServices.WindowsRuntime.EventRegistrationToken",		"Windows.Foundation.EventRegistrationToken"),
				new Tuple<string, string>(
					"System.DateTimeOffset",													"Windows.Foundation.DateTime"),
				new Tuple<string, string>(
					"System.TimeSpan",															"Windows.Foundation.TimeSpan"),
				new Tuple<string, string>(
					"System.Exception",															"Windows.Foundation.HResult")
			};
		}

		public static bool AreAliases(string typeA, string typeB)
		{
			string[] templateA = GetTemplateArguments(typeA),
				templateB = GetTemplateArguments(typeB);

			var namesMatch = typeA == typeB;
			bool templatesMatch = templateA.Length == templateB.Length;

			if (templatesMatch)
			{
				for (int i = 0; i < templateA.Length; i++)
				{
					templatesMatch &= templateA[i] == templateB[i] || 
						(templateA[i] != string.Empty && templateB[i] != string.Empty && AreAliases(templateA[i], templateB[i]));
				}
			}

			if (templateA.Length != 0)
			{
				// Character '`' denotes start of template arguments
				typeA = typeA.Substring(0, typeA.IndexOf('`'));
			}

			if (templateB.Length != 0)
			{
				typeB = typeB.Substring(0, typeB.IndexOf('`'));
			}

			var areAliases = _aliases.Contains(new Tuple<string, string>(typeA, typeB)) ||
							_aliases.Contains(new Tuple<string, string>(typeB, typeA));

			return namesMatch || (templatesMatch && areAliases);
		}

		private static string[] GetTemplateArguments(string type)
		{
			string template;

			int startIndex = type.IndexOf('<') + 1;
			int length = type.LastIndexOf('>') - startIndex;

			if (startIndex > 1 && length > 0)
			{
				template = type.Substring(startIndex, length);
			}
			else if (startIndex == 0 && length == -1)
			{
				template = string.Empty;
			}
			else
			{
				throw new ArgumentException("Invalid type name!", type);
			}

			return template.Split(new char[] { ',', ' ' }, StringSplitOptions.RemoveEmptyEntries);
		}
	}
}
