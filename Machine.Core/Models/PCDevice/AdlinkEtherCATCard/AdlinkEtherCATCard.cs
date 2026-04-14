using APS_Define_W32;
using APS168_W64;
using Machine.Core.Enums;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Machine.Core
{
    public class AdlinkEtherCATCard : IDisposable
    {
        /////Main form data/////////////////////////////////////////////////////////////
        const Int32 YES = 1;
        const Int32 NO = 0;
        const Int32 ON = 1;
        const Int32 OFF = 0;

        static Int32 v_card_name = 0;
        static Int32 v_board_id = -1;
        static Int32 v_channel = 0;
        static Int32 v_total_axis = 0;
        static Int32 v_is_card_initialed = 0;
        static Int32 v_is_FieldBus_Start = 0;
        static Int32 v_StartAxisID = 0;
        static Int32 v_CurrentAxisIDIndex = 0;
        //////////////////////////////////////////////////////////////////////////////////
        static Int32 Is_Creat = NO;
        static bool bDeviceInit = false;


        static int IO_Module_ID = 0;

        public static bool OpenDevice()
        {
            if (v_is_card_initialed == YES)
            {
                //MessageBox.Show("Initial ok !");
                return true;
            }

            if (Close_Device)
                return false;
            Int32 boardID_InBits = 0;
            Int32 mode = 0;
            Int32 ret = 0;
            Int32 i = 0, j = 0;
            Int32 card_name = 0;
            Int32 tamp = 0;
            Int32 StartAxisID = 0;
            Int32 TotalAxisNum = 0;



            // Card(Board) initial
            ret = APS168.APS_initial(ref boardID_InBits, mode);
            if (ret != 0)
                throw new Exception($"Adlink Ethercat Card Initial error {ret}");
            if (ret == 0)
            {
                for (i = 0; i < 16; i++)
                {
                    tamp = (boardID_InBits >> i) & 1;

                    if (tamp == 1)
                    {
                        ret = APS168.APS_get_card_name(i, ref card_name);

                        if (card_name == (Int32)APS_Define.DEVICE_NAME_PCIE_8338
                            || card_name == (Int32)APS_Define.DEVICE_NAME_PCIE_8334 || card_name == (Int32)APS_Define.DEVICE_NAME_PCIE_8332)
                        {
                            ret = APS168.APS_get_first_axisId(i, ref StartAxisID, ref TotalAxisNum);

                            //----------------------------------------------------
                            v_card_name = card_name;
                            v_board_id = i;
                            v_total_axis = TotalAxisNum;
                            v_is_card_initialed = YES;
                            v_StartAxisID = StartAxisID;

                            //for (j = 0; j < TotalAxisNum; j++)
                            //{
                            //    comboBox_Axis.Items.Add(j + StartAxisID);
                            //}
                            //comboBox_Axis.SelectedIndex = 0;


                            if (v_total_axis == 4) v_channel = 2;
                            else if (v_total_axis == 8) v_channel = 4;
                            //----------------------------------------------------
                            bDeviceInit = true;

                            ret = APS168.APS_scan_field_bus(v_board_id, 0);
                            if (ret != 0)
                                throw new Exception($"Adlink Ethercat error : Scan field bus error ({ret})");


                            ret = APS168.APS_start_field_bus(v_board_id, 0, v_StartAxisID);
                            if (ret != 0)

                                throw new Exception($"Adlink Ethercat error : Start field bus error({ret})");


                            //MessageBox.Show("Initial ok !");
                            //
                            ////Show main form title text
                            //if (card_name == (Int32)APS_Define.DEVICE_NAME_PCIE_8338) this.Text = "PCI-8338 Basic Sample";
                            //if (card_name == (Int32)APS_Define.DEVICE_NAME_PCIE_8334) this.Text = "PCIe-8334 Basic Sample";


                            int Info_Count = 0;
                            int Info_Array = 0;
                            APS168.APS_get_field_bus_last_scan_info(0, 0, ref Info_Array, 1, ref Info_Count);

                            for (int k = 0; k < Info_Array; k++)
                            {
                                EC_MODULE_INFO[] info = new EC_MODULE_INFO[1];
                                int a = APS168.APS_get_field_bus_module_info(0, 0, k, info);

                                if (info[0].DI_ModuleNum > 0)
                                    IO_Module_ID = k;

                            }



                            break;
                        }
                    }
                }

                if (v_board_id == -1)
                {
                    v_is_card_initialed = NO;
                    //MessageBox.Show("Board Id search error !");
                }
            }
            else
            {
                v_is_card_initialed = NO;

            }


            return bDeviceInit;
        }


        /// <summary>
        /// 如果驅動器給命令卻不走，可以檢查一下status word(0x6041) bit3 Fault 是否為1
        /// 若是則Ccontrol word(0x6040) bit 7 由 0 -> 1 (Fault reset) 由 0 變 1 可以清除錯誤
        /// </summary>
        /// <param name="subMOD_No"></param>
        static public  void DIMAFaultReset(int subMOD_No)
        {
                     

             
            Protect(() =>
            {
                int ret;


                byte []Data = new byte[2];       
                uint OutDatalen = 0;
                uint Timeout = 5000;
                uint Flags = 0;

                ret = APS168.APS_get_field_bus_sdo(0, 0, subMOD_No,  0x6041, 0, Data, 16, ref OutDatalen, Timeout, Flags);
                if (ret != 0)
                {
                    throw new Exception("DIMAFaultReset error - read 0x6041");
                }
                int a = Data[0] >> 3 & 1;


                if (a > 0)
                {
                    ret = APS168.APS_get_field_bus_sdo(0, 0, subMOD_No, 0x6040, 0, Data, 16, ref OutDatalen, Timeout, Flags);
                    if (ret!= 0)
                    {
                        throw new Exception("DIMAFaultReset error  - read 0x6040");
                    }

                    byte b = 0x80;
                    Data[0] = (byte)(Data[0]  |  b);

                    ret = APS168.APS_set_field_bus_sdo(0, 0, subMOD_No, 0x6040, 0, Data, 16, Timeout, Flags);

                    if (ret != 0)
                    {
                        throw new Exception("DIMAFaultReset error  - write 0x6040");
                    }






                }


            });

        }
 

        public static object GetOutputStatus(int subMOD_No, int ODIndex)
        {
            return Protect(() =>
            {
                UInt32 RawData = 1;

                int ret = APS168.APS_get_field_bus_d_port_output(0, 0, IO_Module_ID, subMOD_No, ref RawData);

                RawData = RawData >> ODIndex & 1;

                if (ret != 0)
                    throw new Exception("Adlink Ethercat error : GetOutputStatus");
                return RawData;
            });
        }

        public static void SetOutputStatus(int subMOD_No, int ODIndex, uint RawData)
        {
            Protect(() =>
            {

                UInt32 Data = 1;

                int ret = APS168.APS_get_field_bus_d_port_output(0, 0, IO_Module_ID, subMOD_No, ref Data);

                if (RawData == 0)
                {
                    int v = ~(1 << ODIndex);
                    Data = (uint)(Data & v);
                }
                else
                {
                    int v = (1 << ODIndex);
                    Data = (uint)(Data | v);
                }

                ret = APS168.APS_set_field_bus_d_port_output(0, 0, IO_Module_ID, subMOD_No, Data);

                //int ret = APS168.APS_set_field_bus_od_data(v_board_id, 0, 0, subMOD_No, ODIndex, RawData);

                //int ret = APS168.APS_set_field_bus_d_port_output(0, 0, 2, 0, ref RawData);


                if (ret != 0)
                    throw new Exception("Adlink Ethercat error : SetOutputStatus");
            });
        }


        public static object GetInputStatus(int subMOD_No, int ODIndex)
        {
            return Protect(() =>
            {
                uint RawData = 0;
                //int ret = APS168.APS_get_field_bus_od_data(v_board_id, 0, 0, subMOD_No, ODIndex, ref RawData);


                int ret = APS168.APS_get_field_bus_d_port_input(0, 0, IO_Module_ID, subMOD_No, ref RawData);

                RawData = RawData >> ODIndex & 1;

                if (ret != 0)
                    throw new Exception("Adlink Ethercat error : GetInputStatus");
                return RawData;
            });
        }


        //public Int32[] MIO_Bit = { (Int32)APS_Define.MIO_ALM, (Int32)APS_Define.MIO_PEL, (Int32)APS_Define.MIO_MEL, (Int32)APS_Define.MIO_ORG, (Int32)APS_Define.MIO_EMG, (Int32)APS_Define.MIO_INP, (Int32)APS_Define.MIO_SVON, (Int32)APS_Define.MIO_SCL, (Int32)APS_Define.MIO_SPEL, (Int32)APS_Define.MIO_SMEL, (Int32)APS_Define.MIO_OP };
        //public Int32[] MSTS_Bit = { (Int32)APS_Define.MTS_CSTP, (Int32)APS_Define.MTS_VM, (Int32)APS_Define.MTS_ACC, (Int32)APS_Define.MTS_DEC, (Int32)APS_Define.MTS_DIR, (Int32)APS_Define.MTS_MDN, (Int32)APS_Define.MTS_HMV, (Int32)APS_Define.MTS_WAIT, (Int32)APS_Define.MTS_PTB, (Int32)APS_Define.MTS_JOG, (Int32)APS_Define.MTS_ASTP, (Int32)APS_Define.MTS_BLD, (Int32)APS_Define.MTS_PRED, (Int32)APS_Define.MTS_POSTD, (Int32)APS_Define.MTS_GER, (Int32)APS_Define.MTS_PSR, (Int32)APS_Define.MTS_GRY };




        public static bool GetMotion_IO_Status(int Axis_ID, eMotion_IO_Status ID)
        {
            return Protect(() =>
            {
                int sts = APS168.APS_motion_io_status(Axis_ID);

                bool stats = (((sts >> (int)ID) & 1) == 1) ? true : false;

                return stats;
            });
        }

        public static bool GetMotionStatus(int Axis_ID, eMotion_Status ID)
        {
            return Protect(() =>
            {
                int sts = APS168.APS_motion_status(Axis_ID);

                bool stats = (((sts >> (int)ID) & 1) == 1) ? true : false;

                return stats;
            });
        }

        //private static bool m_Lock;
        //private static SpinLock sl = new SpinLock();
        private static object Lock = new object();

        public static void LockProtect()
        {
            while (!Monitor.TryEnter(Lock))
                Thread.Sleep(100);



            OpenDevice();

        }

        public static void UnLock()
        {
            Monitor.Exit(Lock);
        }


        public static T Protect<T>(Func<T> Function, bool Protect = true)
        {
            try
            {
                /*while (Monitor.IsEntered(Lock))
                    Thread.Sleep(100);*/
                if (Protect)
                    Monitor.Enter(Lock);

                OpenDevice();
                if (bDeviceInit)
                    return Function();
                else
                    return default(T);
            }
            finally
            {
                if (Protect)
                    Monitor.Exit(Lock);
            }
        }
        public static void Protect(Action Action)
        {
            try
            {


                Monitor.Enter(Lock);

                OpenDevice();
                if (bDeviceInit)
                    Action();
            }
            finally
            {
                Monitor.Exit(Lock);
            }
        }

        //private void Scan_Field_Bus(object sender, EventArgs e)
        //{
        //    Int32 ret = 0;
        //    Int32 Board_ID = v_board_id;
        //    Int32 BUS_No = 0;

        //    ret = APS168.APS_scan_field_bus(Board_ID, BUS_No);
        //    if (ret != 0)
        //    {
        //        MessageBox.Show("Scan field bus error " + ret.ToString());
        //    }
        //    else
        //    {
        //        MessageBox.Show("Scan field bus successfully");
        //    }
        //}

        //private void Start_Field_Bus(object sender, EventArgs e)
        //{
        //    Int32 ret = 0;
        //    Int32 Board_ID = v_board_id;
        //    Int32 BUS_No = 0;
        //    if (v_is_FieldBus_Start == YES)
        //    {
        //        MessageBox.Show("Start field bus successfully !");
        //        return;
        //    }
        //    ret = APS168.APS_start_field_bus(Board_ID, BUS_No, v_StartAxisID);
        //    if (ret != 0)
        //    {
        //        MessageBox.Show("Start field bus error " + ret.ToString());
        //    }
        //    else
        //    {
        //        v_is_FieldBus_Start = YES;
        //        MessageBox.Show("Start field bus successfully !");
        //    }
        //}



        static bool Close_Device = false;
        public static bool CloseDevice()
        {
            return Protect(() =>
           {

               bDeviceInit = false;
               Close_Device = true;
               if (v_is_card_initialed == YES)
               {
                   v_is_card_initialed = NO;
                   APS168.APS_stop_field_bus(0, 0);
                   APS168.APS_close();




               }
               return true;
           });

        }

        public void Dispose()
        {

        }
    }
}
