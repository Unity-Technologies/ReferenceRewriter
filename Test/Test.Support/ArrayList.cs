using System;
using System.Collections;
using System.Collections.Generic;

namespace Test.Support.Collections
{
    public class ArrayList : IList
    {
	    private readonly List<object> _list;

		public ArrayList()
		{
			_list = new List<object>();
		}

		public ArrayList(int capacity)
		{
			_list = new List<object>(capacity);
		}

		public int Count { get { return _list.Count; } }

	    public object SyncRoot { get { return ((ICollection) _list).SyncRoot; } }

	    public bool IsSynchronized { get { return ((ICollection)_list).IsSynchronized; } }

	    public object this[int index]
	    {
		    get { return _list[index]; }
		    set { _list[index] = value; }
	    }

	    public IEnumerator GetEnumerator()
	    {
		    return _list.GetEnumerator();
	    }

	    public void CopyTo(Array array, int index)
	    {
		    ((ICollection)_list).CopyTo(array, index);
	    }

	    public int Add(object value)
	    {
		    _list.Add(value);
		    return _list.Count - 1;
	    }

	    public bool Contains(object value)
	    {
		    return _list.Contains(value);
	    }

	    public void Clear()
	    {
		    _list.Clear();
	    }

	    public int IndexOf(object value)
	    {
		    return _list.IndexOf(value);
	    }

	    public void Insert(int index, object value)
	    {
		    _list.Insert(index, value);
	    }

	    public void Remove(object value)
	    {
		    _list.Remove(value);
	    }

	    public void RemoveAt(int index)
	    {
		    _list.RemoveAt(index);
	    }

		public bool IsReadOnly { get { return false; } }
		public bool IsFixedSize { get { return false; } }
    }
}
