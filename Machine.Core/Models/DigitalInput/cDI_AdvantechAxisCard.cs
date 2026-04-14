using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Advantech.Motion;
using Machine.Core.Enums;
using Machine.Core.Interfaces;

namespace Machine.Core
{
    public class cDI_AdvantechAxisCard : IDigitalInput
    {
        //IntPtr AdvantechAxisCard.ax  => AdvantechAxisCard.axisHandles[BoardID * AdvantechAxisCard.AxisCount + Channel];

        public string UID { get; set; }

        public string Name { get; set; }

        public IOCardType Type => IOCardType.AdvantechAxisCard;

        public Type StatusType { set; get; } = typeof(bool);

        public int BoardID { get; set; }

        public int Channel { get; set; }

        public int Bit { get; set; }

        public bool Inverse { get; set; }


        /// <summary>
        /// 0：RDY---- RDY 针脚输入；
        //1：ALM ---- 报警信号输入；
        //2：LMT+ ---- 限位开关+ ；
        //3：LMT- ---- 限位开关- ；
        //4：ORG---- 原始开关；
        //5：DIR ---- DIR 输出；
        //6：EMG ---- 紧急信号输入；
        //7：PCS ---- PCS 信号输入（PCI-1245/1245E/1265 不支持）；
        //8：ERC ---- 输出偏转计数器清除信号至伺服电机驱动；
        //（OUT7）
        //9：EZ ---- 编码器Z 信号；
        //10：CLR ---- 外部输入至清除位置计数器（PCI-1245/1245V/
        //1245E/1265 不支持）；
        //11：LTC ---- 锁存信号输入；
        //12：SD ---- 减速信号输入（PCI-1245/1245V/1245E/1265 不支
        //持）；
        //13：INP ---- 到位信号输入；
        //14：SVON ---- 伺服开启（OUT6）；
        //15：ALRM ---- 报警复位输出状态；
        //16：SLMT+ ---- 软件限位+ ；
        //17：SLMT- ---- 软件限位- ；
        //18：CMP----- 比较信号（OUT5）；
        //19：CAMDO ---- 凸轮区间DO （OUT4）。
        ///20:IN4/JOG+
        ///21:IN5/JOG-        
        /// </summary>
        /// <returns></returns>

        public object GetStatus()
        {

            //AdvantechAxisCard.LockProtect();
            //uint ioStatus = 0;
            //long status  ;
            //if (Bit == 20 || Bit == 21 || Bit ==11  || Bit == 0)
            //{

            //    int bit = 0;
            //    if (Bit == 20)
            //        bit = 2;
            //    else if (Bit == 21)
            //        bit = 3;
            //    else if (Bit == 11)
            //        bit = 0;
            //    else if (Bit == 0)
            //        bit = 1;
            //    byte bitState = 0;
            //    Motion.mAcm_AxDiGetBit(AdvantechAxisCard.axisHandles[Channel], (ushort)(bit ), ref bitState);
            //    status = bitState;
            //}
            //else
            //{


            //    ErrorCode err = (ErrorCode)Motion.mAcm_AxGetMotionIO(AdvantechAxisCard.axisHandles[Channel], ref ioStatus);

            //    if (err != ErrorCode.SUCCESS)
            //        throw new InvalidOperationException("Advantech Axis Card GetMotionIO error");
            //    status = ioStatus & (uint)Math.Pow(2, Bit);
            //}


            //AdvantechAxisCard.UnLock();

            //return Inverse ? status == 0 : status != 0;

            return AdvantechAxisCard.Protect(() =>
            {

                uint ioStatus = 0;
                long status;
                if (Bit == 20 || Bit == 21 || Bit == 11 || Bit == 0)
                {

                    int bit = 0;
                    if (Bit == 20)
                        bit = 2;
                    else if (Bit == 21)
                        bit = 3;
                    else if (Bit == 11)
                        bit = 0;
                    else if (Bit == 0)
                        bit = 1;
                    byte bitState = 0;
                    Motion.mAcm_AxDiGetBit(AdvantechAxisCard.axisHandles[Channel], (ushort)(bit), ref bitState);
                    status = bitState;
                }
                else
                {


                    ErrorCode err = (ErrorCode)Motion.mAcm_AxGetMotionIO(AdvantechAxisCard.axisHandles[Channel], ref ioStatus);

                    if (err != ErrorCode.SUCCESS)
                        throw new InvalidOperationException("Advantech Axis Card GetMotionIO error");
                    status = ioStatus & (uint)Math.Pow(2, Bit);
                }

                return Inverse ? status == 0 : status != 0;




            });

        }
    }
}
