namespace MPMP;

public abstract class Module
{
    internal int Index;

    private readonly Manager Manager;

    public Module(Manager manager) =>
        Manager = manager;

    /// <summary>
    /// Passes a convert request to the following module.
    /// </summary>
    /// <param name="data"></param>
    public void ContinueRead(byte[] data) =>
        Manager.Read(data, Index + 1);

    /// <summary>
    /// Passes a build request to the following module.
    /// </summary>
    /// <param name="data"></param>
    public void ContinueBuild(byte[] data) =>
        Manager.Build(data, Index + 1);

    public abstract void Read(byte[] data);
    public abstract void Build(byte[] data);
}