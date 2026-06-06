namespace Lakona.Rpc.Core
{
    /// <summary>
    ///     Typed descriptor for a client-to-server RPC method.
    /// </summary>
    /// <typeparam name="TArg">Request DTO type.</typeparam>
    /// <typeparam name="TResult">Response DTO type.</typeparam>
    public readonly struct RpcMethod<TArg, TResult>
    {
        /// <summary>
        ///     Creates a method descriptor from stable protocol ids.
        /// </summary>
        /// <param name="serviceId">Stable service id declared by <see cref="RpcServiceAttribute"/>.</param>
        /// <param name="methodId">Stable method id declared by <see cref="RpcMethodAttribute"/>.</param>
        public RpcMethod(int serviceId, int methodId)
        {
            ServiceId = serviceId;
            MethodId = methodId;
        }

        /// <summary>
        ///     Stable service id used on the wire.
        /// </summary>
        public int ServiceId { get; }

        /// <summary>
        ///     Stable method id used on the wire.
        /// </summary>
        public int MethodId { get; }
    }

    /// <summary>
    ///     Typed descriptor for a server-to-client notification method.
    /// </summary>
    /// <typeparam name="TArg">Notification DTO type.</typeparam>
    public readonly struct RpcNotificationMethod<TArg>
    {
        /// <summary>
        ///     Creates a notification descriptor from stable protocol ids.
        /// </summary>
        /// <param name="serviceId">Stable service id declared by <see cref="RpcServiceAttribute"/>.</param>
        /// <param name="methodId">Stable notification method id declared by <see cref="RpcNotificationAttribute"/>.</param>
        public RpcNotificationMethod(int serviceId, int methodId)
        {
            ServiceId = serviceId;
            MethodId = methodId;
        }

        /// <summary>
        ///     Stable service id used on the wire.
        /// </summary>
        public int ServiceId { get; }

        /// <summary>
        ///     Stable notification method id used on the wire.
        /// </summary>
        public int MethodId { get; }
    }
}
