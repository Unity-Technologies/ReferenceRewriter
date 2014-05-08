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
			var platformPath = "";
			var systemNamespace = "";
			var strongNamedReferences = "";
			var winmdReferences = "";
			var symbolFormat = DebugSymbolFormat.None;
			var alt = new Dictionary<string, IList<string>>();
			var ignore = new Dictionary<string, IList<string>>();

			var set = new OptionSet
			{
				{ "target=", "The target module to rewrite.", t => targetModule = t },
				{ "output=", "Where to write the rewritten target module. Default is write over.", o => targetModuleOutput = o },
				{ "support=", "The support module containing the replacement API.", s => supportModule = s },
				{ "framework=", "A comma separated list of the directories of the target framework.", f => frameworkPath = f },
				{ "platform=", "Path to platform assembly.", p => platformPath = p },
				{ "system=", "The support namespace for System.", s => systemNamespace = s },
				{ "snrefs=", "A comma separated list of assembly names that retain their strong names.", s => strongNamedReferences = s },
				{ "winmdrefs=", "A comma separated list of assembly names that should be redirected to winmd references.", s => winmdReferences = s },
				{ "dbg=", "File format of the debug symbols. Either none, mdb or pdb.", d => symbolFormat = SymbolFormat(d) },
				{ "alt=", "A semicolon separated list of alternative namespace and assembly mappings.", a => AltFormat(alt, a) },
				{ "ignore=", "A semicolon separated list of assembly qualified type names that should not be resolved.", i => IgnoreFormat(ignore, i) },

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

                var frameworkPaths = frameworkPath.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries);
				var strongNamedReferencesArray = strongNamedReferences.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries);
				var winmdReferencesArray = winmdReferences.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries);
				var context = RewriteContext.For(targetModule, symbolFormat, supportModule, frameworkPaths, platformPath, strongNamedReferencesArray, winmdReferencesArray, alt, ignore);

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

		private static void IgnoreFormat(IDictionary<string, IList<string>> ignore, string i)
		{
			foreach (var pair in i.Split(';'))
			{
				var parts = pair.Split(new char[] { ',' }, 2);

				if (parts.Length != 2)
					throw new OptionException("Type name is not assembly qualified.", "ignore");

				var typeName = parts[0];
				var assemblyName = parts[1];

				IList<string> typeNames;

				if (!ignore.TryGetValue(assemblyName, out typeNames))
				{
					typeNames = new List<string>();
					ignore.Add(assemblyName, typeNames);
				}

				typeNames.Add(typeName);
			}
		}

		private static void Usage(OptionSet set)
		{
			Console.WriteLine("rrw reference rewriter");
			set.WriteOptionDescriptions(Console.Out);
		}
	}
}
