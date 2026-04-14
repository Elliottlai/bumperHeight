using Advantech.Motion;
using Newtonsoft.Json;
using Machine.Core;
using Machine.Core.Enums;
using Machine.Core.Interfaces;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

namespace Machine.Core
{
   public class cAxis_AdvantechAxisCard : IAxis, IDisposable
   {

      #region Member Variables

      // private IntPtr AdvantechAxisCard.axisHandles [AxisID] { set; get; }


      public string UID { set; get; }

      public string Name { set; get; }

      public AxisCardType Type => AxisCardType.AdvantechAxisCard;

      //   public int CardID { set; get; }

      public int AxisID { set; get; }

      public int HomeMode { set; get; }
      double homeSpeed;
      double homeStartSpeed;
      double homeBuffer;
      double operationSpeed;
      double operationStartSpeed;
      double softwareNLimit;
      double softwarePLimit;

      public double HomeSpeed { get => Convert.ToDouble(homeSpeed / 1000); set => homeSpeed = Convert.ToInt32(value * 1000); }
      public double HomeStartSpeed { get => Convert.ToDouble(homeStartSpeed / 1000); set => homeStartSpeed = Convert.ToInt32(value * 1000); }
      public double HomeAcc { get; set; }
      public double HomeDec { get; set; }
      public double HomeBuffer { get => Convert.ToDouble(homeBuffer / 1000); set => homeBuffer = Convert.ToInt32(value * 1000); }
      public double OperationSpeed
      {
         get => Convert.ToDouble(operationSpeed / 1000);
         set => operationSpeed = Convert.ToInt32(value * 1000);
      }
      public double OperationStartSpeed { get => Convert.ToDouble(operationStartSpeed / 1000); set => operationStartSpeed = Convert.ToInt32(value * 1000); }
      public double OperationAcc { get; set; }
      public double OperationDec { get; set; }
      public double Scale { get; set; }
      public double Tolerance { get; set; }


      private double CurrrentSpeed;
      private double CurrrentStrSpeed;
      private double CurrrentAcc;
      private double CurrrentDec;

      CurveType curve;
      public CurveType Curve
      {
         get => curve;
         set
         {
            /*AdvantechAxisCard.LockProtect();
            curve = value;
            double jerk = curve == CurveType.T_Curve ? 0 : 1;

            ErrorCode err = (ErrorCode)Motion.mAcm_SetProperty(AdvantechAxisCard.axisHandles[AxisID], (uint)PropertyID.PAR_AxJerk,
                                                ref jerk, (uint)Marshal.SizeOf(typeof(double)));

            if (err != ErrorCode.SUCCESS)
            {
                //throw new InvalidOperationException("Advantech Axis Card SetCurve error");
            }
            AdvantechAxisCard.UnLock();
            */
            AdvantechAxisCard.Protect(() =>
            {
               curve = value;
               double jerk = curve == CurveType.T_Curve ? 0 : 1;

               ErrorCode err = (ErrorCode)Motion.mAcm_SetProperty(AdvantechAxisCard.axisHandles[AxisID], (uint)PropertyID.PAR_AxJerk,
                                                       ref jerk, (uint)Marshal.SizeOf(typeof(double)));

               if (err != ErrorCode.SUCCESS)
               {
                  //throw new InvalidOperationException("Advantech Axis Card SetCurve error");
               }

            }
            );


         }
      }
      public double SoftwareNLimit
      {
         get => Convert.ToDouble(softwareNLimit / 1000);

         set
         {

            AdvantechAxisCard.Protect(() =>
            {
               softwareNLimit = Convert.ToInt32(value * 1000);
               Motion.mAcm_SetProperty(AdvantechAxisCard.axisHandles[AxisID], (uint)PropertyID.CFG_AxSwMelValue, ref softwareNLimit, sizeof(uint));

            });
         }
      }
      public double SoftwarePLimit
      {
         get => Convert.ToDouble(softwarePLimit / 1000);

         set
         {
            //AdvantechAxisCard.LockProtect();
            //softwarePLimit = Convert.ToInt32(value * 1000);
            //Motion.mAcm_SetProperty(AdvantechAxisCard.axisHandles[AxisID], (uint)PropertyID.CFG_AxSwPelValue, ref softwarePLimit, sizeof(uint));
            //AdvantechAxisCard.UnLock();

            AdvantechAxisCard.Protect(() =>
            {
               softwarePLimit = Convert.ToInt32(value * 1000);
               Motion.mAcm_SetProperty(AdvantechAxisCard.axisHandles[AxisID], (uint)PropertyID.CFG_AxSwPelValue, ref softwarePLimit, sizeof(uint));

            });
         }
      }


      double m_CmdPosition;
      #endregion Member Variables


      /// <summary>
      /// Constructor
      /// </summary>
      #region  Constructor
      public cAxis_AdvantechAxisCard()
      {
         CurrrentSpeed = OperationSpeed;
         CurrrentStrSpeed = OperationStartSpeed;
         CurrrentAcc = 0.2;
         CurrrentDec = 0.2;
         double Actual = 0;



        }

        //private bool IsInitialized;
        ////private void Init()
        ////{
        ////    if (IsInitialized)
        ////        return;

        ////    AdvantechAxisCard.OpenDevice();

        ////    //AdvantechAxisCard.axisHandles [AxisID] = AdvantechAxisCard.axisHandles[CardID * AdvantechAxisCard.AxisCount + AxisID];

        ////    //      AdvantechAxisCard.axisHandles [AxisID] = PCI_1245.axisHandles[CardID * PCI_1245.AxisCount + AxisID];

        ////    /*  OperationStartSpeed = OperationStartSpeed;
        ////      OperationSpeed = OperationSpeed;

        ////      OperationAcc = OperationAcc;
        ////      OperationDec = OperationDec;
        ////      Curve = Curve;*/
        ////    m_CmdPosition = 0;
        ////    uint err = Motion.mAcm_AxResetError(AdvantechAxisCard.axisHandles [AxisID]);
        ////    if (err != (uint)ErrorCode.SUCCESS)
        ////        throw new InvalidOperationException("Advantech Axis Card ResetError error");

        ////    uint d = 1;
        ////    Motion.mAcm_SetProperty(AdvantechAxisCard.axisHandles[AxisID], 575, ref d, sizeof(uint));
        ////    d = 0;
        ////    Motion.mAcm_SetProperty(AdvantechAxisCard.axisHandles [AxisID], 575, ref d, sizeof(uint));
        ////    d = 1;
        ////    Motion.mAcm_SetProperty(AdvantechAxisCard.axisHandles [AxisID], 575, ref d, sizeof(uint));


        ////    IsInitialized = true;
        ////}

        //public cAxis_PCI1245()
        //{
        //}

        //public cAxis_PCI1245(string FullFileName, string SectionName) : base(FullFileName, SectionName)
        //{
        //}

        /*
        public cAxis_PCI1245(CAxisSetIni Setting)
        {
            m_Setting = Setting;
            CPcDevice.OpenPcDevice();
            AdvantechAxisCard.axisHandles [AxisID] = CPcDevice.PCI_1245.axisHandles[m_Setting.BoaedID * CPcDevice.PCI_1245.AxisCount + m_Setting.AxisID];
            OperationSpeed = m_Setting.OperationSpeed ;
            OperationStartSpeed = m_Setting.OperationStartSpeed ;
            OperationAcc = m_Setting.OperationAcc ;
            OperationDec = m_Setting.OperationDec ;
            Curve = m_Setting.Curve;
            uint err = Motion.mAcm_AxResetError(AdvantechAxisCard.axisHandles [AxisID]);
            if (err != (uint)ErrorCode.SUCCESS)
                throw new InvalidOperationException("Advantech Axis Card ResetError error");


        }*/
        #endregion  Constructor

        #region Public  Methods
        public double GetLogicPosition()
      {

         //AdvantechAxisCard.LockProtect();
         //double command = 0;
         //uint err = Motion.mAcm_AxGetCmdPosition(AdvantechAxisCard.axisHandles[AxisID], ref command);

         //if (err != (uint)ErrorCode.SUCCESS)
         //{
         //    // throw new InvalidOperationException("Advantech Axis Card GetCmdPosition error");
         //}
         //AdvantechAxisCard.UnLock();
         //return command * Scale;


         return AdvantechAxisCard.Protect(() =>
         {
            double command = 0;
            ErrorCode err = (ErrorCode)Motion.mAcm_AxGetCmdPosition(AdvantechAxisCard.axisHandles[AxisID], ref command);

            if (err != ErrorCode.SUCCESS)
            {
               // throw new InvalidOperationException("Advantech Axis Card GetCmdPosition error");
            }

            return command * Scale;
         });

         /*
         return AdvantechAxisCard.Protect(()=> 
         {
             double command = 0;
             uint err = Motion.mAcm_AxGetCmdPosition(AdvantechAxisCard.axisHandles[AxisID], ref command);

             if (err != (uint)ErrorCode.SUCCESS)
             {
                 // throw new InvalidOperationException("Advantech Axis Card GetCmdPosition error");
             }
             return command * Scale;
         });
         */

      }

      public double GetRealPosition()
      {
         // AdvantechAxisCard.LockProtect();


         //double Position = 0;
         //ErrorCode err = (ErrorCode)Motion.mAcm_AxGetActualPosition(AdvantechAxisCard.axisHandles[AxisID], ref Position);
         //if (err != ErrorCode.SUCCESS)
         //    throw new InvalidOperationException("Advantech Axis Card GetActualPosition error");
         //if (Position == 0)
         //{
         //    err = (ErrorCode)Motion.mAcm_AxGetCmdPosition(AdvantechAxisCard.axisHandles[AxisID], ref Position);
         //}

         //AdvantechAxisCard.UnLock();
         //return Position * Scale;

         return AdvantechAxisCard.Protect(() =>
         {
            double Position = 0;
            ErrorCode err = (ErrorCode)Motion.mAcm_AxGetActualPosition(AdvantechAxisCard.axisHandles[AxisID], ref Position);
            if (err != ErrorCode.SUCCESS)
               throw new InvalidOperationException("Advantech Axis Card GetActualPosition error");
            if (Position == 0)
            {
               err = (ErrorCode)Motion.mAcm_AxGetCmdPosition(AdvantechAxisCard.axisHandles[AxisID], ref Position);
            }


            return Position * Scale;
         });


      }
      public void SetTrigger(double position, int CompareMethods)
      {
         //AdvantechAxisCard.LockProtect();

         //int compareStatus = 1; // 0 : Disabled, 1: Enable
         //int compareSource = 1; // 0 : Command, 1 : Actual
         //int compareMethod = 0; // 0 : >= Position Counter, 1 : <= Position Counter, 2 : = Counter 不支援

         //switch (CompareMethods)
         //{
         //    case 0:  //Greater:
         //        compareMethod = 0;
         //        break;
         //    case 1://Smaller:
         //        compareMethod = 1;
         //        break;
         //    default:
         //        throw new NotSupportedException("Equal method is not supported.");
         //}

         //uint err = Motion.mAcm_SetProperty(AdvantechAxisCard.axisHandles[AxisID], (uint)global::Advantech.Motion.PropertyID.CFG_AxCmpSrc,
         //   ref compareSource, (uint)Marshal.SizeOf(typeof(int)));
         //if (err != (uint)ErrorCode.SUCCESS)
         //    throw new InvalidOperationException("Advantech Axis Card SetProperty AxCmpSrc error");


         //err = Motion.mAcm_SetProperty(AdvantechAxisCard.axisHandles[AxisID], (uint)global::Advantech.Motion.PropertyID.CFG_AxCmpMethod,
         //    ref compareMethod, (uint)Marshal.SizeOf(typeof(int)));

         //if (err != (uint)ErrorCode.SUCCESS)
         //    throw new InvalidOperationException("Advantech Axis Card SetProperty AxCmpMethod error");

         //err = Motion.mAcm_SetProperty(AdvantechAxisCard.axisHandles[AxisID], (uint)global::Advantech.Motion.PropertyID.CFG_AxCmpEnable,
         //    ref compareStatus, (uint)Marshal.SizeOf(typeof(int)));

         //if (err != (uint)ErrorCode.SUCCESS)
         //    throw new InvalidOperationException("Advantech Axis Card SetProperty CFG_AxCmpEnable error");


         //double[] compareArray = new double[] { position };

         //err = Motion.mAcm_AxSetCmpData(AdvantechAxisCard.axisHandles[AxisID], position);
         //if (err != (uint)ErrorCode.SUCCESS)
         //    throw new InvalidOperationException("Advantech Axis Card SetCmpData error");

         //AdvantechAxisCard.UnLock();

         AdvantechAxisCard.Protect(() =>
         {


            int compareStatus = 1; // 0 : Disabled, 1: Enable
            int compareSource = 1; // 0 : Command, 1 : Actual
            int compareMethod = 0; // 0 : >= Position Counter, 1 : <= Position Counter, 2 : = Counter 不支援

            switch (CompareMethods)
            {
               case 0:  //Greater:
                  compareMethod = 0;
                  break;
               case 1://Smaller:
                  compareMethod = 1;
                  break;
               default:
                  throw new NotSupportedException("Equal method is not supported.");
            }

            ErrorCode err = (ErrorCode)Motion.mAcm_SetProperty(AdvantechAxisCard.axisHandles[AxisID], (uint)global::Advantech.Motion.PropertyID.CFG_AxCmpSrc,
                  ref compareSource, (uint)Marshal.SizeOf(typeof(int)));
            if (err != ErrorCode.SUCCESS)
               throw new InvalidOperationException("Advantech Axis Card SetProperty AxCmpSrc error");

             

            err = (ErrorCode)Motion.mAcm_SetProperty(AdvantechAxisCard.axisHandles[AxisID], (uint)global::Advantech.Motion.PropertyID.CFG_AxCmpMethod,
                   ref compareMethod, (uint)Marshal.SizeOf(typeof(int)));

            if (err != ErrorCode.SUCCESS)
               throw new InvalidOperationException("Advantech Axis Card SetProperty AxCmpMethod error");

            err = (ErrorCode)Motion.mAcm_SetProperty(AdvantechAxisCard.axisHandles[AxisID], (uint)global::Advantech.Motion.PropertyID.CFG_AxCmpEnable,
                   ref compareStatus, (uint)Marshal.SizeOf(typeof(int)));

            if (err != ErrorCode.SUCCESS)
               throw new InvalidOperationException("Advantech Axis Card SetProperty CFG_AxCmpEnable error");


            double[] compareArray = new double[] { position };

            err = (ErrorCode)Motion.mAcm_AxSetCmpData(AdvantechAxisCard.axisHandles[AxisID], position);
            if (err != ErrorCode.SUCCESS)
               throw new InvalidOperationException("Advantech Axis Card SetCmpData error");
         });

      }
      public void SetTrigger(double[] positions, int CompareMethods)
        {
            //add by Glenn
            AdvantechAxisCard.Protect(() =>
            {
                int compareStatus = 1; // 0 : Disabled, 1: Enable
                int compareSource = 1; // 0 : Command, 1 : Actual
                int compareMethod = 0; // 0 : >= Position Counter, 1 : <= Position Counter, 2 : = Counter 不支援

                switch (CompareMethods)
                {
                    case 0:  //Greater:
                        compareMethod = 0;
                        break;
                    case 1://Smaller:
                        compareMethod = 1;
                        break;
                    default:
                        throw new NotSupportedException("Equal method is not supported.");
                }

                global::Advantech.Motion.Motion.mAcm_AxSetCmpTable(
                                                        AdvantechAxisCard.axisHandles[AxisID],
                                                        positions,
                                                        positions.Length);


                global::Advantech.Motion.Motion.mAcm_SetProperty(
                                                        AdvantechAxisCard.axisHandles[AxisID],
                                                        (uint)global::Advantech.Motion.PropertyID.CFG_AxCmpSrc,
                                                        ref compareSource,
                                                        (uint)Marshal.SizeOf(typeof(int)));


                global::Advantech.Motion.Motion.mAcm_SetProperty(
                                                        AdvantechAxisCard.axisHandles[AxisID],
                                                        (uint)global::Advantech.Motion.PropertyID.CFG_AxCmpMethod,
                                                        ref compareMethod,
                                                        (uint)Marshal.SizeOf(typeof(int)));

                global::Advantech.Motion.Motion.mAcm_SetProperty(
                                                        AdvantechAxisCard.axisHandles[AxisID],
                                                        (uint)global::Advantech.Motion.PropertyID.CFG_AxCmpEnable,
                                                        ref compareStatus,
                                                        (uint)Marshal.SizeOf(typeof(int)));
            });            
        }
      public bool Home()
      {
         //AdvantechAxisCard.LockProtect();

         //uint err = Motion.mAcm_AxHome(AdvantechAxisCard.axisHandles[AxisID], (uint)HomeMode, 1);
         //bool bSuccess = true;
         //if (err != (uint)ErrorCode.SUCCESS)
         //{
         //    bSuccess = false;
         //    throw new InvalidOperationException("Advantech Axis Card Home error");
         //}
         //else
         //    m_CmdPosition = 0;
         //AdvantechAxisCard.UnLock();                       
         //return bSuccess;



         return AdvantechAxisCard.Protect(() =>
         {

             bool bSuccess = true;
             ushort statusCode=0;               
             Motion.mAcm_AxGetState(AdvantechAxisCard.axisHandles[AxisID], ref statusCode);
             //STA_AxHoming
             if (statusCode == 4 )
                 return bSuccess;

             if (HomeMode == -1)
                 return bSuccess;

             double CrossDistance = 3000;
             ErrorCode err = (ErrorCode)global::Advantech.Motion.Motion.mAcm_SetProperty(
                                                     AdvantechAxisCard.axisHandles[AxisID],
                                                     (uint)global::Advantech.Motion.PropertyID.PAR_AxHomeCrossDistance,
                                                     ref CrossDistance,
                                                     (uint)Marshal.SizeOf(typeof(double)));
              err = (ErrorCode)Motion.mAcm_AxHome(AdvantechAxisCard.axisHandles[AxisID], (uint)HomeMode, 1);



             if (err != ErrorCode.SUCCESS)
            {
               bSuccess = false;
               throw new InvalidOperationException("Advantech Axis Card Home error");
            }
            else
               m_CmdPosition = 0;
            return bSuccess;


         });
      }

      public bool MotMoveAbs(double Pos)
      {

         //AdvantechAxisCard.LockProtect();
         //Pos = (int)(Pos / Scale);
         //bool bSuccess = true;
         ////    SetVelocity(OperationSpeed, OperationStartSpeed, OperationAcc, OperationDec, Curve);


         //if (m_CmdPosition != Pos)
         //{
         //    uint err = (uint)Motion.mAcm_AxMoveAbs(AdvantechAxisCard.axisHandles[AxisID], Pos);

         //    if (err != (uint)ErrorCode.SUCCESS)
         //    {
         //        bSuccess = false;
         //        throw new InvalidOperationException("Advantech Axis Card MotMoveAbsolute error");
         //    }
         //    else
         //    {
         //        m_CmdPosition = Pos;
         //    }
         //}
         //AdvantechAxisCard.UnLock();
         //return bSuccess;

         return AdvantechAxisCard.Protect(() =>
         {

            double Pos1 = (int)(Pos / Scale);
            bool bSuccess = true;
            //    SetVelocity(OperationSpeed, OperationStartSpeed, OperationAcc, OperationDec, Curve);
            bool bExcute = true;

            if (GetRealPosition() != Pos)

            {
               if (AdvantechAxisCard.CardName[AxisID].Contains("1245"))
               {
                  if (GetPLimit()/*GetMotionIO(2) */)//P                     
                  {
                     if (GetRealPosition() < Pos)
                        bExcute = false;
                     else
                     {
                        ErrorCode err1 = (ErrorCode)Motion.mAcm_AxResetError(AdvantechAxisCard.axisHandles[AxisID]);
                     }
                  }
                  if (GetNLimit() /*GetMotionIO(3)*/)//N
                  {
                     if (GetRealPosition() > Pos)
                        bExcute = false;
                     else
                     {
                        ErrorCode err1 = (ErrorCode)Motion.mAcm_AxResetError(AdvantechAxisCard.axisHandles[AxisID]);
                     }
                  }
               }
            }
            else
               bExcute = false;







            if (bExcute)
            {
               ErrorCode err = (ErrorCode)Motion.mAcm_AxMoveAbs(AdvantechAxisCard.axisHandles[AxisID], Pos1);

               if (err != ErrorCode.SUCCESS)
               {
                  bSuccess = false;
                  throw new InvalidOperationException("Advantech Axis Card MotMoveAbsolute error");
               }
               else
               {
                  m_CmdPosition = Pos;
               }
            }
            return bSuccess;
         });


      }

      public bool MotMoveRel(double Pos)
      {
         //AdvantechAxisCard.LockProtect();
         //bool bSuccess = true;
         //Pos = (int)(Pos / Scale);


         //int cmd = (int)Pos;
         //if (cmd != 0)
         //{

         //    uint err = (uint)Motion.mAcm_AxMoveRel(AdvantechAxisCard.axisHandles[AxisID], cmd);

         //    if (err != (uint)ErrorCode.SUCCESS)
         //    {
         //        bSuccess = false;
         //        throw new InvalidOperationException("Advantech Axis Card MotMoveRel error");
         //    }
         //    else
         //        m_CmdPosition = m_CmdPosition + Pos;

         //}


         //AdvantechAxisCard.UnLock();
         //return bSuccess;

         return AdvantechAxisCard.Protect(() =>
         {

            bool bSuccess = true;
            double Pos1 = (int)(Pos / Scale);


            bool bExcute = true;
            int cmd = (int)Pos1;
            if (cmd != 0)
            {
               if (AdvantechAxisCard.CardName[AxisID].Contains("1245"))
               {
                  if (GetPLimit()/*GetMotionIO(2) */)//P                     
                  {
                     if (Pos1 > 0)
                        bExcute = false;
                     else
                     {
                        ErrorCode err1 = (ErrorCode)Motion.mAcm_AxResetError(AdvantechAxisCard.axisHandles[AxisID]);
                     }
                  }
                  if (GetNLimit() /*GetMotionIO(3)*/)//N
                  {
                     if (Pos1 < 0)
                        bExcute = false;
                     else
                     {
                        ErrorCode err1 = (ErrorCode)Motion.mAcm_AxResetError(AdvantechAxisCard.axisHandles[AxisID]);
                     }
                  }
               }
            }
            else
               bExcute = false;




            ErrorCode err = ErrorCode.SUCCESS;


            if (bExcute)
            {
               err = (ErrorCode)Motion.mAcm_AxMoveRel(AdvantechAxisCard.axisHandles[AxisID], cmd);

               if (err != ErrorCode.SUCCESS)
               {
                  bSuccess = false;
                  throw new InvalidOperationException("Advantech Axis Card MotMoveRel error");
               }
               else
                  m_CmdPosition = m_CmdPosition + Pos;
            }




            return bSuccess;
         });

      }



      public void SetSVON(bool State)
      {
         //AdvantechAxisCard.LockProtect();


         //uint err = (uint)Motion.mAcm_AxSetSvOn(AdvantechAxisCard.axisHandles[AxisID], (uint)(State ? 1 : 0));
         //uint aa = (uint)ErrorCode.SUCCESS;
         //if (err != aa)

         //    throw new InvalidOperationException("Advantech Axis Card SetSVON error");

         //AdvantechAxisCard.UnLock();

         AdvantechAxisCard.Protect(() =>
         {

            ErrorCode err = (ErrorCode)Motion.mAcm_AxSetSvOn(AdvantechAxisCard.axisHandles[AxisID], (uint)(State ? 1 : 0));

            if (err != ErrorCode.SUCCESS)
               throw new InvalidOperationException("Advantech Axis Card SetSVON error");


         });


      }

      public bool Wait()
      {
         //AdvantechAxisCard.LockProtect();
         //bool Wait = false;
         //ushort statusCode = 0;
         //Motion.mAcm_AxGetState(AdvantechAxisCard.axisHandles[AxisID], ref statusCode);
         //if (statusCode == 3)
         //{
         //    uint err = Motion.mAcm_AxResetError(AdvantechAxisCard.axisHandles[AxisID]);
         //    if (err != (uint)ErrorCode.SUCCESS)
         //        throw new InvalidOperationException("Advantech Axis Card ResetError error");

         //}
         //switch (statusCode)
         //{
         //    case 0: // Disable
         //    case 1: // Ready                
         //    case 3:
         //        Wait = true; // ErrorStop
         //        break;
         //    case 2: // Stopping
         //    case 4: // Homing
         //    case 5: // PTPMotion
         //    case 6: // 軸正在進行連續動作。
         //    case 7: // SyncMotion
         //    case 8: // EXT JOG
         //    case 9: // EXT MPG
         //    case 11: // Unknow
         //        Wait = false;
         //        break;

         //    default:
         //        {
         //            throw new InvalidOperationException("Failed to get axis status.");
         //        }
         //}
         //AdvantechAxisCard.UnLock();
         //return Wait;


         return AdvantechAxisCard.Protect(() =>
         {

            bool Wait = false;
            ushort statusCode = 0;
            Motion.mAcm_AxGetState(AdvantechAxisCard.axisHandles[AxisID], ref statusCode);
            if (statusCode == 3)
            {
               ErrorCode err = (ErrorCode)Motion.mAcm_AxResetError(AdvantechAxisCard.axisHandles[AxisID]);
               if (err != ErrorCode.SUCCESS)
                  throw new InvalidOperationException("Advantech Axis Card ResetError error");

            }
            switch (statusCode)
            {
               case 0: // Disable
               case 1: // Ready                
               case 3:
                  Wait = true; // ErrorStop
                  break;
               case 2: // Stopping
               case 4: // Homing
               case 5: // PTPMotion
               case 6: // 軸正在進行連續動作。
               case 7: // SyncMotion
               case 8: // EXT JOG
               case 9: // EXT MPG
               case 11: // Unknow
                  Wait = false;
                  break;

               default:
                  {
                     throw new InvalidOperationException("Failed to get axis status.");
                  }
            }

            return Wait;


         });
      }
        public void ResetError()
        {
            ErrorCode err = (ErrorCode)Motion.mAcm_AxResetError(AdvantechAxisCard.axisHandles[AxisID]);
            if (err != ErrorCode.SUCCESS)
                throw new InvalidOperationException("Advantech Axis Card ResetError error");
        }

      public void SetVelocity(double MaxVel, double StrVel, double AccTime, double DecTime, CurveType Curve)
      {
         //AdvantechAxisCard.LockProtect();

         //// 因統一Velocity帶入秒數，但研華Acc & Dec 單位為Pulse / Sec² 因此在此作處理。
         //double accUnit = Math.Abs((MaxVel - StrVel) / AccTime);
         //double decUnit = Math.Abs((MaxVel - StrVel) / DecTime);


         //uint err = Motion.mAcm_SetProperty(
         //    AdvantechAxisCard.axisHandles[AxisID], (uint)PropertyID.PAR_AxAcc,
         //    ref accUnit, (uint)Marshal.SizeOf(typeof(double)));

         //err = Motion.mAcm_SetProperty(
         //    AdvantechAxisCard.axisHandles[AxisID], (uint)PropertyID.PAR_AxDec,
         //    ref decUnit, (uint)Marshal.SizeOf(typeof(double)));

         //if (err != (uint)ErrorCode.SUCCESS)
         //{

         //    throw new InvalidOperationException("Advantech Axis Card SetVelocity error");
         //}

         //AdvantechAxisCard.UnLock();
         ///    Thread.Sleep(10);


         AdvantechAxisCard.Protect(() =>
         {


            // 因統一Velocity帶入秒數，但研華Acc & Dec 單位為Pulse / Sec² 因此在此作處理。
            double accUnit = Math.Abs((MaxVel - StrVel) / AccTime);
            double decUnit = Math.Abs((MaxVel - StrVel) / DecTime);


            ErrorCode err = (ErrorCode)Motion.mAcm_SetProperty(
                   AdvantechAxisCard.axisHandles[AxisID], (uint)PropertyID.PAR_AxAcc,
                   ref accUnit, (uint)Marshal.SizeOf(typeof(double)));

            err = (ErrorCode)Motion.mAcm_SetProperty(
                   AdvantechAxisCard.axisHandles[AxisID], (uint)PropertyID.PAR_AxDec,
                   ref decUnit, (uint)Marshal.SizeOf(typeof(double)));

            if (err != ErrorCode.SUCCESS)
            {

               throw new InvalidOperationException("Advantech Axis Card SetVelocity error");
            }

         });
      }

      public void SetMaxVel(double dMaxVel)
      {
         //AdvantechAxisCard.LockProtect();

         //CurrrentSpeed = dMaxVel / Scale;


         //uint err = Motion.mAcm_SetProperty(AdvantechAxisCard.axisHandles[AxisID], (uint)PropertyID.PAR_AxVelHigh,
         //                                     ref CurrrentSpeed, (uint)Marshal.SizeOf(typeof(double)));


         //if (err != (uint)ErrorCode.SUCCESS)
         //{
         //    string errMessage = "Unknow Error";
         //    if (Enum.IsDefined(typeof(global::Advantech.Motion.ErrorCode), err))
         //    {
         //        errMessage = ((global::Advantech.Motion.ErrorCode)err).ToString();
         //    }
         //    throw new InvalidOperationException("Advantech Axis Card SetMaxVel error");
         //}
         //AdvantechAxisCard.UnLock();
         AdvantechAxisCard.Protect(() =>
         {

            CurrrentSpeed = dMaxVel / Scale;

            ErrorCode err = (ErrorCode)Motion.mAcm_SetProperty(AdvantechAxisCard.axisHandles[AxisID], (uint)PropertyID.PAR_AxVelHigh,
                                                    ref CurrrentSpeed, (uint)Marshal.SizeOf(typeof(double)));


            if (err != ErrorCode.SUCCESS)
            {
               string errMessage = "Unknow Error";
               if (Enum.IsDefined(typeof(global::Advantech.Motion.ErrorCode), err))
               {
                  errMessage = ((global::Advantech.Motion.ErrorCode)err).ToString();
               }
               throw new InvalidOperationException("Advantech Axis Card SetMaxVel error");
            }

         });

         //SetVelocity(CurrrentSpeed, CurrrentStrSpeed, CurrrentAcc, CurrrentDec, Curve);


      }

      public void SetStrVel(double dStrVel)
      {
         //        AdvantechAxisCard.LockProtect();

         //        CurrrentStrSpeed = dStrVel / Scale;


         //        uint err = Motion.mAcm_SetProperty(
         //AdvantechAxisCard.axisHandles[AxisID], (uint)PropertyID.PAR_AxVelLow,
         //ref CurrrentStrSpeed, (uint)Marshal.SizeOf(typeof(double)));



         //        if (err != (uint)ErrorCode.SUCCESS)
         //        {

         //            throw new InvalidOperationException("Advantech Axis Card SetStrVel error");
         //        }


         //        AdvantechAxisCard.UnLock();

         AdvantechAxisCard.Protect(() =>
         {

            CurrrentStrSpeed = dStrVel / Scale;


            ErrorCode err = (ErrorCode)Motion.mAcm_SetProperty(
       AdvantechAxisCard.axisHandles[AxisID], (uint)PropertyID.PAR_AxVelLow,
       ref CurrrentStrSpeed, (uint)Marshal.SizeOf(typeof(double)));



            if (err != ErrorCode.SUCCESS)
            {

               throw new InvalidOperationException("Advantech Axis Card SetStrVel error");
            }


         });

         SetVelocity(CurrrentSpeed, CurrrentStrSpeed, CurrrentAcc, CurrrentDec, Curve);

      }

      public void SetAccTime(double dAccTime)
      {


         CurrrentAcc = dAccTime;

         SetVelocity(CurrrentSpeed, CurrrentStrSpeed, CurrrentAcc, CurrrentDec, Curve);

      }

      public void SetDecTime(double dDecTime)
      {
         CurrrentDec = dDecTime;

         SetVelocity(CurrrentSpeed, CurrrentStrSpeed, CurrrentAcc, CurrrentDec, Curve);
      }

      public void SetCurve(CurveType sCurve)
      {
         Curve = sCurve;

         /*
         AdvantechAxisCard.LockProtect();
         Curve = sCurve;
         double jerk = sCurve == CurveType.T_Curve ? 0 : 1;

         uint err = Motion.mAcm_SetProperty(AdvantechAxisCard.axisHandles [AxisID], (uint)PropertyID.PAR_AxJerk,
                                             ref jerk, (uint)Marshal.SizeOf(typeof(double)));

         if (err != (uint)ErrorCode.SUCCESS)
         {
             //throw new InvalidOperationException("Advantech Axis Card SetCurve error");
         }
         AdvantechAxisCard.UnLock();*/

      }
        public bool GetIOStatus(int ID)
        {
            byte status = 0;
            AdvantechAxisCard.Protect(() =>
            {
                if (ID < 0 || ID > 7) throw new IndexOutOfRangeException("[GetIOStatus]IO索引值超出範圍(0~7)");
                ushort id = (ushort)ID;

                ErrorCode err;
                if (id < 4)
                    err = (ErrorCode)Motion.mAcm_AxDiGetBit(AdvantechAxisCard.axisHandles[AxisID], id, ref status);
                else
                    err = (ErrorCode)Motion.mAcm_AxDoGetBit(AdvantechAxisCard.axisHandles[AxisID], id, ref status);

                if (err != ErrorCode.SUCCESS)
                    throw new InvalidOperationException($"Advantech AxDiGetBit error:{err}");

            });
            return Convert.ToBoolean(status);
        }
        public bool GetPLimit()
      {

         return AdvantechAxisCard.Protect(() => GetMotionIO(2));

      }
      public bool GetNLimit()
      {
         return AdvantechAxisCard.Protect(() => GetMotionIO(3));

      }
      public bool GetOrg()
      {
         return AdvantechAxisCard.Protect(() => GetMotionIO(4));
      }
      public bool GetSVON()
      {
         return AdvantechAxisCard.Protect(() => GetMotionIO(14));
      }
      public bool GetINP()
      {
         return AdvantechAxisCard.Protect(() => GetMotionIO(13));
      }
      public bool GetRDY()
      {
         return AdvantechAxisCard.Protect(() => GetMotionIO(0));
      }
      public bool GetAlarm()
      {
         return AdvantechAxisCard.Protect(() => GetMotionIO(1));
      }
      public bool GetTrigger()
      {
         return AdvantechAxisCard.Protect(() => GetMotionIO(18));
      }

      public bool GetMotionIO(int bit)
      {
      //AdvantechAxisCard.LockProtect();
      //uint ioStatus = 0;
      //uint err = (uint)Motion.mAcm_AxGetMotionIO(AdvantechAxisCard.axisHandles[AxisID], ref ioStatus);


      //if (err != (uint)ErrorCode.SUCCESS)
      //    throw new InvalidOperationException("Advantech Axis Card GetMotionIO error");

      //long status = ioStatus & (uint)Math.Pow(2, bit);
      //bool Action = (status == 0) ? false : true;
      //AdvantechAxisCard.UnLock();
      //return Action;


      //  return AdvantechAxisCard.Protect(() =>
      //  {
      bool Action = false;
      AdvantechAxisCard.Protect(() =>
      {
        uint ioStatus = 0;
         ErrorCode err = (ErrorCode)Motion.mAcm_AxGetMotionIO(AdvantechAxisCard.axisHandles[AxisID], ref ioStatus);

         if (err != ErrorCode.SUCCESS)
            throw new InvalidOperationException("Advantech Axis Card GetMotionIO error");

         long status = ioStatus & (uint)Math.Pow(2, bit);
         Action = (status == 0) ? false : true;

         


          });
        return Action;
      }

      public void SetPosition(double Pos)
      {
         //Pos = Pos / Scale;
         //uint err = Motion.mAcm_AxSetCmdPosition(AdvantechAxisCard.axisHandles[AxisID], Pos);
         //if (err != (uint)ErrorCode.SUCCESS)
         //    throw new InvalidOperationException("Advantech Axis Card SetCmdPosition error");
         //err = Motion.mAcm_AxSetActualPosition(AdvantechAxisCard.axisHandles[AxisID], Pos);
         //if (err != (uint)ErrorCode.SUCCESS)
         //    throw new InvalidOperationException("Advantech Axis Card SetActualPosition error");
         AdvantechAxisCard.Protect(() =>
         {

            Pos = Pos / Scale;
            ErrorCode err = (ErrorCode)Motion.mAcm_AxSetCmdPosition(AdvantechAxisCard.axisHandles[AxisID], Pos);
            if (err != ErrorCode.SUCCESS)
               throw new InvalidOperationException("Advantech Axis Card SetCmdPosition error");
            err = (ErrorCode)Motion.mAcm_AxSetActualPosition(AdvantechAxisCard.axisHandles[AxisID], Pos);
            if (err != ErrorCode.SUCCESS)
               throw new InvalidOperationException("Advantech Axis Card SetActualPosition error");

         });

      }
      public void SetDO(int ID, bool Status)
      {

         //AdvantechAxisCard.LockProtect();
         //ushort id = (ushort)ID;
         //byte status = 0;
         //if (Status)
         //    status = 1;
         //else
         //    status = 0;

         ////Acm_AxDoSetBit(HAND AxisHandle, U16 DoChannel, U8 Bit -Data)

         //uint err = (uint)Motion.mAcm_AxDoSetBit(AdvantechAxisCard.axisHandles[AxisID], id, status);
         //if (err != (uint)ErrorCode.SUCCESS)
         //    throw new InvalidOperationException("Advantech Axis Card AxDoSetBit error");

         ////long status = ioStatus & (uint)Math.Pow(2, bit);
         ////  bool Action = (status == 0) ? false : true;
         //AdvantechAxisCard.UnLock();

         AdvantechAxisCard.Protect(() =>
         {

            ushort id = (ushort)ID;
            byte status = 0;
            if (Status)
               status = 1;
            else
               status = 0;

            ErrorCode err = (ErrorCode)Motion.mAcm_AxDoSetBit(AdvantechAxisCard.axisHandles[AxisID], id, status);
            if (err != ErrorCode.SUCCESS)
               throw new InvalidOperationException("Advantech Axis Card AxDoSetBit error");


         });


      }
      public void MotStop(bool isImmediate = false)
      {


         //if (isImmediate)
         //{

         //    uint err = Motion.mAcm_AxStopEmg(AdvantechAxisCard.axisHandles[AxisID]);
         //    if (err != (uint)ErrorCode.SUCCESS)
         //        throw new InvalidOperationException("Advantech Axis Card AxStopEmg error");
         //    else
         //    {
         //        m_CmdPosition = GetLogicPosition();
         //    }


         //}
         //else
         //{
         //    uint err = Motion.mAcm_AxStopDec(AdvantechAxisCard.axisHandles[AxisID]);
         //    if (err != (uint)ErrorCode.SUCCESS)
         //        throw new InvalidOperationException("Advantech Axis Card AxStopDec error");
         //    else
         //    {
         //        m_CmdPosition = GetLogicPosition();
         //    }
         //}
         AdvantechAxisCard.Protect(() =>
         {
            if (isImmediate)
            {

               ErrorCode err = (ErrorCode)Motion.mAcm_AxStopEmg(AdvantechAxisCard.axisHandles[AxisID]);
               if (err != ErrorCode.SUCCESS)
                  throw new InvalidOperationException("Advantech Axis Card AxStopEmg error");
               else
               {
                  //m_CmdPosition = GetRealPosition ();
               }


            }
            else
            {
               ErrorCode err = (ErrorCode)Motion.mAcm_AxStopDec(AdvantechAxisCard.axisHandles[AxisID]);
               if (err != ErrorCode.SUCCESS)
                  throw new InvalidOperationException("Advantech Axis Card AxStopDec error");
               else
               {
                  //m_CmdPosition = GetRealPosition();
               }
            }

         });

         //return;
      }



      #region IDisposable Support
      private bool disposedValue = false; // 偵測多餘的呼叫

      protected virtual void Dispose(bool disposing)
      {
         if (!disposedValue)
         {
            if (disposing)
            {
               // TODO: 處置受控狀態 (受控物件)。

               MotMoveAbs(0);

            }

            // TODO: 釋放非受控資源 (非受控物件) 並覆寫下方的完成項。
            // TODO: 將大型欄位設為 null。

            disposedValue = true;
         }
      }

      // TODO: 僅當上方的 Dispose(bool disposing) 具有會釋放非受控資源的程式碼時，才覆寫完成項。
      // ~cAxis_PCI1245() {
      //   // 請勿變更這個程式碼。請將清除程式碼放入上方的 Dispose(bool disposing) 中。
      //   Dispose(false);
      // }

      // 加入這個程式碼的目的在正確實作可處置的模式。
      public void Dispose()
      {
         // 請勿變更這個程式碼。請將清除程式碼放入上方的 Dispose(bool disposing) 中。
         Dispose(true);
         // TODO: 如果上方的完成項已被覆寫，即取消下行的註解狀態。
         // GC.SuppressFinalize(this);
      }



      public bool GetEmergency()
      {

         return AdvantechAxisCard.Protect(() => GetMotionIO(6));


      }


      #endregion



      public void MotPrevious()
      {


         if (m_CmdPosition != GetRealPosition())
            _ = MotMoveAbs(m_CmdPosition);

      }

      public double GetTargetPosition()
      {
         return m_CmdPosition;
      }
      #endregion Public  Methods
   }


}