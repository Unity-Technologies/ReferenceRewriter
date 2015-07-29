using System;
using System.Collections.Generic;
using System.IO;
using Mono.Cecil;

namespace Unity.ReferenceRewriter
{
	internal class NuGetAssemblyResolver : SearchPathAssemblyResolver
	{
		private readonly Dictionary<string, string>  _references;
		private readonly Dictionary<string, AssemblyDefinition> _assemblies = new Dictionary<string, AssemblyDefinition>(StringComparer.InvariantCulture);

		public NuGetAssemblyResolver(string projectLockFile)
		{
			var resolver = new NuGetPackageResolver
			{
				ProjectLockFile = projectLockFile,
			};
			resolver.Resolve();
			var references = resolver.ResolvedReferences;

			_references = new Dictionary<string, string>(references.Length, StringComparer.InvariantCultureIgnoreCase);
			foreach (var reference in references)
			{
				var fileName = Path.GetFileName(reference);
				string existingReference;
				if (_references.TryGetValue(fileName, out existingReference))
					throw new Exception(string.Format("Reference \"{0}\" already added as \"{1}\".", reference, existingReference));
				_references.Add(fileName, reference);
			}
		}

		public override AssemblyDefinition Resolve(AssemblyNameReference name, ReaderParameters parameters)
		{
			AssemblyDefinition assembly;
			if (_assemblies.TryGetValue(name.Name, out assembly))
				return assembly;

			var fileName = name.Name + (name.IsWindowsRuntime ? ".winmd" : ".dll");
			string reference;
			if (_references.TryGetValue(fileName, out reference))
			{
				assembly = AssemblyDefinition.ReadAssembly(reference, parameters);
				if (string.Equals(assembly.Name.Name, name.Name, StringComparison.InvariantCulture))
				{
					_assemblies.Add(name.Name, assembly);
					return assembly;
				}
			}

			return base.Resolve(name, parameters);
		}
	}
}
