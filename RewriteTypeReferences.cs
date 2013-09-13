using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Reflection;
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
				var reference = Context.TargetModule.AssemblyReferences.FirstOrDefault(r => r.FullName == name.FullName);
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
			if (method.Resolve() != null || method.DeclaringType.IsArray || ResolveManually(method) != null)
				return;

			Console.WriteLine("Error: method `{0}` doesn't exist in target framework.", method);
		}

		private MethodDefinition ResolveManually(MethodReference method)
		{
			var metadataResolver = method.Module.MetadataResolver;
			var type = metadataResolver.Resolve(method.DeclaringType);

			if (type == null || !type.HasMethods)
			{
				return null;
			}

			method = method.GetElementMethod();

			while (type != null)
			{
				var methodDefinition = GetMethodDefinition(type.Methods, method);

				if (methodDefinition != null)
				{
					return methodDefinition;
				}

				if (type.BaseType == null)
				{
					return null;
				}
				type = metadataResolver.Resolve(type.BaseType);
			}

			return null;
		}

		private MethodDefinition GetMethodDefinition(IEnumerable<MethodDefinition> methods, MethodReference reference)
		{
			foreach (var methodDefinition in methods)
			{
				bool isSameName = methodDefinition.Name == reference.Name || MethodAliases.AreAliases(methodDefinition.Name, reference.Name);
				bool isSameGenericParameters = methodDefinition.HasGenericParameters == reference.HasGenericParameters &&
												(!methodDefinition.HasGenericParameters || methodDefinition.GenericParameters.Count == reference.GenericParameters.Count);

				bool isSameReturnType = AreSame(methodDefinition.ReturnType, reference.ReturnType);

				if (isSameName && isSameGenericParameters && isSameReturnType && methodDefinition.HasParameters == reference.HasParameters)
				{
					if (!methodDefinition.HasParameters && !reference.HasParameters)
					{
						return methodDefinition;
					}

					if (AreSame(methodDefinition.Parameters, reference.Parameters))
					{
						return methodDefinition;
					}
				}
			}

			return null;
		}

		private bool AreSame(TypeReference a, TypeReference b)
		{
			var assembly = System.Reflection.Assembly.GetAssembly(typeof (MetadataResolver));
			var type = assembly.GetType("Mono.Cecil.MetadataResolver");
			var compareMethod = type.GetMethod("AreSame", BindingFlags.Static | BindingFlags.NonPublic, null, new Type[] {typeof (TypeReference), typeof (TypeReference)}, null);

			var areSameAccordingToCecil = (bool)compareMethod.Invoke(null, new object[] {a, b});
			bool areSameAccordingToTypeAliases = TypeAliases.AreAliases(a.FullName, b.FullName);
			
			return areSameAccordingToCecil || areSameAccordingToTypeAliases;
		}

		private bool AreSame(Mono.Collections.Generic.Collection<ParameterDefinition> a, Mono.Collections.Generic.Collection<ParameterDefinition> b)
		{
			if (a.Count != b.Count)
			{
				return false;
			}

			for (int i = 0; i < a.Count; i++)
			{
				if (!AreSame(a[i].ParameterType, b[i].ParameterType))
				{
					return false;
				}
			}
			return true;
		}
	}
}