using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Machine.Core.Helpers
{
    public class AccessRightHandler
    {

        ConcurrentQueue<Guid> AR = new ConcurrentQueue<Guid>();


        Guid Key = Guid.NewGuid();

        public bool Pass(Guid guid)
        {
            return Key.Equals(guid);            
        }

        public void Get(ref Guid guid)
        {            
            AR.TryDequeue(out Guid guidNew);
            if (guidNew.Equals(Key))
                guid = Key;                                
        }

        object obj=new object();
        public void Return(ref Guid guid)
        {
            Monitor.Enter(obj);
            if (Key.Equals(guid))
            {
                guid = new Guid();
                AR.Enqueue(Key);                                            
                if (AR.Count > 1)
                    throw new Exception(Name + " AccessRight > 1 ");
            }
            Monitor.Exit (obj);

        }

        //public bool Get() => AR.TryDequeue(out object key);
        string Name="";

        public AccessRightHandler(string name ="AR")
        {
            Name = name;
        }
        /*
        public void Return()
        {
            AR.Enqueue(new object());
            if (AR.Count > 1)
                throw new Exception(Name + " AccessRight > 1 ");
        }*/

        public void Init()
        {
            Guid gu=new Guid ();
            do
            {
                AR.TryDequeue(out  gu);
            }
            while (gu != Guid.Empty);
              AR.Enqueue(Key);
        }

    }
}
