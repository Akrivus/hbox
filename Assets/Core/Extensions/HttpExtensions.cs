using System.Net;
using System.Text;
using System.Threading.Tasks;

public static class HttpExtensions
{
    public static void WriteString(this HttpListenerResponse response, string content, string contentType)
    {
        var bytes = Encoding.UTF8.GetBytes(content);
        response.ContentType = contentType;
        response.ContentLength64 = bytes.Length;

        var stream = response.OutputStream;
        stream.Write(bytes, 0, bytes.Length);
        stream.Flush();
    }

    public static Task WriteStringAsync(this HttpListenerResponse response, string content, string contentType)
    {
        var bytes = Encoding.UTF8.GetBytes(content);
        response.ContentType = contentType;
        response.ContentLength64 = bytes.Length;

        return response.OutputStream.WriteAsync(bytes, 0, bytes.Length);
    }
}
