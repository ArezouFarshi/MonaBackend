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

    static async Task StartHttpServer()
    {
        HttpListener listener = new HttpListener();

        listener.Prefixes.Add("http://0.0.0.0:5000/ws/");
        listener.Prefixes.Add("http://0.0.0.0:5000/");

        listener.Start();
        Console.WriteLine("✅ WebSocket server listening on http://0.0.0.0:5000/ws/");

        while (true)
        {
            var context = await listener.GetContextAsync();

            // WebSocket request
            if (context.Request.IsWebSocketRequest && context.Request.RawUrl == "/ws/")
            {
                var wsContext = await context.AcceptWebSocketAsync(null);
                Console.WriteLine("🌐 Unity client connected.");
                clients.Add(wsContext.WebSocket);
            }
            // Root path HTTP GET handler
            else if (context.Request.RawUrl == "/" && context.Request.HttpMethod == "GET")
            {
                var message = Encoding.UTF8.GetBytes("✅ Mona backend is running!");
                context.Response.ContentType = "text/plain";
                context.Response.ContentLength64 = message.Length;
                await context.Response.OutputStream.WriteAsync(message, 0, message.Length);
                context.Response.OutputStream.Close();
            }
            else
            {
                context.Response.StatusCode = 404;
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

        Console.WriteLine("👂 Listening for VisibilityChanged events...");

        while (true)
        {
            var logs = await eventHandler.GetAllChangesAsync(filterAll);
            foreach (var ev in logs)
            {
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

            await Task.Delay(5000);
        }
    }

    static async Task Main(string[] args)
    {
        await Task.WhenAll(StartHttpServer(), StartBlockchainListener());
    }
}
