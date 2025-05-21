using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System.IO;
using System.Net.Sockets;
using System.Threading.Tasks;
using System;
using System.Threading;

namespace WebApplication.Services
{
    // Services/ClamAVService.cs
    public class ClamAVService: IAntivirusChecker
    {
        private readonly IConfiguration _configuration;
        private readonly ILogger<ClamAVService> _logger;

        public ClamAVService(IConfiguration configuration, ILogger<ClamAVService> logger)
        {
            _configuration = configuration;
            _logger = logger;
        }

        public async Task<bool> IsVirus(byte[] fileBytes, CancellationToken cancellationToken = default)
        {
            try
            {
                using (var client = new TcpClient
                {
                    SendTimeout = 30000, // 30 seconds
                    ReceiveTimeout = 30000
                })
                {


                    await client.ConnectAsync(_configuration["ClamAV:Host"],
                        int.Parse(_configuration["ClamAV:Port"]));

                    using (var stream = client.GetStream())
                    using (var writer = new StreamWriter(stream))
                    using (var reader = new StreamReader(stream))
                    {
                        await writer.WriteLineAsync("INSTREAM");
                        await writer.FlushAsync();

                        // Send file in chunks
                        var chunkSize = 2048;
                        for (int offset = 0; offset < fileBytes.Length; offset += chunkSize)
                        {
                            var size = Math.Min(chunkSize, fileBytes.Length - offset);
                            var chunk = new byte[size];
                            Array.Copy(fileBytes, offset, chunk, 0, size);

                            await writer.WriteAsync($"{size:X4}");
                            await stream.WriteAsync(chunk, 0, chunk.Length);
                            await writer.FlushAsync();
                        }

                        await writer.WriteLineAsync("0");
                        await writer.FlushAsync();

                        var response = await reader.ReadLineAsync();
                        return response != "OK";
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error scanning file with ClamAV");
                return false;
            }
        }
    }
}
