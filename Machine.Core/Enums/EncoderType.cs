using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Machine.Core.Enums
{    
    /// <summary>馬達編碼器類型</summary>
    public enum EncoderType
    {
        /// <summary>無編碼器</summary>
        None = 0,
        /// <summary>增量型編碼器</summary>
        Relative = 1,
        /// <summary>絕對型編碼器</summary>
        Absolute = 2,
    }
}
