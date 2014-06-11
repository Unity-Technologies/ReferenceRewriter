using System;
using System.Collections.Generic;
using System.IO;
using Mono.Cecil;
using Mono.Cecil.Cil;
using Mono.Cecil.Mdb;
using Mono.Cecil.Pdb;
using Mono.Collections.Generic;

namespace Unity.ReferenceRewriter
{
	public class RewriteContext
	{
		public bool RewriteTarget { get; set; }
		public ModuleDefinition TargetModule { get; private set; }
		public ModuleDefinition SupportModule { get; private set; }
		public string SupportModulePartialNamespace { get; private set; }
		public IDictionary<string, ModuleDefinition[]> AltModules { get; private set; }
		public IDictionary<string, IList<string>> IgnoredTypes { get; private set; }
		public string[] FrameworkPaths { get; private set; }
		public IAssemblyResolver AssemblyResolver { get; private set; }
		public Collection<string> StrongNameReferences { get; private set; }
		public Collection<string> WinmdReferences { get; private set; }
		public DebugSymbolFormat DebugSymbolFormat { get; private set; }

		class RewriteResolver : DefaultAssemblyResolver
		{
			public RewriteResolver(string targetModule, string frameworkPath)
			{
				AddSearchDirectory(Path.GetDirectoryName(targetModule));
				AddSearchDirectory(frameworkPath);
			}

		    public RewriteResolver(string targetModule, string[] frameworkPaths)
		    {
				AddSearchDirectory(Path.GetDirectoryName(targetModule));

		        foreach (var path in frameworkPaths)
		        {
		            AddSearchDirectory(path);
		        }
		    }

		    public void RegisterSupportAssembly(AssemblyDefinition assembly)
			{
				RegisterAssembly(assembly);
			}

			public override AssemblyDefinition Resolve(AssemblyNameReference name)
			{
				try
				{
					return base.Resolve(name);
				}
				catch
				{
					if (name.IsWindowsRuntime)
						return ResolveAndRegisterWinmd(name);
				}

				throw new AssemblyResolutionException(name);
			}

			private AssemblyDefinition ResolveAndRegisterWinmd(AssemblyNameReference name)
			{
				foreach (var dir in this.GetSearchDirectories())
				{
					string file = Path.Combine(dir, name.Name + ".winmd");
					if (!File.Exists(file))
						continue;

					AssemblyDefinition def = AssemblyDefinition.ReadAssembly(file);
					RegisterAssembly(def);
					return def;
				}

				throw new AssemblyResolutionException(name);
			}
		}

		public static RewriteContext For(string targetModule, DebugSymbolFormat symbolFormat, string supportModule, string supportModulePartialNamespace, string[] frameworkPaths, string platformPath, ICollection<string> strongNamedReferences, ICollection<string> winmdReferences, IDictionary<string, IList<string>> alt, IDictionary<string, IList<string>> ignore)
		{
			if (targetModule == null)
				throw new ArgumentNullException("targetModule");
			if (supportModule == null)
				throw new ArgumentNullException("supportModule");

            CheckFrameworkPaths(frameworkPaths);

		    var fullFrameworkPaths = new string[frameworkPaths.Length];
		    for (int i = 0; i < frameworkPaths.Length; i++)
		    {
		        fullFrameworkPaths[i] = Path.GetFullPath(frameworkPaths[i]);
		    }

            var resolver = new RewriteResolver(targetModule, fullFrameworkPaths);
			var support = ModuleDefinition.ReadModule(supportModule, new ReaderParameters {AssemblyResolver = resolver});
			resolver.RegisterSupportAssembly(support.Assembly);

			if (!string.IsNullOrEmpty(platformPath))
			{
				var platform = ModuleDefinition.ReadModule(platformPath, new ReaderParameters {AssemblyResolver = resolver});
				resolver.RegisterSupportAssembly(platform.Assembly);
			}

			var altModules = new Dictionary<string, ModuleDefinition[]>();

			foreach (var pair in alt)
			{
				var modules = new ModuleDefinition[pair.Value.Count];

				for (var i = 0; i < modules.Length; ++i)
				{
				    string path = null;
				    foreach (var frameworkPath in frameworkPaths)
				    {
				        path = Path.Combine(frameworkPath, (pair.Value[i] + ".dll"));

				        if (File.Exists(path))
				        {
				            break;
				        }
				    }
				    modules[i] = ModuleDefinition.ReadModule(path, new ReaderParameters { AssemblyResolver = resolver });
				}

				altModules.Add(pair.Key, modules);
			}

			var target = ModuleDefinition.ReadModule(targetModule, TargetModuleParameters(targetModule, symbolFormat, resolver));
			resolver.RegisterSupportAssembly(target.Assembly);

			return new RewriteContext
			{
				TargetModule = target,
				SupportModule = support,
				SupportModulePartialNamespace = supportModulePartialNamespace,
				AltModules = altModules,
				IgnoredTypes = ignore,
				FrameworkPaths = frameworkPaths,
				AssemblyResolver = resolver,
				StrongNameReferences = new Collection<string>(strongNamedReferences),
				WinmdReferences = new Collection<string>(winmdReferences),
				DebugSymbolFormat = symbolFormat,
			};
		}

		private static void CheckFrameworkPath(string frameworkPath)
		{
			if (frameworkPath == null)
				throw new ArgumentNullException("frameworkPath");
			if (string.IsNullOrEmpty(frameworkPath))
				throw new ArgumentException("Empty framework path", "frameworkPath");
			if (!Directory.Exists(frameworkPath))
				throw new ArgumentException("Reference path doesn't exist.", "frameworkPath");
			if (!File.Exists(Path.Combine(frameworkPath, "mscorlib.dll")))
				throw new ArgumentException("No mscorlib.dll in the framework path.", "frameworkPath");
		}

	    private static void CheckFrameworkPaths(string[] frameworkPaths)
        {
            int timesFoundMscorlib = 0;
            foreach (var path in frameworkPaths)
            {
                try
                {
                    CheckFrameworkPath(path);
                    timesFoundMscorlib++;
                }
                catch (ArgumentException e)
                {
					if (!e.Message.Contains(@"No mscorlib.dll in the framework path."))
                    {
                        throw;
                    }
                }
            }

            if (timesFoundMscorlib == 0)
            {
                throw new ArgumentException("No mscorlib.dll in the framework path.", "frameworkPaths");
            }
	    }

		private static ReaderParameters TargetModuleParameters(string targetModule, DebugSymbolFormat symbolFormat, RewriteResolver resolver)
		{
			var targetParameters = new ReaderParameters { AssemblyResolver = resolver };

			if (File.Exists(Path.ChangeExtension(targetModule, ".pdb")) && symbolFormat == DebugSymbolFormat.Pdb)
				targetParameters.SymbolReaderProvider = new PdbReaderProvider();

			if (File.Exists(targetModule + ".mdb") && symbolFormat == DebugSymbolFormat.Mdb)
				targetParameters.SymbolReaderProvider = new MdbReaderProvider();

			return targetParameters;
		}

		public void Save(string targetModule)
		{
			var parameters = new WriterParameters();
			if (TargetModule.HasSymbols && DebugSymbolFormat != DebugSymbolFormat.None)
				parameters.SymbolWriterProvider = DebugSymbolFormat == DebugSymbolFormat.Mdb
					? (ISymbolWriterProvider) new MdbWriterProvider()
					: new PdbWriterProvider();

			TargetModule.Write(targetModule, parameters);
		}
	}
}
