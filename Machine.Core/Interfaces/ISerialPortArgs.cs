using System.IO.Ports;

namespace Machine.Core.Interfaces
{
    public interface ISerialPortArgs
    {
        /// <summary>
        /// 
        /// </summary>
        int BaudRate { set; get; }

        /// <summary>
        /// 同位元
        /// </summary>
        Parity Parity { set; get; }

        /// <summary>
        /// 資料長度
        /// </summary>
        int DataBits { set; get; }

        /// <summary>
        /// 停止位元數
        /// </summary>
        StopBits StopBits { set; get; }

        /// <summary>
        /// 讀取逾時
        /// </summary>
        int ReadTimeout { set; get; }

        /// <summary>
        /// 寫入逾時
        /// </summary>
        int WriteTimeout { set; get; }
        /// <summary>
        /// 最大亮度
        /// </summary>
        int MaxLevel { set; get; }
    }
}
