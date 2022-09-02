using System.Security.Cryptography;

namespace MPMP.Modules;

public sealed class DynamicAcknowledgement : Module
{
    /// <summary>
    /// How much many times longer before a packet is re-sent based on the round-trip time.
    /// </summary>
    public float TimeoutMultiplier = 2;

    /// <summary>
    /// The minimum amount of time (in milliseconds) before a packet is re-sent.
    /// </summary>
    public int MinimumTimeout = 1;

    /// <summary>
    /// The maximum amount of time (in milliseconds) before a packet is re-sent.
    /// </summary>
    public int MaxTimeout = 1000;

    public int? Timeout { get; private set; } = null;

    /// <summary>
    /// A list of pending packets that have not received an acknowledgement.
    /// </summary>
    public readonly List<Pending> Unacknowledged = new();

    private readonly HashAlgorithm HashAlgorithm = SHA1.Create();

    private enum Method { Data, Resend, Acknowledge }

    public DynamicAcknowledgement()
    {
        Task.Run(async () =>
        {
            while (true)
            {
                Pending[] unacknowledged;
                lock (Unacknowledged)
                    unacknowledged = Unacknowledged.ToArray();

                foreach (Pending pending in unacknowledged)
                {
                    double elapsed = (DateTime.Now - pending.LastSentTime).TotalMilliseconds;
                    if (elapsed >= MaxTimeout || (Timeout != null && Timeout * TimeoutMultiplier >= MinimumTimeout && elapsed >= Timeout * TimeoutMultiplier))
                    {
                        pending.Iteration++;
                        pending.ResendTimes.Add(DateTime.Now);

                        byte[] data = new byte[pending.Data.Length + 2];
                        data[0] = (byte)Method.Resend;
                        data[1] = (byte)pending.Iteration;
                        Buffer.BlockCopy(pending.Data, 0, data, 2, pending.Data.Length);

                        ContinueBuild(data);
                        pending.LastSentTime = DateTime.Now;
                    }
                }

                await Task.Delay(10);
            }
        });
    }

    public override void Build(byte[] data)
    {
        lock (Unacknowledged)
            Unacknowledged.Add(new(HashAlgorithm.ComputeHash(data), data));

        ContinueBuild(AppendMethod(Method.Data, data));
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
                    byte[] hash = HashAlgorithm.ComputeHash(remaining);
                    byte[] acknowledgement = new byte[hash.Length + 2];
                    acknowledgement[0] = (byte)Method.Acknowledge;
                    acknowledgement[1] = 0;
                    Buffer.BlockCopy(hash, 0, acknowledgement, 2, hash.Length);
                    ContinueBuild(acknowledgement);
                }
                break;

            case Method.Resend:
                {
                    byte[] remaining = new byte[data.Length - 2];
                    Buffer.BlockCopy(data, 2, remaining, 0, remaining.Length);

                    ContinueRead(remaining);

                    // Send acknowledgement
                    byte[] hash = HashAlgorithm.ComputeHash(remaining);
                    byte[] acknowledgement = new byte[hash.Length + 2];
                    acknowledgement[0] = (byte)Method.Acknowledge;
                    acknowledgement[1] = data[1];
                    Buffer.BlockCopy(hash, 0, acknowledgement, 2, hash.Length);
                    ContinueBuild(acknowledgement);
                }
                break;

            case Method.Acknowledge:
                {
                    byte[] hash = new byte[data.Length - 2];
                    Buffer.BlockCopy(data, 2, hash, 0, hash.Length);

                    Pending[] unacknowledged;
                    lock (Unacknowledged)
                        unacknowledged = Unacknowledged.ToArray();

                    foreach (Pending pending in unacknowledged)
                        if (pending.Key.SequenceEqual(hash))
                        {
                            Timeout = (int)(DateTime.Now - pending.ResendTimes[(int)(uint)data[1]]).TotalMilliseconds;

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
        internal ushort Iteration = 0;
        internal readonly List<DateTime> ResendTimes = new() { DateTime.Now };

        internal Pending(byte[] key, byte[] data)
        {
            Key = key;
            Data = data;
        }
    }
}