namespace MPMP;

public class Manager
{
    public delegate void ReadCompleteEvent(byte[] data);

    /// <summary>
    /// Called after all modules complete a read.
    /// </summary>
    public event ReadCompleteEvent? OnReadComplete;

    public delegate void BuildCompleteEvent(byte[] data);

    /// <summary>
    /// Called after all modules complete a build.
    /// </summary>
    public event BuildCompleteEvent? OnBuildComplete;

    private Module[] _Modules;
    public Module[] Modules
    {
        get => _Modules;
        set
        {
            for (int i = 0; i < value.Length; i++)
                value[i].Index = i;

            _Modules = value;
        }
    }

    public Manager(Module[] modules)
    {
        Modules = modules;
    }

    public void Read(byte[] data) =>
        Read(data, 0);

    internal void Read(byte[] data, int index)
    {
        if (index == Modules.Length)
            OnReadComplete?.Invoke(data);
        else
            Modules[index].Read(data);
    }

    public void Build(byte[] data) =>
        Build(data, 0);

    internal void Build(byte[] data, int index)
    {
        if (index == Modules.Length)
            OnBuildComplete?.Invoke(data);
        else
            Modules[index].Build(data);
    }
}