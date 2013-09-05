using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Mono.Cecil;
using Mono.Cecil.Cil;
using NUnit.Framework;
using Unity.ReferenceRewriter;

namespace Test.Driver
{
	[TestFixture]
    public class ReferenceRewriting
    {
		[Test]
		public void AssemblyReferences()
		{
			AssertRewrite(
				ctx => Assert.AreEqual("2.0.0.0", ctx.TargetModule.AssemblyReferences.Single(r => r.Name == "mscorlib").Version.ToString(4)),
				ctx => Assert.AreEqual("4.0.0.0", ctx.TargetModule.AssemblyReferences.Single(r => r.Name == "mscorlib").Version.ToString(4)));
		}

		[Test]
		public void ArrayListReferences()
		{
			AssertRewrite(
				ctx => {},
				ctx =>
				{
					var os = ctx.TargetModule.GetType("Test.Target.ObjectStore");
					var add = os.Methods.Single(m => m.Name == "AddObject");

					var call = (MethodReference) add.Body.Instructions.Single(i => i.OpCode.OperandType == OperandType.InlineMethod).Operand;

					Assert.AreEqual("Test.Support", call.DeclaringType.Scope.Name);
					Assert.AreEqual(1, ctx.TargetModule.AssemblyReferences.Count(r => r.Name == "Test.Support"));
				});
		}

		public static void AssertRewrite(Action<RewriteContext> preAssertions, Action<RewriteContext> postAssertions)
		{
			var context = RewriteContext.For(
				"Test.Target.dll",
				DebugSymbolFormat.None,
				"Test.Support.dll",
				new string[] { Path.GetDirectoryName(typeof(object).Assembly.Location) },
				string.Empty,
				new string[0],
				new string[0], 
				new Dictionary<string, IList<string>>());

			var operation = RewriteOperation.Create(ns => ns.StartsWith("System") ? "Test.Support" + ns.Substring("System".Length) : ns);

			preAssertions(context);
			operation.Execute(context);
			postAssertions(context);
		}
    }
}
