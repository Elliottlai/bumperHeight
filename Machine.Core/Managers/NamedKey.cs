using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;

namespace Machine.Core
{
    public class NamedKey
    {
        private readonly string Key;
        public NamedKey(string key) => Key = key;
        public override string ToString() => Key;
        public static NamedKey Create([CallerMemberName] string key = null) => new NamedKey(key);
        public static implicit operator string(NamedKey d) => d.ToString();
          
    }
}
