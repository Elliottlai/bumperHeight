using Advantech.Motion;
using Machine.Core.Interfaces;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Machine.Core.Enums;
using System.Threading;

namespace Machine.Core
{
    public class cDO_AdvantechAxisCard : IDigitalOutput
    {
        public string Name { get; set; }

        public string UID { get; set; }
        //IntPtr AdvantechAxisCard.;
        //public cDO_AdvantechAxisCard()
        //{
        //    AdvantechAxisCard. = AdvantechAxisCard.axisHandles[BoardID * AdvantechAxisCard.AxisCount + Channel];
        //}

        public IOCardType Type => IOCardType.AdvantechAxisCard;

        public Type StatusType { set; get; } = typeof(bool);

        public int Bit { get; set; }

        // public int BoardID { get; set; }

        public int Channel { get; set; }

        public bool Inverse { get; set; }




        public object GetStatus()
        {
            //byte ioStatus = 0;
            //ushort bit = (ushort)Bit;
            //AdvantechAxisCard.LockProtect();
            ////uint err = (uint)Motion.mAcm_AxDoGetBit(AdvantechAxisCard.axisHandles[Channel], bit, ref ioStatus);
            //ErrorCode err = (ErrorCode)Motion.mAcm_AxDoGetBit(AdvantechAxisCard.axisHandles[Channel], bit, ref ioStatus);
            //AdvantechAxisCard.UnLock();

            //if (err != ErrorCode.SUCCESS)
            ////if (err != (uint)ErrorCode.SUCCESS)
            //        throw new InvalidOperationException("Advantech Axis Card ReadIn error");

            //return Inverse ? ioStatus == 0 : ioStatus != 0;



            return AdvantechAxisCard.Protect(() =>
            {

                byte ioStatus = 0;
                ushort bit = (ushort)Bit; 
                //uint err = (uint)Motion.mAcm_AxDoGetBit(AdvantechAxisCard.axisHandles[Channel], bit, ref ioStatus);
                ErrorCode err = (ErrorCode)Motion.mAcm_AxDoGetBit(AdvantechAxisCard.axisHandles[Channel], bit, ref ioStatus);
          

                if (err != ErrorCode.SUCCESS)
                    //if (err != (uint)ErrorCode.SUCCESS)
                    throw new InvalidOperationException($"Advantech Axis Card ReadIn error - Name : {Name} Channel : {Channel} Bit : {Bit}");

                return Inverse ? ioStatus == 0 : ioStatus != 0;

            });

        }

        public  void SetStatus(object Data)
        {
            //dynamic Status = Convert.ChangeType(Data, StatusType);
            //Status = Inverse ? !Status : Status;

            //ushort bit = (ushort)Bit;
            //AdvantechAxisCard.LockProtect();
            //ErrorCode err = (ErrorCode)Motion.mAcm_AxDoSetBit(AdvantechAxisCard.axisHandles[Channel], bit, (byte)(Status ? 1 : 0));
            //AdvantechAxisCard.UnLock();

            //if (err != ErrorCode.SUCCESS)
            //    throw new InvalidOperationException("Advantech Axis Card ReadIn error");

            AdvantechAxisCard.Protect(() =>
            {

                dynamic Status = Convert.ChangeType(Data, StatusType);
                Status = Inverse ? !Status : Status;

                ushort bit = (ushort)Bit;

                ErrorCode err = (ErrorCode)Motion.mAcm_AxDoSetBit(AdvantechAxisCard.axisHandles[Channel], bit, (byte)(Status ? 1 : 0));


                int Count = 0;
                do
                {
                    Thread.Sleep(10);
                    Count++;
                    if(Count > 3)
                        throw new InvalidOperationException("Advantech Axis Card SetStatus is not complete > 30ms");
                } while ((bool)GetStatus() != (bool)Data);

                 
                
                if (err != ErrorCode.SUCCESS)
                    throw new InvalidOperationException("Advantech Axis Card ReadIn error");

            });

            }


    }
}
