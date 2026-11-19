param(
    [Parameter(Mandatory)][int]$Port,
    [Parameter(Mandatory)][ValidateSet('tcp', 'udp')][string]$Protocol
)

$endpoint = [System.Net.IPEndPoint]::new([System.Net.IPAddress]::Loopback, $Port)
$socket = if ($Protocol -eq 'tcp') {
    [System.Net.Sockets.Socket]::new('InterNetwork', 'Stream', 'Tcp')
} else {
    [System.Net.Sockets.Socket]::new('InterNetwork', 'Dgram', 'Udp')
}
try {
    $socket.ExclusiveAddressUse = $true
    $socket.Bind($endpoint)
} catch {
    throw "Cannot bind $endpoint/$Protocol exclusively. $($_.Exception.GetBaseException().Message)"
} finally {
    $socket.Dispose()
}
