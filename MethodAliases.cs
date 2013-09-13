using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Unity.ReferenceRewriter
{
	class MethodAliases
	{
		private static readonly SortedSet<Tuple<string, string>> _aliases;

		static MethodAliases()
		{
			_aliases = new SortedSet<Tuple<string, string>>
			{
				new Tuple<string, string>(
					"Dispose",									"Close")
			};
		}


		public static bool AreAliases(string typeA, string typeB)
		{
			var areAliases = _aliases.Contains(new Tuple<string, string>(typeA, typeB)) ||
							_aliases.Contains(new Tuple<string, string>(typeB, typeA));

			return areAliases;
		}
	}
}
