using System;
using System.IO;
using System.Linq;
using Mono.Cecil;

namespace Unity.ReferenceRewriter
{
	class RewriteAssemblyManifest : RewriteStep
	{
		protected override void Run()
		{
			if (Context.TargetModule.Assembly.Name.HasPublicKey)
				RemoveStrongName();

			foreach (var reference in Context.TargetModule.AssemblyReferences)
				RewriteAssemblyReference(reference);
		}

		private void RemoveStrongName()
		{
			if (ShouldRemoveStrongName(Context.TargetModule.Assembly.Name))
				return;

			Context.RewriteTarget = true;
			Context.TargetModule.Assembly.Name.PublicKey = new byte[0];
			Context.TargetModule.Attributes = ModuleAttributes.ILOnly;
		}

		private void RewriteAssemblyReference(AssemblyNameReference reference)
		{
			try
			{
				var assembly = Context.AssemblyResolver.Resolve(reference);

				if (IsFrameworkAssembly(assembly) && ShouldRewriteFrameworkReference(reference, assembly))
				{
					Context.RewriteTarget = true;
					reference.Version = assembly.Name.Version;
					reference.PublicKeyToken = Copy(assembly.Name.PublicKeyToken);
				}
				else if (ShouldRemoveStrongName(reference))
				{
					Context.RewriteTarget = true;
					reference.PublicKeyToken = new byte[0];
				}

			}
			catch (AssemblyResolutionException)
			{
			}
		}

		private static bool ShouldRewriteFrameworkReference(AssemblyNameReference reference, AssemblyDefinition assembly)
		{
			return reference.Version != assembly.Name.Version
				|| !reference.PublicKeyToken.SequenceEqual(assembly.Name.PublicKeyToken);
		}

		private bool ShouldRemoveStrongName(AssemblyNameReference reference)
		{
			if (reference.PublicKeyToken == null || reference.PublicKeyToken.Length == 0)
				return false;

			return Context.StrongNameReferences.All(r => r != reference.Name);
		}

		private static byte[] Copy(byte[] bytes)
		{
			var copy = new byte[bytes.Length];
			Buffer.BlockCopy(bytes, 0, copy, 0, bytes.Length);
			return copy;
		}

		private bool IsFrameworkAssembly(AssemblyDefinition assembly)
		{
			return FullPath(Path.GetDirectoryName(assembly.MainModule.FullyQualifiedName)) == FullPath(Context.FrameworkPath);
		}

		private static string FullPath(string path)
		{
			var fullPath = Path.GetFullPath(path);
			return fullPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
		}
	}
}
