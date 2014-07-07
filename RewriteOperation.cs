using System;
using System.Collections.Generic;

namespace Unity.ReferenceRewriter
{
	public class RewriteOperation
	{
		private readonly List<IRewriteStep> _steps;

		private RewriteOperation(params IRewriteStep[] steps) : this(steps as IEnumerable<IRewriteStep>)
		{
		}

		private RewriteOperation(IEnumerable<IRewriteStep> steps)
		{
			_steps = new List<IRewriteStep>(steps);
		}

		public void Execute(RewriteContext context)
		{
			if (context == null)
				throw new ArgumentNullException("context");

			foreach (var step in _steps)
				step.Execute(context);
		}

		public static RewriteOperation Create(Func<string, string> supportNamespaceMapper)
		{
			if (supportNamespaceMapper == null)
				throw new ArgumentNullException("supportNamespaceMapper");

			return new RewriteOperation(
				new RewriteAssemblyManifest(),
				new RewriteTypeReferences(supportNamespaceMapper),
				new RewriteMethodSpecMemberRefs(),
				new EnumeratorGenericConstraintsFixer(),
				new RemoveStrongNamesFromAssemblyReferences()
			);
		}
	}
}