using Machine.Core.Enums;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;

namespace Machine.Core.Interfaces
{
    public interface IPlatformArgs : IComponent
    {

        string AxisX_UID { get; set; }
        string AxisY_UID { get; set; }
        string AxisZ_UID { get; set; }


        
        /// <summary>
        /// 掃描原點的 X 坐標
        /// </summary>
        double ScanLocationX { set; get; }

        /// <summary>
        /// 掃描原點的 Y 坐標
        /// </summary>
        double ScanLocationY { set; get; }

       


        /// <summary>
        /// 掃描原點的 Z 坐標
        /// </summary>
        double ScanLocationZ { set; get; }

        /// <summary>
        /// 載台的開始位置
        /// </summary>
        double StartingPosition { set; get; }

        /// <summary>
        /// 校正板 Y 座標 (相對 ScanLocationY)
        /// </summary>
        double StandardBoardRelativeLocationY { set; get; }

        /// <summary>
        /// 校正板 Z 座標 (相對 ScanLocationZ)
        /// </summary>
        double StandardBoardRelativeLocationZ { set; get; }

   

        ///// <summary>
        ///// X操作速度
        ///// </summary>
        //double Vel_X { set; get; }

        ///// <summary>
        ///// Y操作速度
        ///// </summary>
        //double Vel_Y { set; get; }

        ///// <summary>
        ///// 掃描的平台速度
        ///// </summary>
        //double Vel_ScanY { set; get; }

        ///// <summary>
        ///// 兩趟的間距
        ///// </summary>
        //double OverLap { set; get; }


        /// <summary>
        /// 檯面寬
        /// </summary>
        int MesaWidth { set; get; }

        /// <summary>
        /// 檯面長
        /// </summary>
        int MesaHeight { set; get; }

    }
}
