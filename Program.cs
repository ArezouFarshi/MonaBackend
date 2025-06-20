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
        var contractAddress = "0x4F3AC69d127A8b0Ad3b9dFaBdc3A19DC3B34c240";
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
