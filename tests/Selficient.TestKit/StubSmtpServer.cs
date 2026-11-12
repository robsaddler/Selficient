using System.Net;
using System.Net.Sockets;
using System.Text;

namespace Selficient.TestKit;

/// <summary>
/// A mail server that listens on this machine, speaks just enough SMTP to take a message, and
/// keeps what it was given. It stands in for Papercut, so the SMTP adapter is proved against a
/// real socket and a real conversation and not against a mock of the client.
/// </summary>
public sealed class StubSmtpServer : IDisposable
{
    private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
    private readonly CancellationTokenSource _stop = new();
    private readonly List<StubMail> _received = [];
    private readonly Task _loop;
    private bool _disposed;

    /// <summary>Starts listening on a free port.</summary>
    public StubSmtpServer(bool offersSignIn = false, string? refuseRecipientsWith = null)
    {
        OffersSignIn = offersSignIn;
        RefuseRecipientsWith = refuseRecipientsWith;
        _listener.Start();
        Port = ((IPEndPoint)_listener.LocalEndpoint).Port;
        _loop = Task.Run(AcceptAsync);
    }

    /// <summary>The port it answers on.</summary>
    public int Port { get; }

    /// <summary>Whether it tells clients they may sign in, as a relay does and Papercut does not.</summary>
    public bool OffersSignIn { get; }

    /// <summary>The reply every recipient gets instead of 250, to play a server that refuses the message.</summary>
    public string? RefuseRecipientsWith { get; }

    /// <summary>Every message taken, oldest first.</summary>
    public IReadOnlyList<StubMail> Received
    {
        get
        {
            lock (_received)
            {
                return [.. _received];
            }
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _stop.Cancel();
        _listener.Stop();
        try
        {
            _loop.Wait(TimeSpan.FromSeconds(2));
        }
        catch (AggregateException)
        {
            // The loop ends by its listener closing under it.
        }

        _stop.Dispose();
    }

    private async Task AcceptAsync()
    {
        while (!_stop.IsCancellationRequested)
        {
            TcpClient client;
            try
            {
                client = await _listener.AcceptTcpClientAsync(_stop.Token);
            }
            catch (Exception exception) when (exception is OperationCanceledException or SocketException or ObjectDisposedException)
            {
                return;
            }

            _ = Task.Run(() => TalkAsync(client));
        }
    }

    private async Task TalkAsync(TcpClient client)
    {
        using (client)
        {
            try
            {
                await using NetworkStream stream = client.GetStream();
                using StreamReader reader = new(stream, Encoding.Latin1);
                await using StreamWriter writer = new(stream, Encoding.Latin1) { NewLine = "\r\n", AutoFlush = true };
                await writer.WriteLineAsync("220 stub.test ESMTP");
                string from = string.Empty;
                List<string> to = [];
                List<string> credentials = [];
                while (await reader.ReadLineAsync(_stop.Token) is { } line)
                {
                    string verb = line.Length >= 4 ? line[..4].ToUpperInvariant() : line.ToUpperInvariant();
                    if (verb is "EHLO" or "HELO")
                    {
                        await writer.WriteLineAsync(OffersSignIn ? "250-stub.test\r\n250 AUTH LOGIN" : "250 stub.test");
                    }
                    else if (verb == "AUTH")
                    {
                        // AUTH LOGIN, with or without the user on the same line: ask until both halves are in.
                        string[] parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                        if (parts.Length > 2)
                        {
                            credentials.Add(Decode(parts[2]));
                        }

                        while (credentials.Count < 2)
                        {
                            await writer.WriteLineAsync("334 UGFzc3dvcmQ6");
                            credentials.Add(Decode(await reader.ReadLineAsync(_stop.Token) ?? string.Empty));
                        }

                        await writer.WriteLineAsync("235 2.7.0 Authentication successful");
                    }
                    else if (verb == "MAIL")
                    {
                        from = line;
                        await writer.WriteLineAsync("250 OK");
                    }
                    else if (verb == "RCPT")
                    {
                        to.Add(line);
                        await writer.WriteLineAsync(RefuseRecipientsWith ?? "250 OK");
                    }
                    else if (verb == "DATA")
                    {
                        await writer.WriteLineAsync("354 End data with <CR><LF>.<CR><LF>");
                        StringBuilder data = new();
                        while (await reader.ReadLineAsync(_stop.Token) is { } row && row != ".")
                        {
                            data.Append(row).Append('\n');
                        }

                        lock (_received)
                        {
                            _received.Add(new StubMail(from, [.. to], data.ToString(), [.. credentials]));
                        }

                        await writer.WriteLineAsync("250 OK queued");
                    }
                    else if (verb == "QUIT")
                    {
                        await writer.WriteLineAsync("221 Bye");
                        return;
                    }
                    else
                    {
                        await writer.WriteLineAsync("250 OK");
                    }
                }
            }
            catch (Exception exception) when (exception is IOException or OperationCanceledException or ObjectDisposedException)
            {
                // The client hung up, or the test ended.
            }
        }
    }

    private static string Decode(string base64)
    {
        try
        {
            return Encoding.UTF8.GetString(Convert.FromBase64String(base64));
        }
        catch (FormatException)
        {
            return base64;
        }
    }
}

/// <summary>One message as the stub server took it.</summary>
/// <param name="From">The MAIL FROM line.</param>
/// <param name="To">The RCPT TO lines.</param>
/// <param name="Data">Headers and body, as sent.</param>
/// <param name="Credentials">The user and password the client signed in with, if it did.</param>
public sealed record StubMail(string From, IReadOnlyList<string> To, string Data, IReadOnlyList<string> Credentials);
