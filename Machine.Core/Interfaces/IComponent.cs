using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Machine.Core.Interfaces
{
    public interface IComponent
    {
        /// <summary>
        /// 識別碼
        /// </summary>
        string UID { set; get; }

        /// <summary>
        /// 自定名稱
        /// </summary>
        string Name { set; get; }

    }
}
