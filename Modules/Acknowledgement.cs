using System.Security.Cryptography;

namespace MPMP.Modules;

public sealed class Acknowledgement : Module
{
    /// <summary>
    /// The amount of time (in milliseconds) before a packet is re-sent.
    /// </summary>
    public int Timeout = 500;

    /// <summary>
    /// A list of pending packets that have not received an acknowledgement.
    /// </summary>
    public readonly List<Pending> Unacknowledged = new();

    private readonly HashAlgorithm HashAlgorithm = SHA1.Create();

    private enum Method { Data, Acknowledge }

    public Acknowledgement()
    {
        Task.Run(() =>
        {
            while (true)
            {
                Pending[] unacknowledged;
                lock (Unacknowledged)
                    unacknowledged = Unacknowledged.ToArray();

                foreach (Pending pending in unacknowledged)
                    if ((DateTime.Now - pending.LastSentTime).TotalMilliseconds >= Timeout)
                    {
                        ContinueBuild(pending.Data);
                        pending.LastSentTime = DateTime.Now;
                    }
            }
        });
    }

    public override void Build(byte[] data)
    {
        byte[] final = AppendMethod(Method.Data, data);

        lock (Unacknowledged)
            Unacknowledged.Add(new(HashAlgorithm.ComputeHash(data), final));

        ContinueBuild(final);
    }

    public override void Read(byte[] data)
    {
        switch ((Method)data[0])
        {
            case Method.Data:
                {
                    byte[] remaining = new byte[data.Length - 1];
                    Buffer.BlockCopy(data, 1, remaining, 0, remaining.Length);

                    ContinueRead(remaining);

                    // Send acknowledgement
                    ContinueBuild(AppendMethod(Method.Acknowledge, HashAlgorithm.ComputeHash(remaining)));
                }
                break;

            case Method.Acknowledge:
                {
                    byte[] hash = new byte[data.Length - 1];
                    Buffer.BlockCopy(data, 1, hash, 0, hash.Length);

                    Pending[] unacknowledged;
                    lock (Unacknowledged)
                        unacknowledged = Unacknowledged.ToArray();

                    foreach (Pending pending in unacknowledged)
                        if (pending.Key.SequenceEqual(hash))
                        {
                            lock (Unacknowledged)
                                Unacknowledged.Remove(pending);

                            break;
                        }
                }
                break;
        }
    }

    private static byte[] AppendMethod(Method method, byte[] data)
    {
        byte[] final = new byte[data.Length + 1];
        final[0] = (byte)method;
        Buffer.BlockCopy(data, 0, final, 1, data.Length);
        return final;
    }

    public class Pending
    {
        public readonly byte[] Data;

        internal readonly byte[] Key;
        internal DateTime LastSentTime = DateTime.Now;

        internal Pending(byte[] key, byte[] data)
        {
            Key = key;
            Data = data;
        }
    }
}