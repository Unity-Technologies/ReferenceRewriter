using Mono.Cecil;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace Unity.ReferenceRewriter
{
	class RemoveStrongNamesFromAssemblyReferences : RewriteStep
	{
		protected override void Run()
		{
			foreach (var reference in Context.TargetModule.AssemblyReferences)
			{
				if (ShouldRemoveStrongName(reference))
				{
					RemoveStrongName(reference);
				}
			}
		}

		private bool ShouldRemoveStrongName(AssemblyNameReference reference)
		{
			// Strong name is not present already
			if (reference.PublicKeyToken == null || reference.PublicKeyToken.Length == 0)
			{
				return false;
			}

			// Strong name must be kept
			if (Context.StrongNameReferences.Any(r => r == reference.Name))
			{
				return false;
			}

			AssemblyDefinition assembly;

			try
			{
				// Can't find target assembly
				assembly = Context.AssemblyResolver.Resolve(reference);
			}
			catch (AssemblyResolutionException)
			{
				return false;
			}

			// Don't remove strong name to framework references
			var assemblyDir = NormalizePath(Path.GetDirectoryName(assembly.MainModule.FullyQualifiedName));
			if (Context.FrameworkPaths.Any(path => NormalizePath(path) == assemblyDir))
			{
				return false;
			}

			return true;
		}

		private void RemoveStrongName(AssemblyNameReference reference)
		{
			Context.RewriteTarget = true;
			reference.PublicKeyToken = new byte[0];
		}

		public static string NormalizePath(string path)
		{
			return Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar).ToUpperInvariant();
		}
	}
}
