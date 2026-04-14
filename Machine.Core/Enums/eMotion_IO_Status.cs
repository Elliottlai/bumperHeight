using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Machine.Core
{
    public enum eMotion_IO_Status
    {


        // Motion IO status bit number define.
        /// <summary>
        /// Servo alarm.
        /// </summary>
        ALM = 0,   // Servo alarm.
        /// <summary>
        ///  Positive end limit.
        /// </summary>
        PEL = 1,   // Positive end limit.
        /// <summary>
        /// Negative end limit.
        /// </summary>
        MEL = 2,   // Negative end limit.
        /// <summary>
        /// ORG Home
        /// </summary>
        ORG = 3,   // ORG Home
        /// <summary>
        /// Emergency stop
        /// </summary>
        EMG = 4,   // Emergency stop
        /// <summary>
        /// EZ.
        /// </summary>
        EZ = 5,   // EZ.
        /// <summary>
        /// In position.
        /// </summary>
        INP = 6,   // In position.
        /// <summary>
        /// Servo on signal.
        /// </summary>
        SVON = 7,   // Servo on signal.
        /// <summary>
        /// Ready.
        /// </summary>
        RDY = 8,   // Ready.
        /// <summary>
        /// Warning.
        /// </summary>
        WARN = 9,   // Warning.
        /// <summary>
        /// Zero speed.
        /// </summary>
        ZSP = 10,  // Zero speed.
        /// <summary>
        /// Soft positive end limit.
        /// </summary>
        SPEL = 11,  // Soft positive end limit.
        /// <summary>
        /// Soft negative end limit.
        /// </summary>
        SMEL = 12,  // Soft negative end limit.
        /// <summary>
        /// Torque is limited by torque limit value.
        /// </summary>
        TLC = 13,  // Torque is limited by torque limit value.
        /// <summary>
        /// Absolute position lost.
        /// </summary>        
        ABSL = 14,  // Absolute position lost.
        /// <summary>
        /// External start signal.
        /// </summary>
        STA = 15,  // External start signal.
        /// <summary>
        /// Positive slow down signal
        /// </summary>
        PSD = 16,  // Positive slow down signal
        /// <summary>
        /// Negative slow down signal
        /// </summary>
        MSD = 17,  // Negative slow down signal
        /// <summary>
        /// Circular limit.
        /// </summary>
        SCL = 10,  // Circular limit.
        /// <summary>
        /// Not all slaves are in operation mode.
        /// </summary>
        OP = 24  // Not all slaves are in operation mode.
              
    };



}
