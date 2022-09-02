using System.Collections.Concurrent;

namespace MPMP.Modules;

public sealed class DynamicStream : Module
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
    public int MaxTimeout = 500;

    public int? Timeout { get; private set; } = null;

    /// <summary>
    /// Maximum number of pending packets that can be stored in the receiving buffer.
    /// </summary>
    public int ReceiveBufferSize = 50;

    /// <summary>
    /// Unacknowledged packets.
    /// </summary>
    public readonly ConcurrentDictionary<uint, UnacknowledgedPacket> Unacknowledged = new();

    /// <summary>
    /// A buffer of pending disordered packets.
    /// </summary>
    public readonly ConcurrentDictionary<uint, byte[]> ReceiveBuffer = new();

    private uint SendIndex = 0;
    private uint AcknowledgmentIndex = 0;
    private uint ReceiveIndex = 0;
    private DateTime? LastSentTime = null;

    private enum Method { Message, Acknowledement, Resend }

    public DynamicStream()
    {
        Task.Run(async () =>
        {
            while (true)
            {
                if (LastSentTime is not null)
                {
                    int elapsed = (int)(DateTime.Now - LastSentTime.Value).TotalMilliseconds;

                    if ((elapsed >= MaxTimeout
                        || (Timeout is not null
                            && Timeout * TimeoutMultiplier >= MinimumTimeout
                            && elapsed >= Timeout * TimeoutMultiplier))
                        && Unacknowledged.TryGetValue(SendIndex - 1, out UnacknowledgedPacket? packet))
                        ContinueBuild(packet.Data);
                }

                await Task.Delay(1);
            }
        });
    }

    public override void Build(byte[] data)
    {
        byte[] indexBytes = BitConverter.GetBytes(SendIndex);
        byte[] appendedData = new byte[indexBytes.Length + data.Length];
        Buffer.BlockCopy(indexBytes, 0, appendedData, 0, indexBytes.Length);
        Buffer.BlockCopy(data, 0, appendedData, indexBytes.Length, data.Length);
        appendedData = AppendMethod(appendedData, Method.Message);

        Unacknowledged.TryAdd(SendIndex, new(appendedData));
        ContinueBuild(appendedData);

        LastSentTime = DateTime.Now;
        SendIndex++;
    }

    public override void Read(byte[] data)
    {
        if (data.Length < sizeof(uint) + 1)
            return;

        byte[] methodData = new byte[data.Length - 1];
        Buffer.BlockCopy(data, 1, methodData, 0, methodData.Length);

        switch ((Method)data[0])
        {
            case Method.Message:
                {
                    byte[] messageIndexBytes = new byte[sizeof(uint)];
                    Buffer.BlockCopy(methodData, 0, messageIndexBytes, 0, messageIndexBytes.Length);
                    uint messageIndex = BitConverter.ToUInt32(messageIndexBytes, 0);

                    if (messageIndex < ReceiveIndex)
                    {
                        ContinueBuild(AppendMethod(BitConverter.GetBytes(ReceiveIndex - 1), Method.Acknowledement));
                        break;
                    }

                    if (messageIndex - ReceiveIndex > ReceiveBufferSize)
                        break;

                    byte[] messageContent = new byte[methodData.Length - sizeof(uint)];
                    Buffer.BlockCopy(methodData, sizeof(uint), messageContent, 0, messageContent.Length);

                    if (messageIndex == ReceiveIndex)
                    {
                        ContinueRead(messageContent);

                        // Check for pending packets
                        while (ReceiveBuffer.TryRemove(++messageIndex, out byte[]? message))
                            ContinueRead(message);

                        ContinueBuild(AppendMethod(BitConverter.GetBytes(messageIndex - 1), Method.Acknowledement));
                        ReceiveIndex = messageIndex;
                    }
                    else
                    {
                        ReceiveBuffer.TryAdd(messageIndex, messageContent);

                        List<uint> resends = new();

                        for (uint i = ReceiveIndex; i <= messageIndex; i++)
                            if (!ReceiveBuffer.ContainsKey(i))
                                resends.Add(i);

                        byte[] resendData = new byte[resends.Count * sizeof(uint) + 1];
                        resendData[0] = (byte)Method.Resend;

                        for (int i = 0; i < resends.Count; i++)
                        {
                            byte[] resendIndex = BitConverter.GetBytes(resends[i]);
                            Buffer.BlockCopy(resendIndex, 0, resendData, i * sizeof(uint) + 1, resendIndex.Length);
                        }

                        ContinueBuild(resendData);
                    }
                }
                break;

            case Method.Acknowledement:
                {
                    uint messageIndex = BitConverter.ToUInt32(methodData, 0);

                    if (messageIndex >= AcknowledgmentIndex)
                    {
                        double? newTimeout = null;

                        for (uint i = AcknowledgmentIndex; i <= messageIndex; i++)
                            if (Unacknowledged.TryRemove(i, out UnacknowledgedPacket? packet))
                            {
                                double messageTimeout = (DateTime.Now - packet.SentTime).TotalMilliseconds;

                                if (newTimeout is null || messageTimeout < newTimeout)
                                    newTimeout = messageTimeout;
                            }

                        AcknowledgmentIndex = messageIndex + 1;

                        if (newTimeout is not null)
                            Timeout = (int)newTimeout;
                    }
                }
                break;

            case Method.Resend:
                {
                    for (int i = 0; i < methodData.Length / sizeof(uint); i++)
                    {
                        uint messageIndex = BitConverter.ToUInt32(methodData, i * sizeof(uint));

                        if (messageIndex >= AcknowledgmentIndex && Unacknowledged.TryGetValue(messageIndex, out UnacknowledgedPacket? packet))
                        {
                            ContinueBuild(packet.Data);
                            packet.SentTime = DateTime.Now;
                        }
                    }
                }
                break;
        }
    }

    private static byte[] AppendMethod(byte[] data, Method method)
    {
        byte[] appended = new byte[data.Length + 1];
        appended[0] = (byte)method;
        Buffer.BlockCopy(data, 0, appended, 1, data.Length);

        return appended;
    }

    public class UnacknowledgedPacket
    {
        public readonly byte[] Data;
        public DateTime SentTime = DateTime.Now;

        internal UnacknowledgedPacket(byte[] data)
        {
            Data = data;
        }
    }
}