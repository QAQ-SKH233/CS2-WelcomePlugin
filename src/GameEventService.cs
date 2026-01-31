using System.Reflection;
using System.Net.Http;
using System.Text.Json;
using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using SwiftlyS2.Shared;
using SwiftlyS2.Shared.Events;
using SwiftlyS2.Shared.GameEventDefinitions;
using SwiftlyS2.Shared.Misc;
using SwiftlyS2.Shared.Players;
using SwiftlyS2.Shared.ProtobufDefinitions;


public class GameEventService{

    private ISwiftlyCore Core { get; set; }
    private readonly ConcurrentDictionary<int, string> _playerLocation = new();

    private ConcurrentDictionary<IPlayer, bool> FirstJoin = new();

    private readonly IIpLocationService _ipLocationService;

    private readonly ILogger<GameEventService> _logger;
    private readonly HttpClient _httpClient = new()
    {
        Timeout = TimeSpan.FromSeconds(10)
    };

    private readonly ConcurrentQueue<(MessageType Kind, string Message)> _pendingMessages = new();

    private const string DailyQuoteApiUrl = "http://api.kekc.cn/api/wawr?encode=json";

    public GameEventService(ISwiftlyCore core, ILogger<GameEventService> logger, IIpLocationService ipLocationService){
        Core = core;
        _logger = logger;
        _logger.LogInformation("GameEventService loaded ");

        _ipLocationService = ipLocationService;
        core.Registrator.Register(this);

        Core.GameEvent.HookPre<EventPlayerSpawn>(OnPlayerSpawned);
        Core.Event.OnTick += OnTick;

    }

    private void OnTick()
    {
        while (_pendingMessages.TryDequeue(out var item))
        {
            Core.PlayerManager.SendMessage(item.Kind, item.Message);
        }
    }

    private async Task<string?> GetDailyQuoteAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            using var response = await _httpClient.GetAsync(DailyQuoteApiUrl, cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("每日一言接口请求失败，状态码: {StatusCode}", response.StatusCode);
                return null;
            }

            var json = await response.Content.ReadAsStringAsync(cancellationToken);

            using var document = JsonDocument.Parse(json);

            if (document.RootElement.TryGetProperty("text", out var textElement))
            {
                return textElement.GetString();
            }

            _logger.LogWarning("每日一言接口响应中未找到 text 字段");
            return null;
        }
        catch (TaskCanceledException)
        {
            _logger.LogWarning("请求每日一言接口超时");
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "获取每日一言时发生异常");
            return null;
        }
    }


    [EventListener<EventDelegates.OnClientConnected>]
    public void OnClientConnected(IOnClientConnectedEvent @event){
        var player = Core.PlayerManager.GetPlayer(@event.PlayerId);

        if (player != null)
        {

            Task.Run(async () =>
            {
                try
                {
                    var locationResult = await _ipLocationService.QueryLocationAsync(player.IPAddress, default);

                    if (locationResult.Success)
                    {
                        _playerLocation[@event.PlayerId] = locationResult.Location!;
                        _logger.LogInformation($"Got location: {locationResult.Location}");
                    }
                    else
                    {
                        // 处理错误情况
                        _logger.LogError($"Failed to get location: {locationResult.ErrorMessage}");
                    }
                }
                catch (Exception ex)
                {
                    // 异常处理
                    _logger.LogError($"Error getting location: {ex.Message}");
                }
            });

        }

    }

    public HookResult OnPlayerSpawned(EventPlayerSpawn @event){
        
        var player = Core.PlayerManager.GetPlayer(@event.UserId);
        if(player == null || !player.IsValid || player.IsFakeClient) return HookResult.Continue;
        if (!FirstJoin.TryGetValue(player, out var isFirstJoin) || isFirstJoin)
        {
            if (!_playerLocation.TryGetValue(player.PlayerID, out var location))
            {
                location = "未知地区";
            }

            Core.PlayerManager.SendMessage(MessageType.Chat, $"[green]欢迎玩家{player.Controller.PlayerName}加入FG抢先体验服务器[default]\nTA来自 [red]{location}");

            Task.Run(async () =>
            {
                try
                {
                    var quote = await GetDailyQuoteAsync(default);
                    if (!string.IsNullOrWhiteSpace(quote))
                    {
                        Core.PlayerManager.SendMessage(MessageType.Chat, $"[yellow]{quote}[default]");
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "获取每日一言时发生异常");
                }
            });

            FirstJoin[player] = false;
        }

        return HookResult.Continue;

    }


    [EventListener<EventDelegates.OnClientDisconnected>]
    public void OnClientDisconnected(IOnClientDisconnectedEvent @event){
        FirstJoin.Remove(Core.PlayerManager.GetPlayer(@event.PlayerId), out _);
    }

}
