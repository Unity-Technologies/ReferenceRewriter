using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Unity.ReferenceRewriter;

namespace Test.UnitTests
{
	[TestClass]
	public class TypeAliasesTest
	{
		[TestMethod]
		public void TestGetTemplateArgumentsSimpleOne()
		{
			var result = TypeAliases.GetTemplateArguments("Windows.Foundation.Collections.IVector`1<System.Int32>");
			Assert.IsNotNull(result);
			Assert.AreEqual(1, result.Length);
			Assert.AreEqual("System.Int32", result[0]);
		}

		[TestMethod]
		public void TestGetTemplateArgumentsSimpleTwo()
		{
			var result = TypeAliases.GetTemplateArguments("Windows.Foundation.Collections.IMap`2<System.Int32,System.Int32>");
			Assert.IsNotNull(result);
			Assert.AreEqual(2, result.Length);
			Assert.AreEqual("System.Int32", result[0]);
			Assert.AreEqual("System.Int32", result[1]);
		}

		[TestMethod]
		public void TestGetTemplateArgumentsNestedOneOne()
		{
			var result = TypeAliases.GetTemplateArguments("Windows.Foundation.Collections.IVector`1<Windows.Foundation.Collections.IVector`1<System.Int32>>");
			Assert.IsNotNull(result);
			Assert.AreEqual(1, result.Length);
			Assert.AreEqual("Windows.Foundation.Collections.IVector`1<System.Int32>", result[0]);
		}

		[TestMethod]
		public void TestGetTemplateArgumentsNestedOneTwo()
		{
			var result = TypeAliases.GetTemplateArguments("Windows.Foundation.Collections.IVector`1<Windows.Foundation.Collections.IMap`2<System.Int32,System.Int32>>");
			Assert.IsNotNull(result);
			Assert.AreEqual(1, result.Length);
			Assert.AreEqual("Windows.Foundation.Collections.IMap`2<System.Int32,System.Int32>", result[0]);
		}

		[TestMethod]
		public void TestGetTemplateArgumentsNestedTwoOneOne()
		{
			var result = TypeAliases.GetTemplateArguments("Windows.Foundation.Collections.IMap`2<Windows.Foundation.Collections.IVector`1<System.Int32>,Windows.Foundation.Collections.IVector`1<System.Int32>>");
			Assert.IsNotNull(result);
			Assert.AreEqual(2, result.Length);
			Assert.AreEqual("Windows.Foundation.Collections.IVector`1<System.Int32>", result[0]);
			Assert.AreEqual("Windows.Foundation.Collections.IVector`1<System.Int32>", result[1]);
		}

		[TestMethod]
		public void TestGetTemplateArgumentsNestedComplex()
		{
			var result = TypeAliases.GetTemplateArguments("Windows.Foundation.Collections.IMap`2<Windows.Foundation.Collections.IMap`2<Windows.Foundation.Collections.IVector`1<System.Int32>,Windows.Foundation.Collections.IVector`1<System.Int32>>,Windows.Foundation.Collections.IMap`2<Windows.Foundation.Collections.IVector`1<System.Int32>,System.Int32>>");
			Assert.IsNotNull(result);
			Assert.AreEqual(2, result.Length);
			Assert.AreEqual("Windows.Foundation.Collections.IMap`2<Windows.Foundation.Collections.IVector`1<System.Int32>,Windows.Foundation.Collections.IVector`1<System.Int32>>", result[0]);
			Assert.AreEqual("Windows.Foundation.Collections.IMap`2<Windows.Foundation.Collections.IVector`1<System.Int32>,System.Int32>", result[1]);
		}

		[TestMethod]
		public void TestGetTemplateArgumentsHandleSpaces()
		{
			var result = TypeAliases.GetTemplateArguments("Windows.Foundation.Collections.IMap`2< Windows.Foundation.Collections.IVector`1<System.Int32>, Windows.Foundation.Collections.IVector`1<System.Int32> >");
			Assert.IsNotNull(result);
			Assert.AreEqual(2, result.Length);
			Assert.AreEqual("Windows.Foundation.Collections.IVector`1<System.Int32>", result[0]);
			Assert.AreEqual("Windows.Foundation.Collections.IVector`1<System.Int32>", result[1]);
		}
	}
}
