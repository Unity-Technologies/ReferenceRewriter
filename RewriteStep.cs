namespace Unity.ReferenceRewriter
{
	abstract class RewriteStep : IRewriteStep
	{
		protected RewriteContext Context { get; private set; }

		public void Execute(RewriteContext context)
		{
			Context = context;

			Run();
		}

		protected abstract void Run();
	}
}