using Mono.Cecil;

namespace Unity.ReferenceRewriter
{
	interface IMethodDefinitionVisitor
	{
		void Visit(MethodDefinition method);
	}
}
