using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;

// No Unity dependency: future Discord command/submissions intake uses the same preparation path.
public static class ImageAttachmentService
{
    public static async Task PrepareAsync(IEnumerable<ImageAttachment> images,
        Func<ImageAttachment, Task<string>> analyze, Func<string, Task<byte[]>> download = null)
    {
        foreach (var image in (images ?? Enumerable.Empty<ImageAttachment>()).Where(value => value != null).Take(ImageAttachmentContext.MaxImages))
        {
            if (string.IsNullOrEmpty(image.DataBase64) && string.IsNullOrEmpty(image.DownloadError))
            {
                try
                {
                    var bytes = await (download ?? DownloadAsync)(image.Url);
                    if (!TryReadImage(bytes, out var mime)) throw new InvalidDataException("Expected a PNG or JPEG, at most 2 MiB and 16 million pixels.");
                    image.MimeType = mime;
                    image.DataBase64 = Convert.ToBase64String(bytes);
                    using var hash = SHA256.Create();
                    image.Sha256 = BitConverter.ToString(hash.ComputeHash(bytes)).Replace("-", "").ToLowerInvariant();
                    image.RetrievedAtUtc = DateTime.UtcNow.ToString("O");
                }
                catch (Exception e) { image.DownloadError = ImageAttachmentContext.Limit(e.Message, 500); }
            }
            // Validate embedded inputs too (e.g. future command uploads), not just downloads.
            if (!string.IsNullOrEmpty(image.DataBase64))
            {
                try
                {
                    if (image.DataBase64.Length > ((ImageAttachmentContext.MaxImageBytes + 2) / 3) * 4 ||
                        !TryReadImage(Convert.FromBase64String(image.DataBase64), out var mime))
                        throw new InvalidDataException("Invalid embedded image.");
                    image.MimeType = mime;
                }
                catch (Exception e)
                {
                    image.DataBase64 = null;
                    image.DownloadError = ImageAttachmentContext.Limit(e.Message, 500);
                }
            }
            if (!image.UseForGeneration || string.IsNullOrEmpty(image.DataBase64) ||
                !string.IsNullOrEmpty(image.Analysis) || !string.IsNullOrEmpty(image.AnalysisError) || analyze == null) continue;
            try
            {
                var result = await analyze(image);
                if (string.IsNullOrWhiteSpace(result)) throw new InvalidDataException("Image analysis returned no context.");
                image.Analysis = ImageAttachmentContext.Limit(result.Trim(), ImageAttachmentContext.MaxAnalysisChars);
            }
            catch (Exception e) { image.AnalysisError = ImageAttachmentContext.Limit(e.Message, 500); }
        }
    }

    public static async Task<byte[]> DownloadAsync(string url)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        for (var redirect = 0; redirect <= 3; redirect++)
        {
            if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme != "https" ||
                uri.Port != 443 || !string.IsNullOrEmpty(uri.UserInfo) || uri.IsLoopback)
                throw new InvalidDataException("Image URL must be public HTTPS.");
            var addresses = await Dns.GetHostAddressesAsync(uri.DnsSafeHost);
            if (addresses.Length == 0 || addresses.Any(address => !IsPublicAddress(address)))
                throw new InvalidDataException("Image URL must resolve to public addresses.");
            var request = (HttpWebRequest)WebRequest.Create(uri);
            request.AllowAutoRedirect = false;
            request.UserAgent = "HBOx-ImageContext/1.0";
            request.Timeout = 20000;
            request.ReadWriteTimeout = 20000;
            using var cancellation = timeout.Token.Register(request.Abort);
            using var response = (HttpWebResponse)await request.GetResponseAsync();
            if ((int)response.StatusCode >= 300 && (int)response.StatusCode < 400)
            {
                url = new Uri(uri, response.Headers["Location"] ?? throw new InvalidDataException("Missing image redirect.")).AbsoluteUri;
                continue;
            }
            if (response.ContentLength > ImageAttachmentContext.MaxImageBytes) throw new InvalidDataException("Image exceeds 2 MiB.");
            using var input = response.GetResponseStream();
            using var output = new MemoryStream();
            var buffer = new byte[8192];
            int count;
            while ((count = await input.ReadAsync(buffer, 0, buffer.Length, timeout.Token)) != 0)
            {
                if (output.Length + count > ImageAttachmentContext.MaxImageBytes) throw new InvalidDataException("Image exceeds 2 MiB.");
                output.Write(buffer, 0, count);
            }
            return output.ToArray();
        }
        throw new InvalidDataException("Too many image redirects.");
    }

    public static bool IsPublicAddress(IPAddress address)
    {
        if (address.IsIPv4MappedToIPv6) address = address.MapToIPv4();
        var b = address.GetAddressBytes();
        if (address.AddressFamily == AddressFamily.InterNetworkV6) return (b[0] & 0xe0) == 0x20;
        return address.AddressFamily == AddressFamily.InterNetwork && b[0] != 0 && b[0] != 10 && b[0] != 127 && b[0] < 224 &&
            !(b[0] == 169 && b[1] == 254) && !(b[0] == 172 && b[1] >= 16 && b[1] <= 31) &&
            !(b[0] == 192 && b[1] == 168) && !(b[0] == 100 && b[1] >= 64 && b[1] <= 127);
    }

    public static bool TryReadImage(byte[] bytes, out string mime)
    {
        mime = null;
        if (bytes == null || bytes.Length < 24 || bytes.Length > ImageAttachmentContext.MaxImageBytes) return false;
        if (bytes.Take(8).SequenceEqual(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }) &&
            bytes[12] == 73 && bytes[13] == 72 && bytes[14] == 68 && bytes[15] == 82)
        {
            var width = ((long)bytes[16] << 24) | ((long)bytes[17] << 16) | ((long)bytes[18] << 8) | bytes[19];
            var height = ((long)bytes[20] << 24) | ((long)bytes[21] << 16) | ((long)bytes[22] << 8) | bytes[23];
            if (!ValidDimensions(width, height)) return false;
            mime = "image/png";
            return true;
        }
        if (bytes[0] != 255 || bytes[1] != 216) return false;
        for (var offset = 2; offset + 3 < bytes.Length;)
        {
            if (bytes[offset++] != 255) return false;
            while (offset < bytes.Length && bytes[offset] == 255) offset++;
            if (offset + 2 >= bytes.Length) return false;
            var marker = bytes[offset++];
            if (marker == 0xda || marker == 0xd9) return false;
            var length = bytes[offset] * 256 + bytes[offset + 1];
            if (length < 2 || offset + length > bytes.Length) return false;
            if (marker >= 0xc0 && marker <= 0xc3 && length >= 8)
            {
                if (!ValidDimensions(bytes[offset + 5] * 256 + bytes[offset + 6], bytes[offset + 3] * 256 + bytes[offset + 4])) return false;
                mime = "image/jpeg";
                return true;
            }
            offset += length;
        }
        return false;
    }

    private static bool ValidDimensions(long width, long height) => width > 0 && height > 0 && width <= 8192 && height <= 8192 && width * height <= 16000000;
}
