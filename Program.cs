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
using Nethereum.Hex.HexTypes;

[Event("VisibilityChanged")]
public class VisibilityChangedEventDTO : IEventDTO
{
    [Parameter("bool", "visible", 1, false)]
    public bool Visible { get; set; }
}

class Program
{
    static ConcurrentBag<WebSocket> clients = new ConcurrentBag<WebSocket>();

    // NEW: Track the visibility state for each window group
    static ConcurrentDictionary<string, bool> windowVisibility = new ConcurrentDictionary<string, bool>(
        new Dictionary<string, bool>
        {
            ["1stStoryWindows"] = false,
            ["2ndStoryWindows"] = false,
            ["3rdStoryWindows"] = false,
            ["4thStoryWindows"] = false,
        });

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
            else if (context.Request.HttpMethod == "GET" && context.Request.Url.AbsolutePath == "/api/test")
            {
                var response = JsonSerializer.Serialize(new { status = "success", timestamp = DateTime.UtcNow });
                var message = Encoding.UTF8.GetBytes(response);
                context.Response.ContentType = "application/json";
                context.Response.ContentLength64 = message.Length;
                await context.Response.OutputStream.WriteAsync(message, 0, message.Length);
                context.Response.OutputStream.Close();
            }
            // NEW: Visibility API endpoint for Monaverse REST polling
            else if (context.Request.HttpMethod == "GET" && context.Request.Url.AbsolutePath == "/api/visibility")
            {
                var visibilityJson = JsonSerializer.Serialize(windowVisibility);
                var message = Encoding.UTF8.GetBytes(visibilityJson);
                context.Response.ContentType = "application/json";
                context.Response.ContentLength64 = message.Length;
                await context.Response.OutputStream.WriteAsync(message, 0, message.Length);
                context.Response.OutputStream.Close();
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
        var web3 = new Web3("https://sepolia.infura.io/v3/6ad85a144d0445a3b181add73f6a55d9");
        var contractAddress = "0xF47917B108ca4B820CCEA2587546fbB9f7564b56";
        var eventHandler = web3.Eth.GetEvent<VisibilityChangedEventDTO>(contractAddress);

        Console.WriteLine("👂 Listening for VisibilityChanged events...");

        var lastBlock = await web3.Eth.Blocks.GetBlockNumber.SendRequestAsync();

        while (true)
        {
            var currentBlock = await web3.Eth.Blocks.GetBlockNumber.SendRequestAsync();

            if (currentBlock.Value > lastBlock.Value)
            {
                var filter = eventHandler.CreateFilterInput(
                    new BlockParameter(new HexBigInteger(lastBlock.Value + 1)),
                    new BlockParameter(new HexBigInteger(currentBlock.Value))
                );

                var logs = await eventHandler.GetAllChangesAsync(filter);

                foreach (var ev in logs)
                {
                    bool isVisible = ev.Event.Visible;
                    Console.WriteLine($"[Blockchain] NEW VisibilityChanged: {isVisible}");

                    // For now: update all windows the same (you can adjust logic later)
                    windowVisibility["1stStoryWindows"] = isVisible;
                    windowVisibility["2ndStoryWindows"] = isVisible;
                    windowVisibility["3rdStoryWindows"] = isVisible;
                    windowVisibility["4thStoryWindows"] = isVisible;

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

                lastBlock = currentBlock;
            }

            await Task.Delay(20000);
        }
    }

    static async Task Main(string[] args)
    {
        await Task.WhenAll(StartWebSocketServer(), StartBlockchainListener());
    }
}
