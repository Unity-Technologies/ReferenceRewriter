using Mono.Cecil;

namespace Unity.ReferenceRewriter
{
	class RewriteMethodSpecMemberRefs : RewriteStep, IMethodDefinitionVisitor
	{
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
				var genericInstanceMethod = instruction.Operand as GenericInstanceMethod;

				if (genericInstanceMethod != null)
				{
					var elementMethod = genericInstanceMethod.ElementMethod;

					if (elementMethod.MetadataToken.TokenType == TokenType.MemberRef)
					{
						var elementMethodDefinition = elementMethod.Resolve();

						if (elementMethodDefinition.DeclaringType.Module == method.DeclaringType.Module)
						{
							var genericInstanceMethodFixed = new GenericInstanceMethod(elementMethodDefinition);
							foreach (var argument in genericInstanceMethod.GenericArguments)
								genericInstanceMethodFixed.GenericArguments.Add(argument);
							instruction.Operand = genericInstanceMethodFixed;
						}
					}
				}
			}
		}
	}
}
