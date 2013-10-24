using Mono.Cecil;

namespace Unity.ReferenceRewriter
{
	class MethodDefinitionDispatcher
	{
		private readonly ModuleDefinition _module;
		private readonly IMethodDefinitionVisitor _visitor;

		public static void DispatchOn(ModuleDefinition module, IMethodDefinitionVisitor visitor)
		{
			new MethodDefinitionDispatcher(module, visitor).Dispatch();
		}

		private MethodDefinitionDispatcher(ModuleDefinition module, IMethodDefinitionVisitor visitor)
		{
			_module = module;
			_visitor = visitor;
		}

		private void Dispatch()
		{
			foreach (var type in _module.Types)
				Dispatch(type);
		}

		private void Dispatch(TypeDefinition type)
		{
			foreach (var nestedType in type.NestedTypes)
				Dispatch(nestedType);

			foreach (var method in type.Methods)
				Visit(method);

			foreach (var property in type.Properties)
			{
				Visit(property.GetMethod);
				Visit(property.SetMethod);
			}

			foreach (var @event in type.Events)
			{
				Visit(@event.AddMethod);
				Visit(@event.InvokeMethod);
				Visit(@event.RemoveMethod);
			}
		}

		private void Visit(MethodDefinition method)
		{
			if (method == null)
				return;

			_visitor.Visit(method);
		}
	}
}
