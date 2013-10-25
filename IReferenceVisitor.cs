using Mono.Cecil;
using Mono.Cecil.Cil;

namespace Unity.ReferenceRewriter
{
	interface IReferenceVisitor
	{
		void Visit(TypeReference type, string referencingEntityName);
		void Visit(FieldReference field, string referencingEntityName);
		void Visit(MethodReference method, string referencingEntityName);

		bool MethodChanged { get; }
		MethodReference ParamsMethod { get; }

		void RewriteObjectListToParamsCall(MethodBody methodBody, int instructionIndex);
	}
}
