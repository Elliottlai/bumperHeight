using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Machine.Core.Enums
{
    public enum eMotion_Status
    {
        /// <summary>
        /// Command stopped (But it could be in motion)
        /// </summary>
        CSTP = 0,
        /// <summary>
        /// In maximum velocity
        /// </summary>
        VM = 1 ,
        /// <summary>
        /// In acceleration
        /// </summary>
        ACC=2,

        /// <summary>
        /// In deceleration
        /// </summary>
        DEC=3,
        /// <summary>
        ///  Move direction. 1:Positive direction, 0:Negative direction
        /// </summary>
        DIR=4,
        /// <summary>
        ///  Motion done. 0: In motion, 1: Motion done ( It could be abnormal stop)
        /// </summary>
        MDN=5,
        /// <summary>
        ///  In homing
        /// </summary>
        HMV = 6,
        /// <summary>
        ///  Axis is in waiting state. ( Wait move trigger )
        /// </summary>
        WAIT = 10,
        /// <summary>
        /// Axis is in point buffer moving. ( When this bit on, MDN and ASTP will becleared )
        /// </summary>
        PTB = 11,
        /// <summary>
        ///  In jogging
        /// </summary>
        JOG = 15,
        /// <summary>
        ///  0: Stop normally, 1: abnormal stop, When axis in motion, this bit will be clear.
        /// </summary>
        ASTP = 16,
        /// <summary>
        /// , Axis (Axes) in blending moving (Only for interpolation move usage)
        /// </summary>
        BLD = 17,
        /// <summary>
        /// Pre-distance event, 1: event arrived. The event will be clear when axis start moving
        /// </summary>
        PRED = 18,
        /// <summary>
        /// Post-distance event. 1: event arrived. The event will be clear when axis start moving
        /// </summary>
        POSTD = 19,
        /// <summary>
        ///  1: In geared ( This axis as slave axis and it follow a master specified in axis parameter. )
        /// </summary>
        GER = 28,
        /// <summary>
        ///  Pulser function status. 0: Disable, 1: Enable
        /// </summary>
        PSR = 29,
        /// <summary>
        /// 1: When gantry mode is enabled, this axis is master and his motion status bit 30 (GRY) will be turned on.
        /// 0: When gantry mode is disable, turning this axis's motion status bit 30 (GRY) off will depends on his other slaves are in gantry mode or not.
        /// </summary>
        GRY = 30,


    }
}
