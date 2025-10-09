using Microsoft.Extensions.Logging;
using SnapTunnel.Configurations;
using SnapTunnel.Interfaces;
using SnapTunnel.Models;
using System.Buffers;
using System.Net.Mime;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;
using System.Text;

namespace SnapTunnel.Services
{
    public class TunnelService : ITunnelService
    {
        // IIS handle 16KB headers max
        const int DefaultBufferSize = 8192 * 2;

        private readonly ILogger<TunnelService> _logger;
        private readonly IHttpProtocolService _httpProtocolService;
        private readonly IMimeService _mimeService;
        public TunnelService(ILogger<TunnelService> logger, IHttpProtocolService httpProtocolService, IMimeService mimeService)
        {
            _logger = logger;
            _httpProtocolService = httpProtocolService;
            _mimeService = mimeService;
        }

        public async Task StartTunnelAsync(CreateTunnelHostModel createTunnelHostModel, CancellationToken cancellationToken = default)
        {
            try
            {
                TcpListener listener = new TcpListener(createTunnelHostModel.ServerAddress, createTunnelHostModel.ServerPort);
                listener.Start();
                _logger.LogInformation("Tunnel started and listening on {server}:{port}.", createTunnelHostModel.ServerAddress, createTunnelHostModel.ServerPort);

                while (true)
                {
                    TcpClient localClient = await listener.AcceptTcpClientAsync(cancellationToken);
                    _logger.LogInformation("Accepted connection from {RemoteEndPoint}", localClient.Client.RemoteEndPoint);

                    _ = Task.Run(async () =>
                    {
                        try
                        {
                            // Set up secure local connection
                            using NetworkStream localNetStream = localClient.GetStream();

                            var localStreamTunnel = new StreamTunnel
                            {
                                TcpClient = localClient,
                                Stream = localNetStream
                            };

                            if (createTunnelHostModel.UseHttps)
                            {
                                SslStream sslLocalStream = new SslStream(localNetStream, false);
                                localStreamTunnel.Stream = sslLocalStream;
                                X509Certificate2 serverCertificate = createTunnelHostModel.CertificateDomains;
                                await sslLocalStream.AuthenticateAsServerAsync(
                                    serverCertificate, clientCertificateRequired: false,
                                    enabledSslProtocols: SslProtocols.Tls12 | SslProtocols.Tls13, checkCertificateRevocation: false);
                                _logger.LogInformation("Local SSL connection established.");
                            }

                            var tcsRemoteStreamTunnel = new TaskCompletionSource<StreamTunnel>();

                            // Start bidirectional streaming between the two SSL streams.
                            var copyLocalToRemote = StreamLocalToRemoteAsync(
                                                            localStreamTunnel,
                                                            createTunnelHostModel.TunnelsConfiguration,
                                                            tcsRemoteStreamTunnel,
                                                            cancellationToken);

                            var taskCompleted = await Task.WhenAny(copyLocalToRemote, tcsRemoteStreamTunnel.Task);

                            if (taskCompleted == tcsRemoteStreamTunnel.Task)
                            {
                                var remoteStreamTunnel = await tcsRemoteStreamTunnel.Task;
                                var copyRemoteToLocal = CopyStreamAsync(remoteStreamTunnel, localStreamTunnel, cancellationToken);
                                await Task.WhenAny(copyLocalToRemote, copyRemoteToLocal);
                            }
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError(ex, "Error processing tunnel connection.");
                        }
                        finally
                        {
                        }
                    });
                }
            }
            catch (OperationCanceledException ex)
            {
                _logger.LogWarning(ex, "Task has been cancelled.");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error starting tunnel.");
            }
        }

        async Task<StreamTunnel> ConnectToRemoteAsync(string ip, string host, int port, bool useSsl, CancellationToken cancellationToken)
        {
            var remoteClient = new TcpClient();
            await remoteClient.ConnectAsync(ip, port, cancellationToken);

            NetworkStream remoteNetStream = remoteClient.GetStream();

            if (!useSsl)
            {
                _logger.LogInformation($"Connected to remote host {host}/{ip}:{port}.");

                return new StreamTunnel
                {
                    TcpClient = remoteClient,
                    Stream = remoteNetStream,
                };
            }

            SslStream sslRemoteStream = new SslStream(remoteNetStream, false);

            await sslRemoteStream.AuthenticateAsClientAsync(host, null,
               SslProtocols.Tls12 | SslProtocols.Tls13, checkCertificateRevocation: false);

            _logger.LogInformation($"Connected securely to remote host {host}/{ip}:{port}.");

            return new StreamTunnel
            {
                TcpClient = remoteClient,
                Stream = sslRemoteStream,
            };
        }




        private async Task StreamLocalToRemoteAsync(StreamTunnel input, IEnumerable<TunnelConfiguration> tunnelsConfiguration, TaskCompletionSource<StreamTunnel> tcsRemoteStreamTunnel, CancellationToken cancellationToken = default)
        {
            var arrayPool = ArrayPool<byte>.Shared;
            var buffer = arrayPool.Rent(DefaultBufferSize);

            try
            {
                StreamTunnel? outputTunnel = null;

                while (true)
                {
                    int totalBytesRead = 0;
                    int bytesRead;

                    while ((bytesRead = await input.Stream.ReadAsync(buffer, totalBytesRead, buffer.Length - totalBytesRead, cancellationToken)) > 0)
                    {
                        totalBytesRead += bytesRead;

                        if (_httpProtocolService.IsHttpHeadersComplete(buffer, totalBytesRead))
                        {
                            break;
                        }

                        _logger.LogDebug("HTTP header incomplete.");
                        if (totalBytesRead >= buffer.Length)
                        {
                            buffer = ResizeArrayFromArrayPool(arrayPool, buffer, totalBytesRead);
                        }
                    }

                    if (bytesRead == 0)
                    {
                        _logger.LogInformation("Socket is closed");
                        break;
                    }

                    var httpPathMethodModel = _httpProtocolService.ReadHttpPathMethod(buffer, 0, totalBytesRead);
                    if (httpPathMethodModel == null)
                    {
                        throw new Exception("HTTP verb not found in HTTP request.");
                    }

                    input.HttpPathMethod = httpPathMethodModel;
                    var headers = _httpProtocolService.ReadHttpHeaderValues(buffer, 0, totalBytesRead, out var endOfHeaderPosition);

                    if (!headers.TryGetValue("host", out var headerHost))
                    {
                        throw new Exception("Host header not found in HTTP request.");
                    }

                    input.Headers = headers;

                    _logger.LogDebug("{data}", Encoding.UTF8.GetString(buffer, 0, totalBytesRead));

                    var tunnelConfiguration = tunnelsConfiguration.FirstOrDefault(a => string.Equals(a.DomainSource, headerHost.Value, StringComparison.OrdinalIgnoreCase));

                    if (tunnelConfiguration == null)
                    {
                        throw new Exception($"Domain {headerHost.Value} not found in DNS cache.");
                    }

                    if (outputTunnel == null)
                    {
                        var ip = tunnelConfiguration.IpDestination.AddressList.First().ToString();
                        var destPort = tunnelConfiguration.PortDestination;
                        bool useSsl = tunnelConfiguration.UseHttpsDestination;

                        outputTunnel = await ConnectToRemoteAsync(ip, tunnelConfiguration.DomainDestination, destPort, useSsl, cancellationToken);

                        tcsRemoteStreamTunnel.TrySetResult(outputTunnel);
                    }

                    headers.TryGetValue("content-length", out var headerContentLength);
                    headers.TryGetValue("transfer-encoding", out var headerTransferEncoding);

                    if (long.TryParse(headerContentLength?.Value, out var contentLength))
                    {
                        outputTunnel.ContentLength = contentLength;
                    }

                    outputTunnel.IsChunked = headerTransferEncoding?.Value?.Contains("chunked", StringComparison.OrdinalIgnoreCase) == true;

                    // Now replace the domain (Host: xxxxx.com) if they are not the same
                    bool isSameDomain = string.Equals(headerHost.Value, tunnelConfiguration.DomainDestination, StringComparison.OrdinalIgnoreCase);

                    bool isSamePath = true;
                    string? newPath = null;

                    foreach (var pathReplace in tunnelConfiguration.PathReplaces)
                    {
                        newPath = pathReplace.PathRegexMatch.Replace(httpPathMethodModel.Path, pathReplace.PathRegexReplace);
                        isSamePath = newPath == httpPathMethodModel.Path;
                    }


                    if (tunnelConfiguration.OverrideContents != null
                       && tunnelConfiguration.OverrideContents.TryGetValue(httpPathMethodModel.Path, out var filePath))
                    {
                        var fileInfo = new FileInfo(filePath);

                        if (!fileInfo.Exists)
                        {
                            _logger.LogWarning("File {file} not found to replace request {path}", filePath, httpPathMethodModel.Path);
                        }
                        else
                        {
                            await SendFileHttpResponseAsync(input, httpPathMethodModel, filePath, fileInfo, cancellationToken);
                            continue;
                        }
                    }

                    if (!isSameDomain || !isSamePath)
                    {
                        if (!isSamePath)
                        {
                            await outputTunnel.Stream.WriteAsync(buffer.AsMemory(0, httpPathMethodModel.PathPosition));
                            var pathData = Encoding.UTF8.GetBytes(newPath);
                            await outputTunnel.Stream.WriteAsync(pathData, 0, pathData.Length);
                            int positionAfterPath = httpPathMethodModel.PathPosition + httpPathMethodModel.Path.Length;
                            await outputTunnel.Stream.WriteAsync(buffer.AsMemory(positionAfterPath, headerHost.Position - positionAfterPath));
                        }
                        else
                        {
                            await outputTunnel.Stream.WriteAsync(buffer.AsMemory(0, headerHost.Position));
                        }

                        //inject the domain
                        var domainData = Encoding.UTF8.GetBytes(tunnelConfiguration.DomainDestination);
                        await outputTunnel.Stream.WriteAsync(domainData, 0, domainData.Length);
                        int positionAfterHost = headerHost.Position + headerHost.Length;
                        await outputTunnel.Stream.WriteAsync(buffer.AsMemory(positionAfterHost, totalBytesRead - positionAfterHost));
                    }
                    else
                    {
                        await outputTunnel.Stream.WriteAsync(buffer.AsMemory(0, totalBytesRead));
                    }

                    await outputTunnel.Stream.FlushAsync();

                    await StreamEndOfHttpRequest(input, buffer, totalBytesRead, endOfHeaderPosition, outputTunnel, cancellationToken);
                }
            }
            catch (OperationCanceledException ex)
            {
                _logger.LogWarning(ex, "Task has been cancelled.");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error detecting host, make sure the stream is respecting HTTP protocol.");
            }
            finally
            {
                arrayPool.Return(buffer);
                input?.Stream?.Dispose();
                input?.TcpClient?.Dispose();
            }
        }

        private async Task SendFileHttpResponseAsync(StreamTunnel input, HttpPathMethodModel httpPathMethodModel, string filePath, FileInfo fileInfo, CancellationToken cancellationToken)
        {
            var contentType = _mimeService.GetMimeForExtension(fileInfo.Extension);

            var httpVersionAndStatus = $"""
HTTP/1.1 200 OK
Server: SnapTunnel/1.0
Date: {DateTime.UtcNow:R}
Content-Type: {contentType}
Content-Length: {fileInfo.Length}


""";
            var buffHttpVersionAndStatus = Encoding.UTF8.GetBytes(httpVersionAndStatus);
            await input.Stream.WriteAsync(buffHttpVersionAndStatus);
            using var filestream = fileInfo.OpenRead();
            await filestream.CopyToAsync(input.Stream, cancellationToken);

            _logger.LogInformation("Replaced request {path} by file content {file}", httpPathMethodModel.Path, filePath);
        }

        private async Task StreamEndOfHttpRequest(StreamTunnel input, byte[] buffer, int totalBytesRead, int endOfHeaderPosition, StreamTunnel outputTunnel, CancellationToken cancellationToken)
        {
            if (outputTunnel.IsChunked)
            {
                var indexZeroLengthChunk = -1;
                do
                {
                    indexZeroLengthChunk = _httpProtocolService.IndexOfZeroLengthChunk(buffer, totalBytesRead);

                    // already have the zero length chunk
                    if (indexZeroLengthChunk >= 0)
                    {
                        int lengthCRLFCRLF = 4;
                        var remainingBuffer = buffer.AsSpan(indexZeroLengthChunk, totalBytesRead - indexZeroLengthChunk + lengthCRLFCRLF)
                                                          .ToArray();

                        input.RemainingBytes = remainingBuffer;
                        break;
                    }

                    var bytesRead = await input.Stream.ReadAsync(buffer, 0, buffer.Length, cancellationToken);

                    if (bytesRead == 0)
                    {
                        _logger.LogWarning("Stream ended before zero-length chunk was found.");
                        break;
                    }

                    _logger.LogDebug("{data}", Encoding.UTF8.GetString(buffer, 0, bytesRead));
                    await outputTunnel.Stream.WriteAsync(buffer.AsMemory(0, bytesRead), cancellationToken);
                    await outputTunnel.Stream.FlushAsync(cancellationToken);

                    totalBytesRead = buffer.Length;
                }
                while (indexZeroLengthChunk == -1);
            }
            else
            {
                var contentLength = outputTunnel.ContentLength;
                var remainingBytesToRead = contentLength - (totalBytesRead - endOfHeaderPosition);

                //totalBytesRead
                var bytesRead = 0;
                while (remainingBytesToRead > 0)
                {
                    if (remainingBytesToRead < buffer.Length)
                    {
                        bytesRead = await input.Stream.ReadAsync(buffer, 0, (int)remainingBytesToRead, cancellationToken);
                    }
                    else
                    {
                        bytesRead = await input.Stream.ReadAsync(buffer, 0, buffer.Length, cancellationToken);
                    }
                    if (bytesRead == 0)
                    {
                        _logger.LogWarning("Stream ended before all content was read.");
                        break;
                    }
                    remainingBytesToRead -= bytesRead;
                    totalBytesRead += bytesRead;

                    _logger.LogDebug("{data}", Encoding.UTF8.GetString(buffer, 0, bytesRead));
                    await outputTunnel.Stream.WriteAsync(buffer.AsMemory(0, bytesRead), cancellationToken);
                    await outputTunnel.Stream.FlushAsync(cancellationToken);
                }

            }

        }

        private static byte[] ResizeArrayFromArrayPool(ArrayPool<byte> arrayPool, byte[] buffer, int totalBytesRead)
        {
            var newBuffer = arrayPool.Rent(buffer.Length * 2);
            Array.Copy(buffer, 0, newBuffer, 0, totalBytesRead);
            arrayPool.Return(buffer);
            return newBuffer;
        }


        private async Task CopyStreamAsync(StreamTunnel input, StreamTunnel output, CancellationToken cancellationToken = default)
        {
            var arrayPool = ArrayPool<byte>.Shared;
            var buffer = arrayPool.Rent(DefaultBufferSize);

            int bytesRead;
            try
            {
                while ((bytesRead = await input.Stream.ReadAsync(buffer, 0, buffer.Length, cancellationToken)) > 0)
                {
                    var strData = Encoding.UTF8.GetString(buffer, 0, bytesRead);
                    _logger.LogInformation("{data}", strData);
                    await output.Stream.WriteAsync(buffer.AsMemory(0, bytesRead));
                    await output.Stream.FlushAsync();
                }
            }
            catch (OperationCanceledException ex)
            {
                _logger.LogWarning(ex, "Task has been cancelled.");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error copying stream data.");
            }
            finally
            {
                arrayPool.Return(buffer);
                input?.Stream?.Dispose();
                input?.TcpClient?.Dispose();
            }
        }
    }
}