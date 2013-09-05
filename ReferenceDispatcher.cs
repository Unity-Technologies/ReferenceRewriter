using Mono.Cecil;
using Mono.Cecil.Cil;
using System;

namespace Unity.ReferenceRewriter
{
	class ReferenceDispatcher
	{
		private readonly ModuleDefinition _module;
		private readonly IReferenceVisitor _visitor;

		private ReferenceDispatcher(ModuleDefinition module, IReferenceVisitor visitor)
		{
			_module = module;
			_visitor = visitor;
		}

		private void Dispatch()
		{
			foreach (var type in _module.GetTypes())
				Dispatch(type);
		}

		private void Dispatch(TypeDefinition type)
		{
			Visit(type.BaseType);

			DispatchGenericParameters(type);
			DispatchInterfaces(type);
			DispatchAttributes(type);
			DispatchFields(type);
			DispatchProperties(type);
			DispatchEvents(type);
			DispatchMethods(type);
		}

		private void DispatchGenericParameters(IGenericParameterProvider provider)
		{
			foreach (var parameter in provider.GenericParameters)
			{
				DispatchAttributes(parameter);

				foreach (var constraint in parameter.Constraints)
					Visit(constraint);
			}
		}

		private void DispatchMethods(TypeDefinition type)
		{
			foreach (var method in type.Methods)
				DispatchMethod(method);
		}

		private void DispatchMethod(MethodDefinition method)
		{
			Visit(method.ReturnType);
			DispatchAttributes(method.MethodReturnType);
			DispatchGenericParameters(method);

			foreach (var parameter in method.Parameters)
			{
				Visit(parameter.ParameterType);
				DispatchAttributes(parameter);
			}

			foreach (var ov in method.Overrides)
				Visit(ov);

			if (method.HasBody)
				DispatchMethodBody(method.Body);
		}

		private void DispatchMethodBody(MethodBody body)
		{
			foreach (var variable in body.Variables)
				Visit(variable.VariableType);

			foreach (var instruction in body.Instructions)
			{
				var field = instruction.Operand as FieldReference;
				if (field != null)
				{
					Visit(field);
					continue;
				}

				var method = instruction.Operand as MethodReference;
				if (method != null && !IsUnityScriptMethod(method))
				{
					Visit(method);
					continue;
				}

				var type = instruction.Operand as TypeReference;
				if (type != null)
					Visit(type);
			}
		}

		private void DispatchGenericArguments(IGenericInstance genericInstance)
		{
			foreach (var argument in genericInstance.GenericArguments)
				Visit(argument);
		}

		private void DispatchInterfaces(TypeDefinition type)
		{
			foreach (var iface in type.Interfaces)
				Visit(iface);
		}

		private void DispatchEvents(TypeDefinition type)
		{
			foreach (var evt in type.Events)
			{
				Visit(evt.EventType);
				DispatchAttributes(evt);
			}
		}

		private void DispatchProperties(TypeDefinition type)
		{
			foreach (var property in type.Properties)
			{
				Visit(property.PropertyType);
				DispatchAttributes(property);
			}
		}

		private void DispatchFields(TypeDefinition type)
		{
			foreach (var field in type.Fields)
			{
				Visit(field.FieldType);
				DispatchAttributes(field);
			}
		}

		private void DispatchAttributes(ICustomAttributeProvider provider)
		{
			if (!provider.HasCustomAttributes)
				return;

			foreach (var attribute in provider.CustomAttributes)
				DispatchAttribute(attribute);
		}

		private void DispatchAttribute(CustomAttribute attribute)
		{
			Visit(attribute.Constructor);

			foreach (var argument in attribute.ConstructorArguments)
				DispatchCustomAttributeArgument(argument);

			foreach (var namedArgument in attribute.Properties)
				DispatchCustomAttributeArgument(namedArgument.Argument);

			foreach (var namedArgument in attribute.Fields)
				DispatchCustomAttributeArgument(namedArgument.Argument);
		}

		private void DispatchCustomAttributeArgument(CustomAttributeArgument argument)
		{
			var reference = argument.Value as TypeReference;
			if (reference == null)
				return;

			Visit(reference);
		}

		private void Visit(MethodReference method)
		{
			if (method == null)
				return;

			var genericInstance = method as GenericInstanceMethod;
			if (genericInstance != null)
				DispatchGenericArguments(genericInstance);

			Visit(method.DeclaringType);
			Visit(method.ReturnType);

			foreach (var parameter in method.Parameters)
				Visit(parameter.ParameterType);

			_visitor.Visit(method);
		}

		private void Visit(FieldReference field)
		{
			if (field == null)
				return;

			Visit(field.DeclaringType);
			Visit(field.FieldType);

			if (field.DeclaringType.Module == _module)
				return;

			_visitor.Visit(field);
		}

		private void Visit(TypeReference type)
		{
			if (type == null)
				return;

			if (type.Scope == _module)
				return;

			if (type.GetElementType().IsGenericParameter)
				return;

			var genericInstance = type as GenericInstanceType;
			if (genericInstance != null)
				DispatchGenericArguments(genericInstance);

			_visitor.Visit(type.GetElementType());
		}

		public static void DispatchOn(ModuleDefinition module, IReferenceVisitor visitor)
		{
			new ReferenceDispatcher(module, visitor).Dispatch();
		}

        public static bool IsUnityScriptMethod(MethodReference method)
        {
            var ret = false;
            var type = method.DeclaringType.FullName;

            if (type.StartsWith("UnityScript.Lang"))
            {
                ret = true;
            }

            return ret;
        }
	}
}
