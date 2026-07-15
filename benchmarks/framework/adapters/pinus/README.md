# Pinus Framework Benchmark Adapter

Slice 2 maps `frontdoor.echo` to the ordinary Pinus connector handler route
`connector.echoHandler.echo`. The connector is Pinus 1.7.3's
`hybridconnector` in WebSocket mode with its normal JSON fallback encoding;
protobuf and route dictionaries are disabled because this benchmark contract
does not ship framework-specific protobuf definitions.

The native driver uses the MIT-licensed `pomelo-jsclient-websocket` 0.1.1
client with `pinus-protocol` 1.7.3. One independently instantiated compatible
client owns each persistent connection. Pinus's required local master process
is lifecycle-only and is outside the measured request path.

Exact npm artifacts and integrity hashes are recorded by `package-lock.json`.
Pinus 1.7.3 currently emits a Node.js deprecation warning for `util.isFunction`
on Node.js 22; it is captured in server stderr and does not invalidate a run.
