using System;
using System.Collections.Generic;
using System.Linq;
using Mono.Options;

namespace Unity.ReferenceRewriter
{
	class Program
	{
		static int Main(string[] args)
		{
			var help = false;
			var targetModule = "";
			var targetModuleOutput = "";
			var supportModule = "";
			var frameworkPath = "";
			var systemNamespace = "";
			var strongNamedReferences = "";
			var symbolFormat = DebugSymbolFormat.None;
			var alt = new Dictionary<string, IList<string>>();

			var set = new OptionSet
			{
				{ "target=", "The target module to rewrite.", t => targetModule = t },
				{ "output=", "Where to write the rewritten target module. Default is write over.", o => targetModuleOutput = o },
				{ "support=", "The support module containing the replacement API.", s => supportModule = s },
				{ "framework=", "The directory of the target framework.", f => frameworkPath = f },
				{ "system=", "The support namespace for System.", s => systemNamespace = s },
				{ "snrefs=", "A comma separated list of assembly names that retain their strong names.", s => strongNamedReferences = s },
				{ "dbg=", "File format of the debug symbols. Either none, mdb or pdb.", d => symbolFormat = SymbolFormat(d) },
				{ "alt=", "A semicolon separated list of alternative namespace and assembly mappings.", a => AltFormat(alt, a) },

				{ "?|h|help", h => help = true },
			};

			try
			{
				set.Parse(args);
			}
			catch (OptionException)
			{
				Usage(set);
				return 3;
			}

			if (help || new[] {targetModule, supportModule, frameworkPath, systemNamespace }.Any(string.IsNullOrWhiteSpace))
			{
				Usage(set);
				return 2;
			}

			try
			{
				var operation = RewriteOperation.Create(
					ns => ns.StartsWith("System")
						? systemNamespace + ns.Substring("System".Length)
						: ns);

				var context = RewriteContext.For(targetModule, symbolFormat, supportModule, frameworkPath, strongNamedReferences.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries), alt);

				operation.Execute(context);

				if (context.RewriteTarget)
					context.Save(string.IsNullOrEmpty(targetModuleOutput) ? targetModule : targetModuleOutput);
			}
			catch (Exception e)
			{
				Console.WriteLine("Catastrophic failure while running rrw: {0}", e);
				return 1;
			}

			return 0;
		}

		private static DebugSymbolFormat SymbolFormat(string d)
		{
			return string.Equals(d, "mdb", StringComparison.OrdinalIgnoreCase)
				? DebugSymbolFormat.Mdb
				: string.Equals(d, "pdb", StringComparison.OrdinalIgnoreCase)
					? DebugSymbolFormat.Pdb
					: DebugSymbolFormat.None;
		}

		private static void AltFormat(IDictionary<string, IList<string>> alt, string a)
		{
			foreach (var pair in a.Split(';'))
			{
				var parts = pair.Split(new char[] { ',' }, 2);

				var @namespace = parts[0];
				var assemblyName = @namespace;

				if (parts.Length > 1)
					assemblyName = parts[1];

				IList<string> assemblyNames;

				if (!alt.TryGetValue(@namespace, out assemblyNames))
				{
					assemblyNames = new List<string>();
					alt.Add(@namespace, assemblyNames);
				}

				assemblyNames.Add(assemblyName);
			}
		}

		private static void Usage(OptionSet set)
		{
			Console.WriteLine("rrw reference rewriter");
			set.WriteOptionDescriptions(Console.Out);
		}
	}
}
