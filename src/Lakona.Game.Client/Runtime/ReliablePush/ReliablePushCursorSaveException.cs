using System;

namespace Lakona.Game.Client.ReliablePush
{
    internal sealed class ReliablePushCursorSaveException : Exception
    {
        public ReliablePushCursorSaveException(Exception innerException)
            : base("Could not save the reliable push receive cursor: " + innerException.Message, innerException)
        {
        }
    }
}
