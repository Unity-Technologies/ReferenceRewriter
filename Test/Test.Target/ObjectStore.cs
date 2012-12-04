using System.Collections;

namespace Test.Target
{
    public class ObjectStore
    {
		private readonly ArrayList _list = new ArrayList();

		public void AddObject(object obj)
		{
			_list.Add(obj);
		}

		public void DeleteObject(object obj)
		{
			_list.Remove(obj);
		}
    }
}
