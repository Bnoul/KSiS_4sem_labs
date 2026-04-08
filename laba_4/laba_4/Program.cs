using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Linq;

class Program
{
    private const int ProxyPort = 9854;
    private const string ProxyIP = "127.0.0.2";
    private static HashSet<string> BlacklistDomains = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    private static HashSet<string> BlacklistUrls = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

    static void Main(string[] args)
    {
        LoadBlacklist("black_list.txt");

        Socket listener = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
        listener.Bind(new IPEndPoint(IPAddress.Parse(ProxyIP), ProxyPort));
        listener.Listen(100);
        Console.WriteLine($"[INFO] HTTP proxy started on {ProxyIP}:{ProxyPort}");

        while (true)
        {
            Socket clientSocket = listener.Accept();
            ThreadPool.QueueUserWorkItem(HandleClient, clientSocket);
        }
    }

    private static void LoadBlacklist(string path)
    {
        BlacklistDomains.Clear();
        BlacklistUrls.Clear();
        if (!File.Exists(path))
            return;

        foreach (var line in File.ReadAllLines(path))
        {
            var trimmed = line.Trim();
            if (string.IsNullOrEmpty(trimmed) || trimmed.StartsWith("#"))
                continue;

            if (trimmed.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
                trimmed.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                BlacklistUrls.Add(trimmed);
            else
                BlacklistDomains.Add(trimmed);
        }
    }

    private static void HandleClient(object state)
    {
        LoadBlacklist("black_list.txt");
        Socket clientSocket = (Socket)state;
        NetworkStream clientStream = new NetworkStream(clientSocket, ownsSocket: true);
        clientStream.ReadTimeout = 30000;
        clientStream.WriteTimeout = 30000;

        try
        {
            var requestHeader = ReadHttpHeader(clientStream);
            if (string.IsNullOrEmpty(requestHeader))
                return;

            var headerLines = requestHeader.Split(new[] { "\r\n" }, StringSplitOptions.None);
            var requestLine = headerLines[0].Split(' ');
            if (requestLine.Length < 3)
                return;

            string method = requestLine[0];
            string uriString = requestLine[1];
            string httpVersion = requestLine[2];

            if (method.Equals("CONNECT", StringComparison.OrdinalIgnoreCase))
            {
                WriteSimpleResponse(clientStream, "501 Not Implemented", "CONNECT not supported.");
                Console.WriteLine($"{Time()} | {method} {uriString} | 501 NOT IMPLEMENTED");
                return;
            }

            Uri uri;
            if (!Uri.TryCreate(uriString, UriKind.Absolute, out uri))
            {
                string hostHeader = headerLines.Skip(1).FirstOrDefault(h => h.StartsWith("Host:", StringComparison.OrdinalIgnoreCase));
                if (hostHeader == null)
                {
                    WriteSimpleResponse(clientStream, "400 Bad Request", "Host header missing.");
                    Console.WriteLine($"{Time()} | {method} {uriString} | 400 BAD REQUEST");
                    return;
                }

                string host = hostHeader.Substring(5).Trim();
                if (!Uri.TryCreate("http://" + host + uriString, UriKind.Absolute, out uri))
                {
                    WriteSimpleResponse(clientStream, "400 Bad Request", "Invalid URI.");
                    Console.WriteLine($"{Time()} | {method} {uriString} | 400 BAD REQUEST");
                    return;
                }
            }

            if (IsBlocked(uri))
            {
                string body = $"<html><body><h1>Blocked</h1><p>{WebUtility.HtmlEncode(uri.ToString())}</p></body></html>";
                WriteCustomHtmlResponse(clientStream, "403 Forbidden", body);
                Console.WriteLine($"{Time()} | {method} {uri} | 403 FORBIDDEN");
                return;
            }

            int port = uri.Port > 0 ? uri.Port : 80;

            Socket serverSocket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
            serverSocket.ReceiveTimeout = 30000;
            serverSocket.SendTimeout = 30000;
            serverSocket.Connect(uri.Host, port);

            NetworkStream serverStream = new NetworkStream(serverSocket, ownsSocket: true);

            string path = string.IsNullOrEmpty(uri.PathAndQuery) ? "/" : uri.PathAndQuery;
            string newRequestLine = $"{method} {path} {httpVersion}\r\n";

            var newHeader = new StringBuilder();
            newHeader.Append(newRequestLine);

            bool hasHost = false;
            foreach (var line in headerLines.Skip(1))
            {
                if (string.IsNullOrEmpty(line))
                    continue;

                if (line.StartsWith("Proxy-Connection:", StringComparison.OrdinalIgnoreCase))
                    continue;

                if (line.StartsWith("Host:", StringComparison.OrdinalIgnoreCase))
                {
                    hasHost = true;
                    newHeader.Append($"Host: {uri.Host}:{port}\r\n");
                }
                else
                {
                    newHeader.Append(line + "\r\n");
                }
            }

            if (!hasHost)
                newHeader.Append($"Host: {uri.Host}:{port}\r\n");

            newHeader.Append("\r\n");

            byte[] headerBytes = Encoding.ASCII.GetBytes(newHeader.ToString());
            serverStream.Write(headerBytes, 0, headerBytes.Length);

            string responseHeader = ReadHttpHeader(serverStream);
            if (string.IsNullOrEmpty(responseHeader))
                return;

            string statusLine = responseHeader.Split('\n')[0];
            string statusCode = statusLine.Split(' ')[1];

            Console.WriteLine($"{Time()} | {method} {uri} | {statusCode} {StatusText(statusCode)}");

            byte[] responseHeaderBytes = Encoding.ASCII.GetBytes(responseHeader);
            clientStream.Write(responseHeaderBytes, 0, responseHeaderBytes.Length);

            CopyStream(serverStream, clientStream);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[ERROR] {ex.Message}");
        }
    }

    private static bool IsBlocked(Uri uri)
    {
        if (BlacklistUrls.Contains(uri.ToString()))
            return true;

        string host = uri.Host;
        if (BlacklistDomains.Contains(host))
            return true;

        foreach (var domain in BlacklistDomains)
            if (host.EndsWith("." + domain, StringComparison.OrdinalIgnoreCase))
                return true;

        return false;
    }

    private static string ReadHttpHeader(NetworkStream stream)
    {
        var buffer = new List<byte>();
        byte[] temp = new byte[1];
        int state = 0;

        while (true)
        {
            int read;
            try { read = stream.Read(temp, 0, 1); }
            catch { break; }

            if (read <= 0)
                break;

            buffer.Add(temp[0]);

            if (state == 0 && temp[0] == '\r') state = 1;
            else if (state == 1 && temp[0] == '\n') state = 2;
            else if (state == 2 && temp[0] == '\r') state = 3;
            else if (state == 3 && temp[0] == '\n') break;
            else state = 0;
        }

        if (buffer.Count == 0)
            return null;

        return Encoding.ASCII.GetString(buffer.ToArray());
    }

    private static void CopyStream(NetworkStream from, NetworkStream to)
    {
        byte[] buffer = new byte[8192];
        int bytesRead;

        try
        {
            while ((bytesRead = from.Read(buffer, 0, buffer.Length)) > 0)
                to.Write(buffer, 0, bytesRead);
        }
        catch { }
    }

    private static void WriteSimpleResponse(NetworkStream stream, string status, string message)
    {
        string body = $"<html><body><h1>{status}</h1><p>{WebUtility.HtmlEncode(message)}</p></body></html>";
        WriteCustomHtmlResponse(stream, status, body);
    }

    private static void WriteCustomHtmlResponse(NetworkStream stream, string status, string body)
    {
        byte[] bodyBytes = Encoding.UTF8.GetBytes(body);
        string header =
            $"HTTP/1.1 {status}\r\n" +
            "Content-Type: text/html; charset=utf-8\r\n" +
            $"Content-Length: {bodyBytes.Length}\r\n" +
            "Connection: close\r\n\r\n";

        byte[] headerBytes = Encoding.ASCII.GetBytes(header);
        stream.Write(headerBytes, 0, headerBytes.Length);
        stream.Write(bodyBytes, 0, bodyBytes.Length);
    }

    private static string StatusText(string code)
    {
        return code switch
        {
            "100" => "CONTINUE",
            "200" => "OK",
            "301" => "MOVED",
            "302" => "FOUND",
            "304" => "NOT MODIFIED",
            "400" => "BAD REQUEST",
            "401" => "UNAUTHORIZED",
            "403" => "FORBIDDEN",
            "404" => "NOT FOUND",
            "500" => "SERVER ERROR",
            "501" => "NOT IMPLEMENTED",
            "502" => "BAD GATEWAY",
            "503" => "SERVICE UNAVAILABLE",
            _ => ""
        };
    }

    private static string Time() => DateTime.Now.ToString("HH:mm:ss");
}
