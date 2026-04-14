using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Machine.Core.Enums
{
    /// <summary>
    /// 表示軸目前的狀態資訊。
    /// </summary>
    [Flags]
    public enum SensorStatus
    {
        /// <summary>
        /// 表示無任何訊號觸發。
        /// </summary>
        None = 0x0000,

        /// <summary>
        /// 表示Alarm訊號觸發。
        /// </summary>
        ALM = 0x0001,

        /// <summary>
        /// 表示反向運動訊號觸發。
        /// </summary>
        DIR = 0x0002,

        /// <summary>
        /// 表示緊急停止訊號觸發。
        /// </summary>
        EMG = 0x0004,

        /// <summary>
        /// 表示到位訊號觸發。
        /// </summary>
        INP = 0x0008,

        /// <summary>
        /// 表示負極限訊號觸發。
        /// </summary>
        NEL = 0x0010,

        /// <summary>
        /// 表示位於原點訊號觸發。
        /// </summary>
        ORG = 0x0020,

        /// <summary>
        /// 表示正極限訊號觸發。
        /// </summary>
        PEL = 0x0040,

        /// <summary>
        /// 表示Ready訊號觸發。
        /// </summary>
        RDY = 0x0080,

        /// <summary>
        /// 表示減速到停止(Slow Down)訊號觸發。
        /// </summary>
        SD = 0x0100,

        /// <summary>
        /// 表示Servo已經開啟的訊號。
        /// </summary>
        SVON = 0x0200,

        /// <summary>
        /// 編碼器Z訊號
        /// </summary>
        EZ = 0x0400
    }
}
