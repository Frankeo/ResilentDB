using System;
using System.Threading;
using System.Runtime.CompilerServices;

namespace KeyGenerator
{
    public class Generator
    {
        static readonly Lazy<int> NodeId = new Lazy<int>(GenerateNodeId);
        static long LastTimestamp = 0;                                      
        static volatile int Sequence = 0;

        [MethodImpl(MethodImplOptions.Synchronized)]
        public static ulong GetKey() 
        {
            long currentTimestamp = GetTimestamp();

            if(currentTimestamp < Interlocked.Read(ref LastTimestamp)) 
                throw new Exception("Invalid System Clock!");

            if (currentTimestamp == Interlocked.Read(ref LastTimestamp)) 
            {
                Sequence = (Sequence + 1) & KeyConstants.MAX_SEQUENCE;
                if(Sequence == default(int)) currentTimestamp = WaitNextMilisecond(currentTimestamp);
            } else {
                Sequence = 0;
            }

            Interlocked.Exchange(ref LastTimestamp, currentTimestamp);
            return ComputeKey(currentTimestamp);
        }

        static ulong ComputeKey(long currentTimestamp)
        {            
            ulong id = ((ulong)currentTimestamp) << (KeyConstants.TOTAL_BITS - KeyConstants.EPOCH_BITS);
            id |= ((ulong)Sequence) << (KeyConstants.TOTAL_BITS - KeyConstants.EPOCH_BITS - KeyConstants.SEQUENCE_BITS);
            id |= (ulong)NodeId.Value;
            return id;
        }

        static int GenerateNodeId() =>
            Guid.NewGuid().GetHashCode() & KeyConstants.MAX_NODE_ID;

        static long GetTimestamp() =>
            DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        static long WaitNextMilisecond(long currentTimestamp) {
            while (currentTimestamp == LastTimestamp) {
                currentTimestamp = GetTimestamp();
            }
            return currentTimestamp;
        }
    }
}
