using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Reflection;
using Mono.Cecil;
using Mono.Cecil.Cil;
using Mono.Cecil.Rocks;
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

		public void Visit(TypeReference type, string referencingEntityName)
		{
			if (type.IsNested)
			{
				Visit(type.DeclaringType, referencingEntityName);
				return;
			}

			if (TryToResolveInSupport(type))
				return;

			if (type.Resolve() != null)
				return;

			if (TryToResolveInAlt(type))
				return;

			Console.WriteLine("Error: type `{0}` doesn't exist in target framework. It is referenced from {1} at {2}.", 
				type.FullName, type.Module.Name, referencingEntityName);
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

		public void Visit(FieldReference field, string referencingEntityName)
		{
			if (field.Resolve() != null)
				return;

			Console.WriteLine("Error: field `{0}` doesn't exist in target framework. It is referenced from {1} at {2}.", 
				field, field.Module.Name, referencingEntityName);
		}

		public void Visit(MethodReference method, string referencingEntityName)
		{
			MethodChanged = false;
			ParamsMethod = null;

			if (method.Resolve() != null || method.DeclaringType.IsArray || ResolveManually(method) != null)
				return;

			Console.WriteLine("Error: method `{0}` doesn't exist in target framework. It is referenced from {1} at {2}.",
				method, method.Module.Name, referencingEntityName);
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

			Func<IEnumerable<MethodDefinition>, MethodReference, MethodDefinition>[] finderMethods = 
				{GetMethodDefinition, GetCompatibleMethodDefinition};

			for (int i = 0; i < finderMethods.Length; i++)
			{
				while (type != null)
				{
					var methodDefinition = finderMethods[i](type.Methods, method);

					if (methodDefinition != null)
					{
						return methodDefinition;
					}

					if (type.BaseType == null)
					{
						break;
					}
					type = metadataResolver.Resolve(type.BaseType);
				}
				type = metadataResolver.Resolve(method.DeclaringType);
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
				}
			}

			return null;
		}

		private MethodDefinition GetCompatibleMethodDefinition(IEnumerable<MethodDefinition> methods, MethodReference reference)
		{
			foreach (var methodDefinition in methods)
			{
				bool isSameName = methodDefinition.Name == reference.Name || MethodAliases.AreAliases(methodDefinition.Name, reference.Name);
				bool isSameGenericParameters = methodDefinition.HasGenericParameters == reference.HasGenericParameters &&
												(!methodDefinition.HasGenericParameters || methodDefinition.GenericParameters.Count == reference.GenericParameters.Count);

				bool isSameReturnType = AreSame(methodDefinition.ReturnType, reference.ReturnType);

				if (isSameName && isSameGenericParameters && isSameReturnType)
				{
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
		// IL_0007: ldstr "{0}, {1}\r\n"
		// IL_000c: ldstr "one"
		// IL_0011: ldstr "two"
		// IL_0016: ldc.i4 2
		// IL_001b: newarr [mscorlib]System.Object
		// IL_0020: stloc 1
		// IL_0024: stloc 2
		// IL_0028: stloc 3
		// IL_002c: ldloc 1
		// IL_0030: ldc.i4 0
		// IL_0035: ldloc 2
		// IL_0039: stelem.ref
		// IL_003a: ldloc 1
		// IL_003e: ldc.i4 1
		// IL_0043: ldloc 3
		// IL_0047: stelem.ref
		// IL_0048: ldloc 1
		// IL_004c: callvirt instance class [mscorlib]System.Text.StringBuilder [mscorlib]System.Text.StringBuilder::AppendFormat(string, object[])
		// IL_0051: pop
		//
		// Basically, just before the invalid function call we pack all the arguments (that are already on the stack, 
		// ready to be passed to invalid function) to a newly created array and call a valid function instead passing 
		// the array as argument
		//
		public void RewriteObjectListToParamsCall(MethodBody methodBody, int instructionIndex)
		{
			methodBody.SimplifyMacros();
			
			var parameterType = ParamsMethod.Parameters.Last().ParameterType;
			var arrayInfo = new VariableDefinition(parameterType);
			methodBody.InitLocals = true;
			methodBody.Variables.Add(arrayInfo);

			var instruction = methodBody.Instructions[instructionIndex];
			int numberOfObjects = (instruction.Operand as MethodReference).Parameters.Count - ParamsMethod.Parameters.Count + 1;
			
			// Firstly, let's create the object array

			// Push number of objects to the stack
			var instr = Instruction.Create(OpCodes.Ldc_I4, numberOfObjects);
			var firstInstruction = instr;
			methodBody.Instructions.Insert(instructionIndex, instr);
			instructionIndex++;

			// Create a new array
			instr = Instruction.Create(OpCodes.Newarr, ParamsMethod.Parameters.Last().ParameterType.GetElementType());
			methodBody.Instructions.Insert(instructionIndex, instr);
			instructionIndex++;

			// Store the newly created array to first variable slot
			instr = Instruction.Create(OpCodes.Stloc, arrayInfo);
			methodBody.Instructions.Insert(instructionIndex, instr);
			instructionIndex++;

			// At this point, all the references to objects that need to be packed to the array are on the stack.
			var objectInfo = new VariableDefinition[numberOfObjects];
			for (int i = 0; i < numberOfObjects; i++)
			{
				objectInfo[i] = new VariableDefinition(parameterType.GetElementType());
				methodBody.Variables.Add(objectInfo[i]);

				instr = Instruction.Create(OpCodes.Stloc, objectInfo[i]);
				methodBody.Instructions.Insert(instructionIndex, instr);
				instructionIndex++;
			}

			// Now that we got the references to objects in local variables rather than the stack, it's high time we insert them to the array
			// We need to load them in backwards order, because the last argument was taken off the stack first.
			for (int i = 0; i < numberOfObjects; i++)
			{
				// Load reference to the array to the stack
				instr = Instruction.Create(OpCodes.Ldloc, arrayInfo);
				methodBody.Instructions.Insert(instructionIndex, instr);
				instructionIndex++;

				// Load object index to the stack
				instr = Instruction.Create(OpCodes.Ldc_I4, numberOfObjects - i - 1);
				methodBody.Instructions.Insert(instructionIndex, instr);
				instructionIndex++;

				// Load reference to the object to the stack
				instr = Instruction.Create(OpCodes.Ldloc, objectInfo[i]);
				methodBody.Instructions.Insert(instructionIndex, instr);
				instructionIndex++;
				
				// Insert the object to the array
				if (parameterType.GetElementType().IsValueType)
				{
					instr = Instruction.Create(OpCodes.Stelem_Any, parameterType.GetElementType());
					methodBody.Instructions.Insert(instructionIndex, instr);
					instructionIndex++;
				}
				else
				{
					instr = Instruction.Create(OpCodes.Stelem_Ref);
					methodBody.Instructions.Insert(instructionIndex, instr);
					instructionIndex++;
				}
			}

			// Finally, load reference to the array to the stack so it can be inserted to the array
			instr = Instruction.Create(OpCodes.Ldloc, arrayInfo);
			methodBody.Instructions.Insert(instructionIndex, instr);

			instruction.Operand = ParamsMethod;
			ParamsMethod = null;
			MethodChanged = false;
			methodBody.OptimizeMacros();		// This, together with SimplifyMacros() before touching IL code, recalculates IL instruction offsets 

			// If any other instruction is referencing the illegal call, we need to rewrite it to reference beginning of object packing instead
			// For example, there's a branch jump to call the method. We need to pack the objects anyway before calling the method
			foreach (var changeableInstruction in methodBody.Instructions)
			{
				if (changeableInstruction.Operand is Instruction &&
					(changeableInstruction as Instruction).Operand == instruction)
				{
					changeableInstruction.Operand = firstInstruction;
				}
			}
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
			if (a.Count == 0 || a.Last().CustomAttributes.Any(x => x.AttributeType.FullName != "System.ParamArrayAttribute"))
			{
				if (b.Count == 0 || b.Last().CustomAttributes.Any(x => x.AttributeType.FullName != "System.ParamArrayAttribute"))
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