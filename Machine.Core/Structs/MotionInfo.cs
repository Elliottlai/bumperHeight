using Machine.Core.Interfaces;

namespace Machine.Core
{
    public struct MotionInfo
    {
        public IAxis Axis { get; }

        public double Start { get; }

        public double End { get; }

        public MotionInfo(IAxis Axis, double Start, double End)
        {
            this.Axis = Axis;
            this.Start = Start;
            this.End = End;
        }

        public MotionInfo(IAxis Axis, double End) : this(Axis, Axis.GetRealPosition(), End)
        {

        }


    }
}
