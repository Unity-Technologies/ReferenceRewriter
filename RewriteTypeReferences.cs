using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Reflection;
using Mono.Cecil;
using Mono.Cecil.Cil;
using MethodBody = Mono.Cecil.Cil.MethodBody;
using TypeAttributes = Mono.Cecil.TypeAttributes;

namespace Unity.ReferenceRewriter
{
	class RewriteTypeReferences : RewriteStep, IReferenceVisitor
	{
		private readonly Func<string, string> _supportNamespaceMapper;
		public bool MethodChanged { get; private set; }
		public MethodReference ParamsMethod { get; private set; }

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
			MethodChanged = false;
			ParamsMethod = null;

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

					if (ArgsMatchParamsList(methodDefinition.Parameters, reference.Parameters))
					{
						ParamsMethod = Context.TargetModule.Import(methodDefinition);
						MethodChanged = true;
						Context.RewriteTarget = true;
						return methodDefinition;
					}
				}
			}

			return null;
		}

		// The idea behind this method is to change method call from Method(obj1, obj2, obj3) to Method(param Object[] objs)
		// To do that, we need to first pack all the objects to an array, then push the array onto the stack before calling the method.
		// Creating an array is achieved by pushing number of elements on the stack, and using newarr instruction.
		// Lastly, we need to pop the array back from the stack and store it in a local variable.
		//
		// Putting object to array is done by:
		// Loading array to the stack, loading index to the stack, loading the reference to value on the stack
		// and using stelem.ref to insert it to the array. stelem.ref instruction pops all three inserted values
		// from the stack
		//
		// For example, we need to convert something like this:
		//
		// IL_0000: newobj instance void [mscorlib]System.Text.StringBuilder::.ctor()
		// IL_0005: stloc.0
		// IL_0006: ldloc.0
		// IL_0007: ldstr "{0}, {1}"
		// IL_000c: ldstr "one"
		// IL_0011: ldstr "two"
		// IL_0016: callvirt instance class [mscorlib]System.Text.StringBuilder [mscorlib]System.Text.StringBuilder::AppendFormat(string, object[])
		// IL_001b: pop
		//
		// To this:
		//
		// IL_0000: newobj instance void [mscorlib]System.Text.StringBuilder::.ctor()
		// IL_0005: stloc.0
		// IL_0006: ldloc.0
		// IL_0007: ldstr "{0}, {1}"
		// IL_000c: ldc.i4.2
		// IL_000d: newarr [mscorlib]System.Object
		// IL_0012: stloc.1
		// IL_0013: ldloc.1
		// IL_0014: ldc.i4.0
		// IL_0015: ldstr "one"
		// IL_001a: stelem.ref
		// IL_001b: ldloc.1
		// IL_001c: ldc.i4.1
		// IL_001d: ldstr "two"
		// IL_0022: stelem.ref
		// IL_0023: ldloc.1
		// IL_0024: callvirt instance class [mscorlib]System.Text.StringBuilder [mscorlib]System.Text.StringBuilder::AppendFormat(string, object[])
		// IL_0029: pop
		//
		public void RewriteObjectListToParamsCall(MethodBody methodBody, int instructionIndex)
		{
			var variableInfo = new VariableDefinition(ParamsMethod.Parameters.Last().ParameterType);
			var arrayIndex = methodBody.Variables.Count;
			methodBody.Variables.Add(variableInfo);

			var instruction = methodBody.Instructions[instructionIndex];
			int numberOfObjects = (instruction.Operand as MethodReference).Parameters.Count - ParamsMethod.Parameters.Count + 1;
			instructionIndex -= numberOfObjects; // We need to insert our code before objects are loaded onto the stack

			// Push number of objects to the stack
			var instr = Instruction.Create(OpCodes.Ldc_I4, numberOfObjects);
			methodBody.Instructions.Insert(instructionIndex, instr);
			instructionIndex++;

			// Create a new array
			instr = Instruction.Create(OpCodes.Newarr, ParamsMethod.Parameters.Last().ParameterType.GetElementType());
			methodBody.Instructions.Insert(instructionIndex, instr);
			instructionIndex++;

			// Store the newly created array to first variable slot
			instr = Instruction.Create(OpCodes.Stloc, variableInfo);
			methodBody.Instructions.Insert(instructionIndex, instr);
			instructionIndex++;
			
			// Load every object to the array
			for (int i = 0; i < numberOfObjects; i++)
			{
				// Load reference to the array to the stack
				instr = Instruction.Create(OpCodes.Ldloc, variableInfo);
				methodBody.Instructions.Insert(instructionIndex, instr);
				instructionIndex++;

				// Load object index to the stack
				instr = Instruction.Create(OpCodes.Ldc_I4, i);
				methodBody.Instructions.Insert(instructionIndex, instr);
				instructionIndex += 2;		// The next instruction is loading the object to the stack, we skip it because it's already there

				// Load reference to object to the stack
				instr = Instruction.Create(OpCodes.Stelem_Ref);
				methodBody.Instructions.Insert(instructionIndex, instr);
				instructionIndex++;
			}

			// Load reference to the array to the stack
			instr = Instruction.Create(OpCodes.Ldloc, variableInfo);
			methodBody.Instructions.Insert(instructionIndex, instr);

			instruction.Operand = ParamsMethod;
			ParamsMethod = null;
			MethodChanged = false;
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

		private bool ArgsMatchParamsList(Mono.Collections.Generic.Collection<ParameterDefinition> a, Mono.Collections.Generic.Collection<ParameterDefinition> b)
		{
			if (a.Last().CustomAttributes.Any(x => x.AttributeType.FullName != "System.ParamArrayAttribute"))
			{
				if (b.Last().CustomAttributes.Any(x => x.AttributeType.FullName != "System.ParamArrayAttribute"))
				{
					return false;
				}
				else
				{
					var temp = a;
					a = b;
					b = temp;
				}
			}

			int numberOfMatches = 0;
			while (numberOfMatches < a.Count - 1 && numberOfMatches < b.Count)
			{
				if (AreSame(a[numberOfMatches].ParameterType, b[numberOfMatches].ParameterType))
				{
					numberOfMatches++;
				}
				else
				{
					break;
				}
			}

			if (numberOfMatches != a.Count - 1)
			{
				return false;
			}

			var paramsArg = a.Last().ParameterType.GetElementType();
			for (int i = a.Count - 1; i < b.Count; i++)
			{
				bool matches = false;
				var type = b[i].ParameterType;
				while (type != null && !matches)
				{
					if (AreSame(type, paramsArg))
					{
						matches = true;
					}
					else
					{
						type = type.Resolve().BaseType;
					}
				}

				if (!matches)
				{
					return false;
				}
			}

			return true;
		}
	}
}