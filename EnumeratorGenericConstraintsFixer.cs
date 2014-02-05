using Mono.Cecil;

namespace Unity.ReferenceRewriter
{
	/*
	 *	There's a bug in Mono compiler that makes this code throw a runtime exception when running under .NET:
	 * 
	 *  public class SomeBaseClass {}
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
	 *  The task of this class is find such methods and modify them to look like this:
	 *  
	 *	.class nested private auto ansi sealed beforefieldinit '<GetResource>c__Iterator0`1'<(SomeBaseClass) TType>
	 *	extends [mscorlib]System.Object
	 *	implements [mscorlib]System.Collections.IEnumerator,
	 *	           class [mscorlib]System.Collections.Generic.IEnumerator`1<object>,
	 *	           [mscorlib]System.IDisposable
	 */
	static class EnumeratorGenericConstraintsFixer
	{
		public static void Fix(ModuleDefinition module)
		{
			foreach (var type in module.Types)
			{
				Fix(type);
			}
		}

		private static void Fix(TypeDefinition type)
		{
			foreach (var method in type.Methods)
			{
				MethodDefinition overridenMethod;

				if (IsBroken(method, out overridenMethod))
				{
					Fix(method, overridenMethod);
				}
			}
		}

		private static bool IsBroken(MethodDefinition method, out MethodDefinition overridenMethod)
		{
			overridenMethod = null;
			return false;
		}

		private static MethodDefinition GetOverridenMethod(MethodDefinition overridingMethod)
		{
			return null;
		}

		private static void Fix(MethodDefinition method, MethodDefinition overridenMethod)
		{

		}
	}
}
