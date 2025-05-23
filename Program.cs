using System;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using System.Net.WebSockets;
using System.Collections.Concurrent;

using Nethereum.Web3;
using Nethereum.Contracts;
using Nethereum.RPC.Eth.DTOs;
using Nethereum.ABI.FunctionEncoding.Attributes;

[Event("VisibilityChanged")]
public class VisibilityChangedEventDTO : IEventDTO
{
    [Parameter("bool", "visible", 1, false)]
    public bool Visible { get; set; }
}

class Program
{
    static ConcurrentBag<WebSocket> clients = new ConcurrentBag<WebSocket>();

    static async Task StartHttpServer()
    {
        string port = Environment.GetEnvironmentVariable("PORT") ?? "5000";
        string url = $"http://0.0.0.0:{port}/";
        HttpListener listener = new HttpListener();
        listener.Prefixes.Add(url);
        listener.Prefixes.Add($"{url}ws/");
        listener.Start();
        Console.WriteLine($"🌐 Server listening on {url}");

        while (true)
        {
            var context = await listener.GetContextAsync();

            if (context.Request.IsWebSocketRequest)
            {
                var wsContext = await context.AcceptWebSocketAsync(null);
                clients.Add(wsContext.WebSocket);
                Console.WriteLine("🔌 Unity WebSocket connected");
            }
            else
            {
                // Root path handler for Render health check
                var buffer = Encoding.UTF8.GetBytes("🚀 MonaBackend is running!");
                context.Response.ContentType = "text/plain";
                context.Response.OutputStream.Write(buffer, 0, buffer.Length);
                context.Response.Close();
            }
        }
    }

    static async Task StartBlockchainListener()
    {
        var web3 = new Web3("https://sepolia.infura.io/v3/6ad85a144d0445a3b181add73f6a55d9");
        var contractAddress = "0x4F3AC69d127A8b0Ad3b9dFaBdc3A19DC3B34c240";
        var eventHandler = web3.Eth.GetEvent<VisibilityChangedEventDTO>(contractAddress);
        var filterAll = eventHandler.CreateFilterInput(BlockParameter.CreateLatest(), BlockParameter.CreateLatest());

        Console.WriteLine("🎧 Listening for VisibilityChanged events...");

        while (true)
        {
            var logs = await eventHandler.GetAllChangesAsync(filterAll);
            foreach (var ev in logs)
            {
                bool isVisible = ev.Event.Visible;
                Console.WriteLine($"[🔁 Blockchain] VisibilityChanged: {isVisible}");

                var json = JsonSerializer.Serialize(new { visible = isVisible });
                var bytes = Encoding.UTF8.GetBytes(json);

                foreach (var socket in clients)
                {
                    if (socket.State == WebSocketState.Open)
                    {
                        await socket.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, CancellationToken.None);
                    }
                }
            }

            await Task.Delay(5000);
        }
    }

    static async Task Main(string[] args)
    {
        await Task.WhenAll(StartHttpServer(), StartBlockchainListener());
    }
}
