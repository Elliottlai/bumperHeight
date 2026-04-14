namespace Machine.Core.Interfaces
{
    public interface IDigitalOutput : IDigitalInput
    {
        void SetStatus(object Data);

    }
}
