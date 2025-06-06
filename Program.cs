using System;
using System.Net.WebSockets;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using Nethereum.Web3;
using Nethereum.RPC.Eth.DTOs;
using Nethereum.ABI.FunctionEncoding.Attributes;
using Nethereum.Contracts;

[Event("VisibilityChanged")]
public class VisibilityChangedEventDTO : IEventDTO
{
    [Parameter("bool", "visible", 1, false)]
    public bool Visible { get; set; }
}

class Program
{
    static ConcurrentBag<WebSocket> clients = new ConcurrentBag<WebSocket>();

    static async Task StartWebSocketServer()
    {
        var port = Environment.GetEnvironmentVariable("PORT") ?? "10000";
        HttpListener listener = new HttpListener();
        listener.Prefixes.Add($"http://+:{port}/");
        listener.Start();

        Console.WriteLine($"✅ WebSocket server listening on http://0.0.0.0:{port}/");

        while (true)
        {
            var context = await listener.GetContextAsync();
            if (context.Request.IsWebSocketRequest)
            {
                var wsContext = await context.AcceptWebSocketAsync(null);
                Console.WriteLine("🌐 Unity client connected.");
                clients.Add(wsContext.WebSocket);
            }
            else
            {
                // Respond to HTTP GET on root path
                var message = Encoding.UTF8.GetBytes("👋 MonaBackend is running!");
                context.Response.ContentType = "text/plain";
                context.Response.ContentLength64 = message.Length;
                await context.Response.OutputStream.WriteAsync(message, 0, message.Length);
                context.Response.OutputStream.Close();
            }
        }
    }

    static async Task StartBlockchainListener()
    {
        // ---- DO NOT EDIT THESE! ----
        var web3 = new Web3("https://sepolia.infura.io/v3/6ad85a144d0445a3b181add73f6a55d9");
        var contractAddress = "0x4F3AC69d127A8b0Ad3b9dFaBdc3A19DC3B34c240";
        // ----------------------------

        var eventHandler = web3.Eth.GetEvent<VisibilityChangedEventDTO>(contractAddress);

        // Track the last processed block and logIndex
        BigInteger lastBlock = 0;
        BigInteger lastLogIndex = -1;

        Console.WriteLine("👂 Listening for VisibilityChanged events...");

        while (true)
        {
            // Always fetch events from the last seen block onward
            var filter = eventHandler.CreateFilterInput(new BlockParameter(lastBlock));
            var logs = await eventHandler.GetAllChangesAsync(filter);

            foreach (var ev in logs)
            {
                var blockNumber = (long)ev.Log.BlockNumber.Value;
                var logIndex = (long)ev.Log.LogIndex.Value;

                // Only process if this is after last processed, or a new log in the same block
                if (blockNumber > (long)lastBlock ||
                    (blockNumber == (long)lastBlock && logIndex > (long)lastLogIndex))
                {
                    lastBlock = ev.Log.BlockNumber.Value;
                    lastLogIndex = ev.Log.LogIndex.Value;

                    bool isVisible = ev.Event.Visible;
                    Console.WriteLine($"[Blockchain] VisibilityChanged: {isVisible}");

                    var json = JsonSerializer.Serialize(new { visible = isVisible });
                    var message = Encoding.UTF8.GetBytes(json);

                    foreach (var socket in clients)
                    {
                        if (socket.State == WebSocketState.Open)
                        {
                            await socket.SendAsync(new ArraySegment<byte>(message), WebSocketMessageType.Text, true, CancellationToken.None);
                        }
                    }
                }
            }

            await Task.Delay(5000); // Poll every 5 seconds
        }
    }

    static async Task Main(string[] args)
    {
        await Task.WhenAll(StartWebSocketServer(), StartBlockchainListener());
    }
}
