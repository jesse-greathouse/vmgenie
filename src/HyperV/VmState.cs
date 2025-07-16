namespace VmGenie.HyperV;

public enum VmState : ushort
{
    Unknown = 0,
    Other = 1,
    Running = 2,
    Off = 3,
    ShuttingDown = 4,
    NotApplicable = 5,
    Paused = 6,
    Suspended = 7,
    Starting = 10,
    Snapshotting = 11,
    Saving = 32773,
    Stopping = 32774
}
