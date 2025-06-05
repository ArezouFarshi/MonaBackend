using System.Net.WebSockets;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Hosting;
using Microsoft.AspNetCore.Http;
using System.Text;
using System.Text.Json;
using System.Collections.Concurrent;
using Nethereum.JsonRpc.WebSocketStreamingClient;
using Nethereum.RPC.Eth.DTOs;
using Nethereum.ABI.Model;
using Nethereum.ABI.FunctionEncoding;

var builder = WebApplication.CreateBuilder(args);
var app = builder.Build();

var clients = new ConcurrentBag<WebSocket>();

app.UseWebSockets();

app.Use(async (context, next) =>
{
    if (context.Request.Path == "/ws" && context.WebSockets.IsWebSocketRequest)
    {
        var ws = await context.WebSockets.AcceptWebSocketAsync();
        clients.Add(ws);
        Console.WriteLine("Client connected");

        var buffer = new byte[1024 * 4];
        while (ws.State == WebSocketState.Open)
        {
            var result = await ws.ReceiveAsync(new ArraySegment<byte>(buffer), CancellationToken.None);
            if (result.MessageType == WebSocketMessageType.Close)
            {
                await ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "Closed", CancellationToken.None);
                break;
            }
        }
    }
    else
    {
        await next();
    }
});

_ = Task.Run(async () =>
{
    var wsClient = new StreamingWebSocketClient("wss://sepolia.infura.io/ws/v3/51bc36040f314e85bf103ff18c570993");

    var ethLogs = new EthLogsObservableSubscription(wsClient);

    ethLogs.GetSubscriptionDataResponsesAsObservable().Subscribe(log =>
    {
        var abi = new EventABI("VisibilityChanged", false);
        abi.InputParameters = new[] { new Parameter("bool", "visible", 1, false) };

        var decoder = new EventTopicDecoder();
        var decoded = decoder.DecodeDefaultTopics(abi, log);

        if (decoded.Count > 0)
        {
            var visible = (bool)decoded[0].Result;
            Console.WriteLine($"[EVENT] Visibility: {visible}");

            var json = JsonSerializer.Serialize(new { visible });
            var bytes = Encoding.UTF8.GetBytes(json);

            foreach (var socket in clients)
            {
                if (socket.State == WebSocketState.Open)
                {
                    socket.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, CancellationToken.None);
                }
            }
        }
    });

    await wsClient.StartAsync();
    await ethLogs.SubscribeAsync(new NewFilterInput
    {
        Address = new[] { "0x4F3AC69d127A8b0Ad3b9dFaBdc3A19DC3B34c240" }
    });
});

app.Run("http://0.0.0.0:5000");
