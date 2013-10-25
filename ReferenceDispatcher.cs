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
			Visit(type.BaseType, type.FullName);

			DispatchGenericParameters(type, type.FullName);
			DispatchInterfaces(type, type.FullName);
			DispatchAttributes(type, type.FullName);
			DispatchFields(type, type.FullName);
			DispatchProperties(type, type.FullName);
			DispatchEvents(type, type.FullName);
			DispatchMethods(type);
		}

		private void DispatchGenericParameters(IGenericParameterProvider provider, string referencingEntityName)
		{
			foreach (var parameter in provider.GenericParameters)
			{
				DispatchAttributes(parameter, referencingEntityName);

				foreach (var constraint in parameter.Constraints)
					Visit(constraint, referencingEntityName);
			}
		}

		private void DispatchMethods(TypeDefinition type)
		{
			foreach (var method in type.Methods)
				DispatchMethod(method);
		}

		private void DispatchMethod(MethodDefinition method)
		{
			Visit(method.ReturnType, method.FullName);
			DispatchAttributes(method.MethodReturnType, method.FullName);
			DispatchGenericParameters(method, method.FullName);

			foreach (var parameter in method.Parameters)
			{
				Visit(parameter.ParameterType, method.FullName);
				DispatchAttributes(parameter, method.FullName);
			}

			foreach (var ov in method.Overrides)
				Visit(ov, method.FullName);

			if (method.HasBody)
				DispatchMethodBody(method.Body);
		}

		private void DispatchMethodBody(MethodBody body)
		{
			foreach (var variable in body.Variables)
				Visit(variable.VariableType, body.Method.FullName);

			for (int i = 0; i < body.Instructions.Count; i++)
			{
				var instruction = body.Instructions[i];
				var field = instruction.Operand as FieldReference;
				if (field != null)
				{
					Visit(field, body.Method.FullName);
					continue;
				}

				var method = instruction.Operand as MethodReference;
				if (method != null && !IsUnityScriptMethod(method))
				{
					Visit(method, body.Method.FullName);
					if (_visitor.MethodChanged)
					{
						_visitor.RewriteObjectListToParamsCall(body, i);
					}

					continue;
				}

				var type = instruction.Operand as TypeReference;
				if (type != null)
					Visit(type, body.Method.FullName);
			}
		}

		private void DispatchGenericArguments(IGenericInstance genericInstance, string referencingEntityName)
		{
			foreach (var argument in genericInstance.GenericArguments)
				Visit(argument, referencingEntityName);
		}

		private void DispatchInterfaces(TypeDefinition type, string referencingEntityName)
		{
			foreach (var iface in type.Interfaces)
				Visit(iface, referencingEntityName);
		}

		private void DispatchEvents(TypeDefinition type, string referencingEntityName)
		{
			foreach (var evt in type.Events)
			{
				Visit(evt.EventType, referencingEntityName);
				DispatchAttributes(evt, referencingEntityName);
			}
		}

		private void DispatchProperties(TypeDefinition type, string referencingEntityName)
		{
			foreach (var property in type.Properties)
			{
				Visit(property.PropertyType, referencingEntityName);
				DispatchAttributes(property, referencingEntityName);
			}
		}

		private void DispatchFields(TypeDefinition type, string referencingEntityName)
		{
			foreach (var field in type.Fields)
			{
				Visit(field.FieldType, referencingEntityName);
				DispatchAttributes(field, referencingEntityName);
			}
		}

		private void DispatchAttributes(ICustomAttributeProvider provider, string referencingEntityName)
		{
			if (!provider.HasCustomAttributes)
				return;

			foreach (var attribute in provider.CustomAttributes)
				DispatchAttribute(attribute, referencingEntityName);
		}

		private void DispatchAttribute(CustomAttribute attribute, string referencingEntityName)
		{
			Visit(attribute.Constructor, referencingEntityName);

			foreach (var argument in attribute.ConstructorArguments)
				DispatchCustomAttributeArgument(argument, referencingEntityName);

			foreach (var namedArgument in attribute.Properties)
				DispatchCustomAttributeArgument(namedArgument.Argument, referencingEntityName);

			foreach (var namedArgument in attribute.Fields)
				DispatchCustomAttributeArgument(namedArgument.Argument, referencingEntityName);
		}

		private void DispatchCustomAttributeArgument(CustomAttributeArgument argument, string referencingEntityName)
		{
			var reference = argument.Value as TypeReference;
			if (reference == null)
				return;

			Visit(reference, referencingEntityName);
		}

		private void Visit(MethodReference method, string referencingEntityName)
		{
			if (method == null)
				return;

			var genericInstance = method as GenericInstanceMethod;
			if (genericInstance != null)
				DispatchGenericArguments(genericInstance, referencingEntityName);

			Visit(method.DeclaringType, referencingEntityName);
			Visit(method.ReturnType, referencingEntityName);

			foreach (var parameter in method.Parameters)
				Visit(parameter.ParameterType, referencingEntityName);

			_visitor.Visit(method, referencingEntityName);
		}

		private void Visit(FieldReference field, string referencingEntityName)
		{
			if (field == null)
				return;

			Visit(field.DeclaringType, referencingEntityName);
			Visit(field.FieldType, referencingEntityName);

			if (field.DeclaringType.Module == _module)
				return;

			_visitor.Visit(field, referencingEntityName);
		}

		private void Visit(TypeReference type, string referencingEntityName)
		{
			if (type == null)
				return;

			if (type.Scope == _module)
				return;

			if (type.GetElementType().IsGenericParameter)
				return;

			var genericInstance = type as GenericInstanceType;
			if (genericInstance != null)
				DispatchGenericArguments(genericInstance, referencingEntityName);

			_visitor.Visit(type.GetElementType(), referencingEntityName);
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
