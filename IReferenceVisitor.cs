using Mono.Cecil;
using Mono.Cecil.Cil;

namespace Unity.ReferenceRewriter
{
	interface IReferenceVisitor
	{
		void Visit(TypeReference type);
		void Visit(FieldReference field);
		void Visit(MethodReference method);

		bool MethodChanged { get; }
		MethodReference ParamsMethod { get; }

		void RewriteObjectListToParamsCall(MethodBody methodBody, int instructionIndex);
	}
}
