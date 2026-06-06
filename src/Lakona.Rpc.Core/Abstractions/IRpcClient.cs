using System;
using System.Threading;
using System.Threading.Tasks;

namespace Lakona.Rpc.Core
{
    /// <summary>
    ///     Runtime-facing RPC client abstraction used by generated client proxies.
    /// </summary>
    /// <remarks>
    ///     Application code normally uses the generated client facade instead of this interface directly.
    ///     Implementations are responsible for request correlation, response decoding, and server notification dispatch.
    /// </remarks>
    public interface IRpcClient
    {
        /// <summary>
        ///     Sends one RPC request and waits for the matching response.
        /// </summary>
        /// <typeparam name="TArg">Request DTO type.</typeparam>
        /// <typeparam name="TResult">Response DTO type.</typeparam>
        /// <param name="method">Generated method descriptor containing the service id and method id.</param>
        /// <param name="arg">Request DTO instance, or <see langword="null"/> for void-style requests.</param>
        /// <param name="ct">Cancellation token for the outbound send and response wait.</param>
        /// <returns>The deserialized response DTO.</returns>
        /// <exception cref="RpcException">
        ///     Thrown by the default runtime when the remote response status is not <see cref="RpcStatus.Ok"/>.
        /// </exception>
        ValueTask<TResult> CallAsync<TArg, TResult>(RpcMethod<TArg, TResult> method, TArg? arg,
            CancellationToken ct = default);

        /// <summary>
        ///     Registers the handler for a server-to-client notification method.
        /// </summary>
        /// <typeparam name="TArg">Notification DTO type.</typeparam>
        /// <param name="method">Generated notification descriptor containing the service id and notification method id.</param>
        /// <param name="handler">Handler invoked with the deserialized notification DTO.</param>
        /// <exception cref="InvalidOperationException">
        ///     Thrown when a handler is already registered for the notification method.
        /// </exception>
        /// <remarks>
        ///     The default runtime invokes handlers from its internal notification-processing loop. It does not marshal
        ///     notifications to the Unity main thread.
        /// </remarks>
        void RegisterNotificationHandler<TArg>(RpcNotificationMethod<TArg> method, Func<TArg, ValueTask> handler);
    }
}
