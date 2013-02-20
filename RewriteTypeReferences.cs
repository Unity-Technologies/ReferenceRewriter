using System;
using System.Linq;
using Mono.Cecil;

namespace Unity.ReferenceRewriter
{
	class RewriteTypeReferences : RewriteStep, IReferenceVisitor
	{
		private readonly Func<string, string> _supportNamespaceMapper;

		public RewriteTypeReferences(Func<string, string> supportNamespaceMapper)
		{
			_supportNamespaceMapper = supportNamespaceMapper;
		}

		protected override void Run()
		{
			ReferenceDispatcher.DispatchOn(Context.TargetModule, this);
		}

		private AssemblyNameReference SupportAssemblyReference()
		{
			var supportName = Context.SupportModule.Assembly.Name;
			var reference = Context.TargetModule.AssemblyReferences.SingleOrDefault(r => r.FullName == supportName.FullName);
			if (reference != null)
				return reference;

			reference = new AssemblyNameReference(supportName.Name, supportName.Version) { PublicKeyToken = supportName.PublicKeyToken };
			return reference;
		}

		private bool AltAssemblyReference(string @namespace, out AssemblyNameReference[] names)
		{
			ModuleDefinition[] modules;

			if (!Context.AltModules.TryGetValue(@namespace, out modules))
			{
				names = null;
				return false;
			}

			names = new AssemblyNameReference[modules.Length];

			for (var i = 0; i < modules.Length; ++i)
			{
				var name = modules[i].Assembly.Name;
				var reference = Context.TargetModule.AssemblyReferences.SingleOrDefault(r => r.FullName == name.FullName);
				if (reference == null)
					reference = new AssemblyNameReference(name.Name, name.Version) { PublicKeyToken = name.PublicKeyToken };
				names[i] = reference;
			}

			return true;
		}

		public void Visit(TypeReference type)
		{
			if (type.IsNested)
			{
				Visit(type.DeclaringType);
				return;
			}

			if (TryToResolveInSupport(type))
				return;

			if (type.Resolve() != null)
				return;

			if (TryToResolveInAlt(type))
				return;

			Console.WriteLine("Error: type `{0}` doesn't exist in target framework.", type.FullName);
		}

		private bool TryToResolveInSupport(TypeReference type)
		{
			var originalScope = type.Scope;
			var originalNamespace = type.Namespace;

			var support = SupportAssemblyReference();

			type.Scope = support;
			type.Namespace = _supportNamespaceMapper(type.Namespace);

			var resolved = type.Resolve();
			if (resolved != null)
			{
				Context.RewriteTarget = true;
				AddSupportReferenceIfNeeded(support);
				return true;
			}

			type.Scope = originalScope;
			type.Namespace = originalNamespace;
			return false;
		}

		private bool TryToResolveInAlt(TypeReference type)
		{
			AssemblyNameReference[] names;

			if (!AltAssemblyReference(type.Namespace, out names))
				return false;

			var originalScope = type.Scope;

			foreach (var name in names)
			{
				type.Scope = name;

				var resolved = type.Resolve();
				if (resolved != null)
				{
					Context.RewriteTarget = true;
					AddSupportReferenceIfNeeded(name);
					return true;
				}
			}

			type.Scope = originalScope;
			return false;
		}

		private void AddSupportReferenceIfNeeded(AssemblyNameReference support)
		{
			if (Context.TargetModule.AssemblyReferences.Any(r => r.FullName == support.FullName))
				return;

			Context.TargetModule.AssemblyReferences.Add(support);
		}

		public void Visit(FieldReference field)
		{
			if (field.Resolve() != null)
				return;

			Console.WriteLine("Error: field `{0}` doesn't exist in target framework.", field);
		}

		public void Visit(MethodReference method)
		{
			if (method.Resolve() != null)
				return;

			Console.WriteLine("Error: method `{0}` doesn't exist in target framework.", method);
		}
	}
}