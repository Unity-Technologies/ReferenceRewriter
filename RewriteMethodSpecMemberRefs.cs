using System;
using Mono.Cecil;

namespace Unity.ReferenceRewriter
{
	class RewriteMethodSpecMemberRefs : RewriteStep, IMethodDefinitionVisitor
	{
		private static string GetAssemblyName(IMetadataScope scope)
		{
			switch (scope.MetadataScopeType)
			{
				case MetadataScopeType.AssemblyNameReference:
					return ((AssemblyNameReference)scope).Name;
				case MetadataScopeType.ModuleDefinition:
					return ((ModuleDefinition)scope).Assembly.Name.Name;
				default:
					throw new NotSupportedException(string.Format("Metadata scope type {0} is not supported.", scope.MetadataScopeType));
			}
		}

		private static bool IsSameScope(IMetadataScope left, IMetadataScope right)
		{
			return string.Equals(GetAssemblyName(left), GetAssemblyName(right), StringComparison.Ordinal);
		}

		protected override void Run()
		{
			MethodDefinitionDispatcher.DispatchOn(Context.TargetModule, this);
		}

		public void Visit(MethodDefinition method)
		{
			if (!method.HasBody)
				return;

			foreach (var instruction in method.Body.Instructions)
			{
				var typeReference = instruction.Operand as TypeReference;
				if (typeReference != null)
				{
					if (typeReference.IsDefinition)
						continue;
					if (typeReference.IsArray || typeReference.IsGenericParameter || typeReference.IsGenericInstance)
						continue;
					if (IsSameScope(typeReference.Scope, method.DeclaringType.Scope))
					{
						instruction.Operand = typeReference.Resolve();
						this.Context.RewriteTarget = true;
					}
					continue;
				}

				var memberReference = instruction.Operand as MemberReference;
				if (memberReference != null)
				{
					if (memberReference.IsDefinition || memberReference.DeclaringType.IsGenericInstance || memberReference.DeclaringType.IsArray)
						continue;

					var methodReference = memberReference as MethodReference;
					if (methodReference != null)
					{
						var genericInstanceMethod = methodReference as GenericInstanceMethod;
						if (genericInstanceMethod != null)
						{
							var elementMethod = genericInstanceMethod.ElementMethod.Resolve();
							if (IsSameScope(elementMethod.DeclaringType.Scope, method.DeclaringType.Scope))
							{
								var genericInstanceMethodFixed = new GenericInstanceMethod(elementMethod);
								foreach (var argument in genericInstanceMethod.GenericArguments)
									genericInstanceMethodFixed.GenericArguments.Add(argument);
								instruction.Operand = genericInstanceMethodFixed;
								this.Context.RewriteTarget = true;
							}
						}
						else
						{
							if (IsSameScope(methodReference.DeclaringType.Scope, method.DeclaringType.Scope))
							{
								instruction.Operand = methodReference.Resolve();
								this.Context.RewriteTarget = true;
							}
						}
						continue;
					}
					

					var fieldReference = memberReference as FieldReference;
					if (fieldReference != null)
					{
						if (IsSameScope(fieldReference.DeclaringType.Scope, method.DeclaringType.Scope))
						{
							instruction.Operand = fieldReference.Resolve();
							this.Context.RewriteTarget = true;
						}
						continue;
					}

					continue;
				}
			}
		}
	}
}
