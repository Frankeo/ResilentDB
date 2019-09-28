using System;

namespace KeyGenerator
{
    public class Generator
    {
        static readonly Lazy<int> NodeId = new Lazy<int>(GenerateNodeId);
        static ulong LastTimestamp = 0;                                      
        static ulong Sequence = 0;

        public static ulong GetKey() 
        {
            ulong currentTimestamp = GetTimestamp();

            if(currentTimestamp < LastTimestamp) 
                throw new Exception("Invalid System Clock!");

            if (currentTimestamp == LastTimestamp) 
            {
                Sequence = (Sequence + 1) & KeyConstants.MAX_SEQUENCE;
                if(Sequence == default(ulong)) currentTimestamp = WaitNextMilisecond(currentTimestamp);
            } else {
                Sequence = 0;
            }

            LastTimestamp = currentTimestamp;
            return ComputeKey(currentTimestamp);
        }

        static ulong ComputeKey(ulong currentTimestamp)
        {            
            ulong id = currentTimestamp << (KeyConstants.TOTAL_BITS - KeyConstants.EPOCH_BITS);
            id |= (Sequence << (KeyConstants.TOTAL_BITS - KeyConstants.EPOCH_BITS - KeyConstants.SEQUENCE_BITS));
            id |= (ulong)NodeId.Value;
            return id;
        }

        static int GenerateNodeId() =>
            Guid.NewGuid().GetHashCode() & KeyConstants.MAX_NODE_ID;

        static ulong GetTimestamp() =>
            (ulong)DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        static ulong WaitNextMilisecond(ulong currentTimestamp) {
            while (currentTimestamp == LastTimestamp) {
                currentTimestamp = GetTimestamp();
            }
            return currentTimestamp;
        }
    }
}
