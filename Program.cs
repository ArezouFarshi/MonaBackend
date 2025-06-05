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
        var infuraUrl = "https://sepolia.infura.io/v3/6ad85a144d0445a3b181add73f6a55d9"; // <<-- CHANGE if needed
        var contractAddress = "0x4F3AC69d127A8b0Ad3b9dFaBdc3A19DC3B34c240"; // <<-- CHANGE if needed
        var web3 = new Web3(infuraUrl);

        var eventHandler = web3.Eth.GetEvent<VisibilityChangedEventDTO>(contractAddress);

        // Start from block 0 (Earliest) and move forward, so you won't miss anything.
        var lastCheckedBlock = (await web3.Eth.Blocks.GetBlockNumber.SendRequestAsync()).Value;

        Console.WriteLine("👂 Listening for VisibilityChanged events...");

        while (true)
        {
            var latestBlockNumber = (await web3.Eth.Blocks.GetBlockNumber.SendRequestAsync()).Value;
            if (latestBlockNumber > lastCheckedBlock)
            {
                var filter = eventHandler.CreateFilterInput(
                    new BlockParameter(lastCheckedBlock + 1), // Start from the next block
                    new BlockParameter(latestBlockNumber)      // Up to the latest
                );

                var logs = await eventHandler.GetAllChangesAsync(filter);
                foreach (var ev in logs)
                {
                    bool isVisible = ev.Event.Visible;
                    Console.WriteLine($"[Blockchain] VisibilityChanged: {isVisible} (Block: {ev.Log.BlockNumber.Value})");

                    var json = JsonSerializer.Serialize(new { visible = isVisible });
                    var message = Encoding.UTF8.GetBytes(json);

                    foreach (var socket in clients)
                    {
                        if (socket.State == WebSocketState.Open)
                        {
                            await socket.SendAsync(new ArraySegment<byte>(message), WebSocketMessageType.Text, true, CancellationToken.None);
                            Console.WriteLine("➡️ Sent message to Unity clients.");
                        }
                    }
                }
                lastCheckedBlock = latestBlockNumber;
            }

            await Task.Delay(3000); // Poll every 3 seconds
        }
    }

    static async Task Main(string[] args)
    {
        await Task.WhenAll(StartWebSocketServer(), StartBlockchainListener());
    }
}
