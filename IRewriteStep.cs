namespace Unity.ReferenceRewriter
{
	interface IRewriteStep
	{
		void Execute(RewriteContext context);
	}
}