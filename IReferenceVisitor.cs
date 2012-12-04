using Mono.Cecil;

namespace Unity.ReferenceRewriter
{
	interface IReferenceVisitor
	{
		void Visit(TypeReference type);
		void Visit(FieldReference field);
		void Visit(MethodReference method);
	}
}
