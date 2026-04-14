using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Machine.Core
{

    public enum GMExpandParamter
    {
        CROP_WIDTH,
        CROP_HEIGHT,
        SHAFT_ENCODER_DROP,
        ExposureTime,
        sensorScanDirection,
        AcquisitionLineRate,
        TriggerMode,
        GainSelector,
    }

 

    public enum GMExpandFunction
    {
        /// <summary>
        /// public void PrepareStart(bool IsInternal = true, FlipMode flipMode = FlipMode.None, int binning = 1)
        /// </summary>
        PrepareStart,
        /// <summary>
        /// public void ClearCalibration()
        /// </summary>
        ClearCalibration,
        /// <summary>
        /// public void FPNSet()
        /// </summary>
        FPNSet,
        /// <summary>
        /// public void SelectPRNUSet(int index)
        /// </summary>
        SelectPRNUSet,
        /// <summary>
        /// public bool CurrentPRNUtoFile(string FileName)
        /// </summary>
        CurrentPRNUtoFile,
        /// <summary>
        /// public bool CurrentPRNUfromFile(string FileName)
        /// </summary>
        CurrentPRNUfromFile,
        /// <summary>
        /// public double GetGain(ChannelType channel)
        /// </summary>
        GetGain,
        /// <summary>
        /// public void SetDirection(ScanDirection scanDirection)
        /// </summary>
        SetDirection,
        /// <summary>
        /// public void SetGain(ChannelType channel, double Gain = 1)
        /// </summary>
        SetGain,
        /// <summary>
        /// public void SavePRNUSet(int index = -1)
        /// </summary>
        SavePRNUSet,
        /// <summary>
        /// public void PRNUSet(uint target = 150)
        /// </summary>
        PRNUSet,
        /// <summary>
        /// public Rect GetScanedRect(ScanDirection direction)
        /// </summary>
        GetScanedRect,
        /// <summary>
        /// public void UserSetLoadSve(UsersetFunc func, UsersetSelect UserSet)
        /// </summary>
        UserSetLoadSve,
        /// <summary>
        ///  public void BufSave(int index ,string FileName)
        /// </summary>
        SaveBuf

    }
}
