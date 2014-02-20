using Mono.Cecil;
using System.Linq;

namespace Unity.ReferenceRewriter
{
	/*
	 *	There's a bug in Mono compiler that makes this code throw a runtime exception when running under .NET:
	 * 
	 *	public class SomeBaseClass {}
	 *	public class SomeChildClass : SomeBaseClass {}
	 *	public delegate void ResourceReady<TType>(int id, TType resource) where TType : SomeBaseClass;
	 *	
	 *	public abstract class Base
	 *	{
	 *		public abstract IEnumerator GetResource<TType>(int count, ResourceReady<TType> resourceReadyCallback) where TType : SomeBaseClass;
	 *	}
	 *
	 *	public class Child : Base
	 *	{
	 *		public override IEnumerator GetResource<TType>(int count, ResourceReady<TType> resourceReadyCallback)
	 *		{
	 *			for (int i = 0; i < count; i++)
	 *			{
	 *				var item = default(TType);
	 *				resourceReadyCallback(i, item);
	 *				yield return item;
	 *			}
	 *		}
	 *	}
	 *	
	 *	public class Test
	 *	{
	 *		void Test()
	 *		{
	 *			SomeBaseClass item = new SomeChildClass();
	 *			var enumerator = item.GetResource<SomeChildClass>(3, Callback);
	 *			while (enumerator.MoveNext()) { }
	 *		}
	 *	}
	 *	
	 *	void Callback<SomeChildClass>(int id, SomeChildClass resource) { } 
	 *
	 *	The exception reads:
	 *
	 *	Exception: GenericArguments[0], 'TType', on 'ResourceReady`1[TType]' violates the constraint of type parameter 'TType'.
	 *	Compiling with Microsoft C# compiler, doesn't have the same behaviour and generates slightly different code.
	 *	This happens only when all conditions are met:
	 *
	 *	1. There's a base class with a method which has a generic parameter;
	 *	2. That generic parameter has a constraint;
	 *	3. The method returns an IEnumerator;
	 *	4. A method also takes a delegate which has the same generic parameter;
	 *	5. There's a child class that inherits from base class and overrides that method
	 *	
	 *	The bug itself happens on a compiler generated inner class of Child for Enumerator. It is a generic class, and should contain
	 *	constraint, but it doesn't:
	 *	
	 * 	.class nested private auto ansi sealed beforefieldinit '<GetResource>c__Iterator0`1'<TType>
	 *	extends [mscorlib]System.Object
	 *	implements [mscorlib]System.Collections.IEnumerator,
	 *	           class [mscorlib]System.Collections.Generic.IEnumerator`1<object>,
	 *	           [mscorlib]System.IDisposable
	 *	
	 *	The task of this class is find such methods and modify them to look like this:
	 *  
	 *	.class nested private auto ansi sealed beforefieldinit '<GetResource>c__Iterator0`1'<(SomeBaseClass) TType>
	 *	extends [mscorlib]System.Object
	 *	implements [mscorlib]System.Collections.IEnumerator,
	 *	           class [mscorlib]System.Collections.Generic.IEnumerator`1<object>,
	 *	           [mscorlib]System.IDisposable
	 */
	class EnumeratorGenericConstraintsFixer : IRewriteStep
	{
		RewriteContext _context;

		public void Execute(RewriteContext context)
		{
			_context = context;

			foreach (var type in _context.TargetModule.Types)
			{
				Fix(type);
			}
		}

		private void Fix(TypeDefinition type)
		{
			foreach (var method in type.Methods)
			{
				if (IsBroken(method))
				{
					Fix(method);
				}
			}
		}

		private bool IsBroken(MethodDefinition method)
		{
			if (!method.HasBody)
			{
				return false;
			}

			if (!method.HasGenericParameters)
			{
				return false;
			}

			if (!method.IsVirtual || method.IsAbstract)
			{
				return false;
			}

			if (!method.ReturnType.FullName.Equals("System.Collections.IEnumerator"))
			{
				return false;
			}

			if (method.GenericParameters.All(x => x.Constraints.Count == 0 && !x.HasDefaultConstructorConstraint
				&& !x.HasNotNullableValueTypeConstraint && !x.HasReferenceTypeConstraint))
			{
				return false;
			}

			var overridenMethod = GetOverridenMethod(method);

			if (overridenMethod == null)
			{
				return false;
			}

			return true;
		}

		private MethodDefinition GetOverridenMethod(MethodDefinition overridingMethod)
		{
			MethodDefinition overridenMethod = null;
			var declaringType = overridingMethod.DeclaringType;

			while (true)
			{
				if (declaringType.BaseType == null)
				{
					return overridenMethod;
				}

				var declaringBaseType = declaringType.BaseType.Resolve();
				var baseMethodName = overridingMethod.FullName.Replace(overridingMethod.DeclaringType.FullName + "::",
										declaringBaseType.FullName + "::");
				var methodInBaseType = declaringBaseType.Methods.FirstOrDefault(x => x.FullName == baseMethodName);

				if (methodInBaseType != null)
				{
					overridenMethod = methodInBaseType;
				}

				declaringType = declaringBaseType;
			}
		}

		private void Fix(MethodDefinition method)
		{
			var iteratorClass = FindIteratorClass(method);

			if (iteratorClass == null)
			{
				return;
			}

			for (int i = 0; i < method.GenericParameters.Count; i++)
			{
				var methodParameter = method.GenericParameters[i];
				var classParameter = iteratorClass.GenericParameters[i];

				ChangeConstraintAttributesIfNeeded(methodParameter, classParameter);

				for (int j = 0; j < methodParameter.Constraints.Count; j++)
				{
					if (!classParameter.Constraints.Contains(methodParameter.Constraints[j]))
					{
						classParameter.Constraints.Add(methodParameter.Constraints[j]);
						_context.RewriteTarget = true;
					}
				}
			}
		}

		private TypeDefinition FindIteratorClass(MethodDefinition method)
		{
			var declaringType = method.DeclaringType;

			foreach (var nestedType in declaringType.NestedTypes)
			{
				if (!nestedType.Name.Contains(method.Name))
				{
					continue;
				}

				if (!nestedType.HasGenericParameters)
				{
					continue;
				}

				if (nestedType.GenericParameters.Count != method.GenericParameters.Count)
				{
					continue;
				}

				for (int i = 0; i < nestedType.GenericParameters.Count; i++)
				{
					if (nestedType.GenericParameters[i].Name != method.GenericParameters[i].Name)
					{
						continue;
					}
				}

				if (!method.Body.Variables.Any(x => x.VariableType.Name == nestedType.Name))
				{
					continue;
				}

				return nestedType;
			}

			return null;
		}

		private void ChangeConstraintAttributesIfNeeded(GenericParameter source, GenericParameter target)
		{
			if (target.HasDefaultConstructorConstraint != source.HasDefaultConstructorConstraint)
			{
				target.HasDefaultConstructorConstraint = source.HasDefaultConstructorConstraint;
				_context.RewriteTarget = true;
			}

			if (target.HasNotNullableValueTypeConstraint != source.HasNotNullableValueTypeConstraint)
			{
				target.HasNotNullableValueTypeConstraint = source.HasNotNullableValueTypeConstraint;
				_context.RewriteTarget = true;
			}

			if (target.HasReferenceTypeConstraint != source.HasReferenceTypeConstraint)
			{
				target.HasReferenceTypeConstraint = source.HasReferenceTypeConstraint;
				_context.RewriteTarget = true;
			}
		}
	}
}
