using System;

namespace Lakona.Rpc.Core
{
    /// <summary>
    ///     Exception thrown when a remote RPC response reports a non-success status.
    /// </summary>
    public sealed class RpcException : Exception
    {
        /// <summary>
        ///     Creates an exception from a remote RPC failure response.
        /// </summary>
        /// <param name="status">Remote response status.</param>
        /// <param name="errorMessage">Optional remote error message.</param>
        /// <param name="requestId">Request id associated with the failed response.</param>
        /// <param name="serviceId">Target service id.</param>
        /// <param name="methodId">Target method id.</param>
        public RpcException(RpcStatus status, string? errorMessage, uint requestId, int serviceId, int methodId)
            : base(FormatMessage(status, errorMessage, requestId, serviceId, methodId))
        {
            Status = status;
            ErrorMessage = errorMessage;
            RequestId = requestId;
            ServiceId = serviceId;
            MethodId = methodId;
        }

        /// <summary>
        ///     Remote response status.
        /// </summary>
        public RpcStatus Status { get; }

        /// <summary>
        ///     Optional remote error message.
        /// </summary>
        public string? ErrorMessage { get; }

        /// <summary>
        ///     Request id associated with the failed response.
        /// </summary>
        public uint RequestId { get; }

        /// <summary>
        ///     Target service id.
        /// </summary>
        public int ServiceId { get; }

        /// <summary>
        ///     Target method id.
        /// </summary>
        public int MethodId { get; }

        private static string FormatMessage(
            RpcStatus status,
            string? errorMessage,
            uint requestId,
            int serviceId,
            int methodId)
        {
            var message = $"RPC request {requestId} for {serviceId}:{methodId} failed with status {status}.";
            return string.IsNullOrEmpty(errorMessage)
                ? message
                : $"{message} {errorMessage}";
        }
    }
}
