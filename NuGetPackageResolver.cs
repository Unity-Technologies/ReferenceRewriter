using System;
using System.Collections.Generic;
using System.IO;
using MiniJSON;

namespace Unity.ReferenceRewriter
{
	internal sealed class NuGetPackageResolver
	{
		#region Properties

		public string PackagesDirectory
		{
			get;
			set;
		}

		public string ProjectLockFile
		{
			get;
			set;
		}

		public string TargetMoniker
		{
			get;
			set;
		}

		public string[] ResolvedReferences
		{
			get;
			private set;
		}

		#endregion

		public NuGetPackageResolver()
		{
			TargetMoniker = "UAP,Version=v10.0";
		}

		public void Resolve()
		{
			var text = File.ReadAllText(ProjectLockFile);
			var lockFile = (Dictionary<string, object>)Json.Deserialize(text);
			var targets = (Dictionary<string, object>)lockFile["targets"];
			var target = (Dictionary<string, object>)targets[TargetMoniker];

			var references = new List<string>();
			var packagesPath = GetPackagesPath().ConvertToWindowsPath();

			foreach (var packagePair in target)
			{
				var package = (Dictionary<string, object>)packagePair.Value;

				object compileObject;
				if (!package.TryGetValue("compile", out compileObject))
					continue;
				var compile = (Dictionary<string, object>)compileObject;

				var parts = packagePair.Key.Split('/');
				var packageId = parts[0];
				var packageVersion = parts[1];
				var packagePath = Path.Combine(packagesPath, packageId, packageVersion);
				if (!Directory.Exists(packagePath))
					throw new Exception(string.Format("Package directory not found: \"{0}\".", packagePath));

				foreach (var name in compile.Keys)
				{
					if (string.Equals(Path.GetFileName(name), "_._", StringComparison.InvariantCultureIgnoreCase))
						continue;
					var reference = Path.Combine(packagePath, name.ConvertToWindowsPath());
					if (!File.Exists(reference))
						throw new Exception(string.Format("Reference not found: \"{0}\".", reference));
					references.Add(reference);
				}

				if (package.ContainsKey("frameworkAssemblies"))
					throw new NotImplementedException("Support for \"frameworkAssemblies\" property has not been implemented yet.");
			}

			ResolvedReferences = references.ToArray();
		}

		private string GetPackagesPath()
		{
			var value = PackagesDirectory;
			if (!string.IsNullOrEmpty(value))
				return value;
			value = Environment.GetEnvironmentVariable("NUGET_PACKAGES");
			if (!string.IsNullOrEmpty(value))
				return value;
			var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
			return Path.Combine(userProfile, ".nuget", "packages");
		}
	}
}
