using System.Text.Json;
using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using SwiftlyS2.Shared;
using SwiftlyS2.Shared.Events;
using SwiftlyS2.Shared.GameEventDefinitions;
using SwiftlyS2.Shared.Misc;
using SwiftlyS2.Shared.Players;

namespace FGWelcome;

public class GameEventService
{

    private ISwiftlyCore Core { get; set; }
    private readonly ConcurrentDictionary<int, string> _playerLocation = new();

    private ConcurrentDictionary<int, bool> FirstJoin = new();

    private readonly IIpLocationService _ipLocationService;

    private readonly ILogger<GameEventService> _logger;
    private readonly HttpClient _httpClient = new()
    {
        Timeout = TimeSpan.FromSeconds(10)
    };

    private readonly ConcurrentQueue<(MessageType Kind, string Message)> _pendingMessages = new();

    private const string DailyQuoteApiUrl = "http://api.kekc.cn/api/wawr?encode=json";

    private readonly Random _random = new();
    private DateTime _nextQuoteTime;
    private readonly int _minQuoteInterval = 120;
    private readonly int _maxQuoteInterval = 600;

    public GameEventService(ISwiftlyCore core, ILogger<GameEventService> logger, IIpLocationService ipLocationService){
        Core = core;
        _logger = logger;
        _logger.LogInformation("GameEventService loaded ");

        _ipLocationService = ipLocationService;
        core.Registrator.Register(this);

        Core.GameEvent.HookPre<EventPlayerTeam>(OnPlayerTeam);
        Core.Event.OnTick += OnTick;

        _nextQuoteTime = DateTime.UtcNow.AddSeconds(_random.Next(_minQuoteInterval, _maxQuoteInterval));
    }

    private void OnTick()
    {
        while (_pendingMessages.TryDequeue(out var item))
        {
            Core.PlayerManager.SendMessage(item.Kind, item.Message);
        }

        if (DateTime.UtcNow >= _nextQuoteTime)
        {
            _ = Task.Run(async () =>
            {
                try
                {
                    var quote = await GetDailyQuoteAsync(default);
                    if (!string.IsNullOrWhiteSpace(quote))
                    {
                        _pendingMessages.Enqueue((MessageType.Chat, $"[default]每日一言 - [olive]{quote}[default]"));
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "获取每日一言时发生异常");
                }
            });

            _nextQuoteTime = DateTime.UtcNow.AddSeconds(_random.Next(_minQuoteInterval, _maxQuoteInterval));
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
                    _logger.LogError(ex, "获取玩家位置时发生异常");
                }
            });

        }

    }

    public HookResult OnPlayerTeam(EventPlayerTeam @event){
        if (@event.Disconnect || @event.IsBot || @event.Silent) return HookResult.Continue;

        var player = @event.UserIdPlayer;
        if (player == null || !player.IsValid || player.IsFakeClient) return HookResult.Continue;

        if (@event.Team == 0 || @event.Team == 1) return HookResult.Continue;

        @event.DontBroadcast = true;

        if (FirstJoin.ContainsKey(player.PlayerID)) return HookResult.Continue;

        if (!_playerLocation.TryGetValue(player.PlayerID, out var location))
        {
            location = "未知地区";
        }

        _pendingMessages.Enqueue((MessageType.Chat, $"[[olive]FG社区] [green]{player.Controller.PlayerName} [default]加入房间[default] - 来自 [olive]{location}"));

        FirstJoin[player.PlayerID] = false;

        return HookResult.Continue;
    }


    [EventListener<EventDelegates.OnClientDisconnected>]
    public void OnClientDisconnected(IOnClientDisconnectedEvent @event){
        var player = Core.PlayerManager.GetPlayer(@event.PlayerId);
        if (player != null)
        {
            FirstJoin.Remove(player.PlayerID, out _);
            _playerLocation.Remove(player.PlayerID, out _);
        }
    }

}
