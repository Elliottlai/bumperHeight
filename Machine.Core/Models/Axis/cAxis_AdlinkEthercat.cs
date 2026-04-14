using APS_Define_W32;
using APS168_W64;
using Machine.Core.Enums;
using Machine.Core.Interfaces;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Machine.Core

{

    public class cAxis_AdlinkEthercat : IAxis, IDisposable

    {

        #region Member Variables

        // private IntPtr AdvantechAxisCard.axisHandles [AxisID] { set; get; }

        public string UID { set; get; }

        public string Name { set; get; }

        public AxisCardType Type => AxisCardType.AdlinkEthercat;

        //   public int CardID { set; get; }

        public int AxisID { set; get; }

        public int HomeMode { set; get; }
        //double homeSpeed;
        //double homeStartSpeed;
        //double homeBuffer;
        //double operationSpeed;
        //double operationStartSpeed;
        //double softwareNLimit;
        //double softwarePLimit;

        public double HomeSpeed { get; set; }   //mm/s
        public double HomeStartSpeed { get; set; }  //mm/s
        public double HomeAcc { get; set; }
        public double HomeDec { get; set; }
        public double HomeBuffer { get; set; }
        public double OperationSpeed { get; set; }
        public double OperationStartSpeed { get; set; }
        public double OperationAcc { get; set; }
        public double OperationDec { get; set; }
        public double Scale { get; set; }
        public double Tolerance { get; set; }


        private double CurrrentSpeed_pulse;
        private double CurrrentStrSpeed_pulse;
        private double CurrrentAcc_sec;
        private double CurrrentDec_sec;

        CurveType curve;
        public CurveType Curve
        {
            get => curve;
            set
            {
                AdlinkEtherCATCard.Protect(() =>
                {
                    curve = value;
                    int ret = APS168.APS_set_axis_param_f(AxisID, (Int32)APS_Define.PRA_CURVE, curve == CurveType.T_Curve ? 0 : 1);
                });
            }
        }
        public double SoftwareNLimit { get; set; }

        public double SoftwarePLimit { get; set; }

        double m_CmdPosition;
        #endregion Member Variables
        #region Public  Methods
        public bool GetAlarm()
        {
            return  AdlinkEtherCATCard.GetMotion_IO_Status(AxisID, eMotion_IO_Status.ALM) ;
        }

        public bool GetEmergency()
        {
            return AdlinkEtherCATCard.GetMotion_IO_Status(AxisID, eMotion_IO_Status.EMG);
        }

        public bool GetINP()
        {
            return AdlinkEtherCATCard.GetMotion_IO_Status(AxisID, eMotion_IO_Status.INP);
        }

        public bool GetIOStatus(int ID)
        {
            return AdlinkEtherCATCard.GetMotion_IO_Status(AxisID, (eMotion_IO_Status)ID);
        }

        public double GetLogicPosition()
        {
            return AdlinkEtherCATCard.Protect(() =>
            {
                double cmd = 0;
                APS168.APS_get_command_f(AxisID, ref cmd);
                return cmd * Scale;
            });
        }

        public bool GetNLimit()
        {
            return  AdlinkEtherCATCard.GetMotion_IO_Status(AxisID, eMotion_IO_Status.MEL);
        }

        public bool GetOrg()
        {
            return AdlinkEtherCATCard.GetMotion_IO_Status(AxisID, eMotion_IO_Status.ORG);
        }

        public bool GetPLimit()
        {
            return  AdlinkEtherCATCard.GetMotion_IO_Status(AxisID, eMotion_IO_Status.PEL);
        }

        public bool GetRDY()
        {
            return   AdlinkEtherCATCard.GetMotion_IO_Status(AxisID, eMotion_IO_Status.RDY);
        }

        public double GetRealPosition()
        {
            return AdlinkEtherCATCard.Protect(() =>
            {
                double Position = 0;
                APS168.APS_get_position_f(AxisID, ref Position);
                return Position * Scale;
            });
        }

        public bool GetSVON()
        {
            return   AdlinkEtherCATCard.GetMotion_IO_Status(AxisID, eMotion_IO_Status.SVON) ;
        }

        public double GetTargetPosition()
        {
            return m_CmdPosition;
        }

        public bool GetTrigger()
        {
            throw new NotImplementedException();
        }


        public void SetVelocity(double MaxVel, double StrVel, double AccTime, double DecTime)
        {
            AdlinkEtherCATCard.Protect(() =>
            {

                double accUnit = Math.Abs((MaxVel - StrVel) / AccTime);
                double decUnit = Math.Abs((MaxVel - StrVel) / DecTime);



                int ret = APS168.APS_set_axis_param_f(AxisID, (Int32)APS_Define.PRA_ACC, accUnit);
                ret = APS168.APS_set_axis_param_f(AxisID, (Int32)APS_Define.PRA_DEC, decUnit);
            });





        }


        public bool Home()
        {

            return AdlinkEtherCATCard.Protect(() =>
            {

                Int32 axis_id = AxisID;
                Int32 return_code = 0;
                if (((APS168.APS_motion_io_status(axis_id) >> (Int32)APS_Define.MIO_SVON) & 1) == 0)
                {
                    APS168.APS_set_servo_on(axis_id, 1);
                    Thread.Sleep(500); // Wait stable.
                }
                MotStop();
                if (HomeMode != -1)
                {
                    // 1. Select home mode and config home parameters 
                    return_code = APS168.APS_set_axis_param(axis_id, (Int32)APS_Define.PRA_HOME_MODE, 4); // Set home mode
                    return_code = APS168.APS_set_axis_param(axis_id, (Int32)APS_Define.PRA_HOME_DIR, 1); // Set home direction
                    return_code = APS168.APS_set_axis_param_f(axis_id, (Int32)APS_Define.PRA_HOME_CURVE, 0); // Set acceleration paten (T-curve)
                    return_code = APS168.APS_set_axis_param_f(axis_id, (Int32)APS_Define.PRA_HOME_ACC, HomeSpeed / Scale / HomeAcc); // Set homing acceleration rate
                    return_code = APS168.APS_set_axis_param_f(axis_id, (Int32)APS_Define.PRA_HOME_VM, HomeSpeed / Scale *1.1); // Set homing maximum velocity.
                    return_code = APS168.APS_set_axis_param_f(axis_id, (Int32)APS_Define.PRA_HOME_VO, HomeStartSpeed / Scale); // Set homing VO speed
                    return_code = APS168.APS_set_axis_param(axis_id, (Int32)APS_Define.PRA_HOME_EZA, 0); // Set EZ signal alignment (yes or no)
                    return_code = APS168.APS_set_axis_param_f(axis_id, (Int32)APS_Define.PRA_HOME_SHIFT, 0); // Set home position shfit distance. 
                    return_code = APS168.APS_set_axis_param_f(axis_id, (Int32)APS_Define.PRA_HOME_POS, 0); // Set final home position.                    

                    //servo on

                    // 2. Start home move
                    return_code = APS168.APS_home_move(axis_id); //Start homing 
                    if (return_code != (Int32)APS_Define.ERR_NoError)
                    { /* Error handling */
                        ;
                    }
                    m_CmdPosition = 0;



                    int MotionStatus=0;
                    do
                    {
                          MotionStatus = APS168.APS_motion_status(axis_id); //获取运动状态
                     
                    } while ((MotionStatus >> 5 & 0x1) == 0);


                }
                return true;
            });
        }

        public bool MotMoveAbs(double Pos)
        {
            return AdlinkEtherCATCard.Protect(() =>
            {
                Int32 Axis_ID = AxisID;

                Int32 ret = 0;
                ASYNCALL p = new ASYNCALL();

                // Config speed profile parameters.
                //ret = APS168.APS_set_axis_param_f(Axis_ID, (Int32)APS_Define.PRA_CURVE, curve==CurveType.T_Curve ?0:1);
                //// ret = APS168.APS_set_axis_param_f(Axis_ID, (Int32)APS_Define.PRA_SF, 0.5);

                //ret = APS168.APS_set_axis_param_f(Axis_ID, (Int32)APS_Define.PRA_ACC, CurrrentAcc_pulse);

                //ret = APS168.APS_set_axis_param_f(Axis_ID, (Int32)APS_Define.PRA_DEC, CurrrentDec_pulse);

                //ret = APS168.APS_set_axis_param_f(Axis_ID, (Int32)APS_Define.PRA_VS, CurrrentStrSpeed_pulse);

                //ret = APS168.APS_set_axis_param_f(Axis_ID, (Int32)APS_Define.PRA_VM, CurrrentSpeed_pulse );

                ////servo on
                //if (((APS168.APS_motion_io_status(Axis_ID) >> (Int32)APS_Define.MIO_SVON) & 1) == 0)
                //{
                //    APS168.APS_set_servo_on(Axis_ID, 1);
                //    Thread.Sleep(500); // Wait stable.
                //}

                // Start a relative p to p move

                double Pos1 = (int)(Pos / Scale);
                ret = APS168.APS_ptp(Axis_ID, (Int32)APS_Define.OPT_ABSOLUTE, Pos1, ref p);
                // ret = APS168.APS_ptp_v(Axis_ID, (Int32)APS_Define.OPT_ABSOLUTE +(Int32) APS_Define.PTP_OPT_BLEND_NEXT, CurrrentSpeed_pulse,  Pos1, ref p);
                m_CmdPosition = Pos;
                return true;
            });
        }

        public bool MotMoveRel(double Pos)
        {
            return AdlinkEtherCATCard.Protect(() =>
            {
                ASYNCALL p = new ASYNCALL();
                double Pos1 = (int)(Pos / Scale);
                int ret = APS168.APS_ptp(AxisID, (Int32)APS_Define.OPT_RELATIVE, Pos1, ref p);

                //  ret = APS168.APS_ptp_v(AxisID, (Int32)APS_Define.OPT_RELATIVE + (Int32)APS_Define.PTP_OPT_BLEND_NEXT, CurrrentSpeed_pulse, Pos1, ref p);
                m_CmdPosition = m_CmdPosition + Pos;
                return true;
            });
        }

        public void MotPrevious()
        {

            if (m_CmdPosition != GetRealPosition())
                _ = MotMoveAbs(m_CmdPosition);
        }

        public void MotStop(bool isImmediate = false)
        {
            AdlinkEtherCATCard.Protect(() =>
           {
               int dec = 0;
               APS168.APS_get_axis_param(AxisID, (Int32)APS_Define.PRA_DEC, ref dec); // Set home mode

               int dec2 = 0;
               APS168.APS_get_axis_param(AxisID, (Int32)APS_Define.PRA_SD_DEC, ref dec2); // Set home mode

               APS168.APS_set_axis_param(AxisID, (Int32)APS_Define.PRA_SD_DEC, dec); // Set home mode
               APS168.APS_stop_move(AxisID);
               APS168.APS_set_axis_param(AxisID, (Int32)APS_Define.PRA_SD_DEC, dec2); // Set home mode
           });
        }

        public void SetAccTime(double Value)
        {
            CurrrentAcc_sec = Value;
            SetVelocity(CurrrentSpeed_pulse, CurrrentStrSpeed_pulse, CurrrentAcc_sec, CurrrentDec_sec);
        }

        public void SetCurve(CurveType Curve)
        {
            curve = Curve;
        }

        public void SetDecTime(double Value)
        {
            CurrrentDec_sec = Value;
            SetVelocity(CurrrentSpeed_pulse, CurrrentStrSpeed_pulse, CurrrentAcc_sec, CurrrentDec_sec);
        }

        public void SetDO(int ID, bool OnorOff)
        {
            //throw new NotImplementedException();
        }

        public void SetMaxVel(double Value)
        {

            AdlinkEtherCATCard.Protect(() =>
            {
                CurrrentSpeed_pulse = Value / Scale;

                int ret = APS168.APS_set_axis_param_f(AxisID, (Int32)APS_Define.PRA_VM, CurrrentSpeed_pulse);
            });


        }

        public void SetPosition(double Pos)
        {
            AdlinkEtherCATCard.Protect(() =>
            {
                Pos = Pos / Scale;

                APS168.APS_set_command_f(AxisID, Pos);
                APS168.APS_set_position_f(AxisID, Pos);
            });
        }

        public void SetStrVel(double Value)
        {
            AdlinkEtherCATCard.Protect(() =>
            {
                CurrrentStrSpeed_pulse = Value / Scale;

                int ret = APS168.APS_set_axis_param_f(AxisID, (Int32)APS_Define.PRA_VS, CurrrentStrSpeed_pulse);
            });

        }

        public void SetSVON(bool OnorOff)
        {
            AdlinkEtherCATCard.Protect(() =>
            {


                int ret = APS168.APS_set_servo_on(AxisID, OnorOff ? 1 : 0);
            });

        }

        public void SetTrigger(double Position, int CompareMethods)
        {
            //throw new NotImplementedException();
        }

        public bool Wait()
        {
            bool ok=false;
 
                ok = AdlinkEtherCATCard.GetMotionStatus(AxisID, eMotion_Status.MDN);
        
            return ok;
        }

        public void Dispose()
        {
 
                AdlinkEtherCATCard.CloseDevice();
                Thread.Sleep(500);
                 
 
       
        }
        ~cAxis_AdlinkEthercat() {

            Dispose();
        }

        public void ResetError()
        {
            AdlinkEtherCATCard.Protect(() =>
            {
                APS168.APS_reset_emx_alarm(AxisID);
            });
        }
        #endregion Public  Methods
    }
}
