using Machine.Core.Interfaces;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Machine.Core
{
    public class cPlatform_General : IPlatformArgs
    {

        private IAxis AxisX;
        private IAxis AxisY;
        private IAxis AxisZ;
        private double standardBoardRelativeLocationZ;

        public string AxisX_UID
        {
            get => AxisX?.UID ?? string.Empty;
            set => AxisX = cMachineManager.Axises.TryGetValue(value, out IAxis Axis) ? Axis : null;
        }

        public string AxisY_UID
        {
            get => AxisY?.UID ?? string.Empty;
            set => AxisY = cMachineManager.Axises.TryGetValue(value, out IAxis Axis) ? Axis : null;

        }

        public string AxisZ_UID
        {
            get => AxisZ?.UID ?? string.Empty;
            set => AxisZ = cMachineManager.Axises.TryGetValue(value, out IAxis Axis) ? Axis : null;
        }



        public string UID { set; get; }
        public string Name { set; get; }

        public double ScanLocationX { set; get; }
        public double ScanLocationY { set; get; }
        public bool ScanLocationY_IsStart { set; get; }

        public double ScanLocationZ { set; get; }


        //[JsonIgnore]
      //  public double ScanLocationY_2 => ScanLocationY - MesaHeight;

        //public double StandardBoardMoveHeight { set; get; } = 100000;


        public double StartingPosition { set; get; }

        public double StandardBoardRelativeLocationY { set; get; }

        [JsonIgnore]
        public double StandardBoardRelativeLocationZ 
        { 
            get => standardBoardRelativeLocationZ; 
            set => standardBoardRelativeLocationZ = value; 
        }
        [JsonIgnore]
        public double StandardBoardLocationY => ScanLocationY + StandardBoardRelativeLocationY;
        [JsonIgnore]
        public double StandardBoardLocationZ => ScanLocationZ + StandardBoardRelativeLocationZ;
        // public double StandardBoardLocationYEnd => StandardBoardLocationY + StandardBoardMoveHeight;
     
      //  public double Velocity_Y { set; get; }

        public double Outgoing_Y { set; get; }


        public int MesaWidth { set; get; }
        public int MesaHeight { set; get; }

        [JsonIgnore]
        public double ScanBoardWidth { set; get; }
        [JsonIgnore]
        public double ScanBoardHeight { set; get; }

        [JsonIgnore]
        public double FirstColumn_StartY {
            get {
                double value = 0;
                if (ScanLocationY_IsStart)
                    value = ScanLocationY;
                else
                    value = ScanLocationY - ScanBoardHeight;
                return  value;
            }
        }

        [JsonIgnore]
        public double FirstColumn_EndY 
            => ScanLocationY_IsStart ? ScanLocationY - ScanBoardHeight : 
                                       ScanLocationY;

    }
}
