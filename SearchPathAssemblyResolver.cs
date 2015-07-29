using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Mono.Cecil;

namespace Unity.ReferenceRewriter
{
	internal class SearchPathAssemblyResolver : IAssemblyResolver
	{
		private readonly Dictionary<string, AssemblyDefinition> _assemblies = new Dictionary<string, AssemblyDefinition>(StringComparer.InvariantCulture);
		private readonly List<string> _searchPaths = new List<string>();

		public void RegisterAssembly(AssemblyDefinition assembly)
		{
			var name = assembly.Name.Name;
			if (_assemblies.ContainsKey(name))
				throw new Exception(string.Format("Assembly \"{0}\" is already registered.", name));
			_assemblies.Add(name, assembly);
		}

		public void RegisterSearchPath(string path)
		{
			if (_searchPaths.Any(p => string.Equals(p, path, StringComparison.InvariantCultureIgnoreCase)))
				return;
			_searchPaths.Add(path);
		}

		public AssemblyDefinition Resolve(string fullName)
		{
			return Resolve(fullName, new ReaderParameters());
		}

		public AssemblyDefinition Resolve(string fullName, ReaderParameters parameters)
		{
			return Resolve(AssemblyNameReference.Parse(fullName), parameters);
		}

		public AssemblyDefinition Resolve(AssemblyNameReference name)
		{
			return Resolve(name, new ReaderParameters());
		}

		public virtual AssemblyDefinition Resolve(AssemblyNameReference name, ReaderParameters parameters)
		{
			AssemblyDefinition assembly;
			if (_assemblies.TryGetValue(name.Name, out assembly))
				return assembly;

			foreach (var searchPath in _searchPaths)
			{
				var fileName = name.Name + (name.IsWindowsRuntime ? ".winmd" : ".dll");
				var filePath = Path.Combine(searchPath, fileName);
				if (!File.Exists(filePath))
					continue;

				assembly = AssemblyDefinition.ReadAssembly(filePath, parameters);
				if (!string.Equals(assembly.Name.Name, name.Name, StringComparison.InvariantCulture))
					continue;
				_assemblies.Add(name.Name, assembly);
				return assembly;
			}

			throw new AssemblyResolutionException(name);
		}
	}
}
