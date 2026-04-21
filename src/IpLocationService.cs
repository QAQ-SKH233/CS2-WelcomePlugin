using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using SwiftlyS2.Shared;


public class IpLocationResponse
{
    [JsonPropertyName("status")]
    public string Status { get; set; } = string.Empty;

    [JsonPropertyName("data")]
    public List<IpLocationData> Data { get; set; } = new();
}

public class IpLocationData
{
    [JsonPropertyName("location")]
    public string Location { get; set; } = string.Empty;
}
public class IpLocationResult
{
    public bool Success { get; set; }
    public string? Location { get; set; }
    public string? ErrorMessage { get; set; }
    public string? IpAddress { get; set; }

    public static IpLocationResult CreateSuccess(string ipAddress, string location)
    {
        return new IpLocationResult
        {
            Success = true,
            IpAddress = ipAddress,
            Location = location
        };
    }

    public static IpLocationResult CreateError(string ipAddress, string errorMessage)
    {
        return new IpLocationResult
        {
            Success = false,
            IpAddress = ipAddress,
            ErrorMessage = errorMessage
        };
    }
}

public interface IIpLocationService
{
    Task<IpLocationResult> QueryLocationAsync(string ipAddress, CancellationToken cancellationToken = default);
}



public class IpLocationService : IIpLocationService, IDisposable
{

    private ISwiftlyCore Core { get; set; } = null!;
    public IpLocationService(ISwiftlyCore core, ILogger<IpLocationService> logger)
    {
        Core = core;

        logger.LogInformation("IpLocationService loaded ");


        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _httpClient = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(10)
        };

        core.Registrator.Register(this);

    }

    private readonly HttpClient _httpClient = new HttpClient();
    private readonly ILogger<IpLocationService> _logger = null!;
    private readonly string _apiUrl = "http://opendata.baidu.com/api.php?co=&resource_id=6006&oe=utf8&query={0}";
    private readonly Regex _ipRegex = new(@"^((25[0-5]|2[0-4][0-9]|[01]?[0-9][0-9]?)\.){3}(25[0-5]|2[0-4][0-9]|[01]?[0-9][0-9]?)$", RegexOptions.Compiled);


    /// <summary>
    /// 异步查询IP地理位置
    /// </summary>
    public async Task<IpLocationResult> QueryLocationAsync(string ipAddress, CancellationToken cancellationToken = default)
    {
        try
        {
            // 验证IP地址格式
            if (string.IsNullOrWhiteSpace(ipAddress))
            {
                return IpLocationResult.CreateError(ipAddress, "IP地址不能为空");
            }

            _logger.LogInformation("开始查询IP地址 {IpAddress} 的地理位置", ipAddress);

            // 构建API URL
            var requestUrl = string.Format(_apiUrl, ipAddress);

            // 发送HTTP请求
            var response = await _httpClient.GetAsync(requestUrl, cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                var errorMessage = $"API请求失败，状态码: {response.StatusCode}";
                _logger.LogWarning("IP地理位置查询失败: {ErrorMessage}, IP: {IpAddress}", errorMessage, ipAddress);
                return IpLocationResult.CreateError(ipAddress, errorMessage);
            }

            // 读取响应内容
            var jsonContent = await response.Content.ReadAsStringAsync(cancellationToken);

            if (string.IsNullOrWhiteSpace(jsonContent))
            {
                return IpLocationResult.CreateError(ipAddress, "API返回空响应");
            }

            // 解析JSON响应
            var locationResponse = JsonSerializer.Deserialize<IpLocationResponse>(jsonContent, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

            if (locationResponse == null)
            {
                return IpLocationResult.CreateError(ipAddress, "无法解析API响应");
            }

            // 检查API状态
            if (locationResponse.Status != "0")
            {
                return IpLocationResult.CreateError(ipAddress, $"API返回错误状态: {locationResponse.Status}");
            }

            // 提取位置信息
            if (locationResponse.Data?.Count > 0)
            {
                var locationData = locationResponse.Data[0];
                var location = locationData.Location;

                if (!string.IsNullOrWhiteSpace(location))
                {
                    _logger.LogInformation("成功查询到IP地址 {IpAddress} 的位置: {Location} , 从API返回", ipAddress, location, jsonContent);
                    return IpLocationResult.CreateSuccess(ipAddress, location);
                }
            }

            return IpLocationResult.CreateError(ipAddress, "未找到位置信息");
        }
        catch (TaskCanceledException ex) when (ex.InnerException is TimeoutException)
        {
            var errorMessage = "查询超时";
            _logger.LogWarning("IP地理位置查询超时: IP: {IpAddress}", ipAddress);
            return IpLocationResult.CreateError(ipAddress, errorMessage);
        }
        catch (TaskCanceledException)
        {
            var errorMessage = "查询被取消";
            _logger.LogWarning("IP地理位置查询被取消: IP: {IpAddress}", ipAddress);
            return IpLocationResult.CreateError(ipAddress, errorMessage);
        }
        catch (HttpRequestException ex)
        {
            var errorMessage = $"网络请求异常: {ex.Message}";
            _logger.LogError(ex, "IP地理位置查询网络异常: IP: {IpAddress}", ipAddress);
            return IpLocationResult.CreateError(ipAddress, errorMessage);
        }
        catch (JsonException ex)
        {
            var errorMessage = $"JSON解析异常: {ex.Message}";
            _logger.LogError(ex, "IP地理位置查询JSON解析异常: IP: {IpAddress}", ipAddress);
            return IpLocationResult.CreateError(ipAddress, errorMessage);
        }
        catch (Exception ex)
        {
            var errorMessage = $"未知异常: {ex.Message}";
            _logger.LogError(ex, "IP地理位置查询发生未知异常: IP: {IpAddress}", ipAddress);
            return IpLocationResult.CreateError(ipAddress, errorMessage);
        }
    }

    public void Dispose()
    {
        _httpClient?.Dispose();
    }
}


