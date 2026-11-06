using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace ShoujoKageki.Core.Telemetry;

/// <summary>Durable, bounded delivery; no game objects are accessed by the retry worker.</summary>
internal sealed class RunHistoryUploadQueue
{
    internal const int MaxItems = 64;
    internal const long MaxBytes = 64 * 1024 * 1024;
    internal const int MaxAttempts = 5;
    private readonly string _directory;
    private readonly Uri _endpoint;
    private readonly HttpClient _client;
    private readonly Func<bool> _enabled;
    private readonly Action<string> _log;
    private readonly Func<DateTimeOffset> _clock;
    private readonly Func<TimeSpan, Task> _delay;
    private readonly SemaphoreSlim _worker = new(1, 1);
    private readonly object _files = new();
    private readonly int _maxItems, _maxPayloadBytes;
    private readonly long _maxBytes;
    private readonly TimeSpan _requestInterval;
    private readonly bool _conflictIsSuccess;
    private DateTimeOffset _nextRequest;

    private sealed class Pending
    {
        public string Payload { get; set; } = "";
        public int Attempts { get; set; }
        public DateTimeOffset NextAttempt { get; set; }
    }

    internal static bool ShouldUploadCharacters(IEnumerable<string> characters) =>
        characters.Any(character => !string.IsNullOrWhiteSpace(character));

    internal RunHistoryUploadQueue(string root, Uri endpoint, HttpClient client, Func<bool> enabled,
        Action<string> log, Func<DateTimeOffset>? clock = null, Func<TimeSpan, Task>? delay = null,
        int maxItems = MaxItems, long maxBytes = MaxBytes, int maxPayloadBytes = 8 * 1024 * 1024,
        TimeSpan requestInterval = default, bool conflictIsSuccess = true)
    {
        _maxItems = maxItems; _maxBytes = maxBytes; _maxPayloadBytes = maxPayloadBytes;
        _requestInterval = requestInterval;
        _conflictIsSuccess = conflictIsSuccess;
        _endpoint = endpoint;
        _directory = Path.Combine(root, Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(endpoint.AbsoluteUri)))[..16]);
        _client = client;
        _enabled = enabled;
        _log = log;
        _clock = clock ?? (() => DateTimeOffset.UtcNow);
        _delay = delay ?? (duration => Task.Delay(duration));
        Directory.CreateDirectory(_directory);
        // An exhausted item is kept for another bounded attempt batch on the next game launch.
        foreach (string path in Directory.EnumerateFiles(_directory, "*.json"))
        {
            var pending = Read(path);
            if (pending != null && pending.Attempts >= MaxAttempts)
            {
                pending.Attempts = 0;
                Write(path, pending);
            }
        }
    }

    internal bool Enqueue(string payload)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(payload);
        if (bytes.Length > _maxPayloadBytes)
        {
            _log("[RunHistoryUploadPatch] 对局超过上传大小限制，保留游戏本地历史");
            return false;
        }
        string path = Path.Combine(_directory, Convert.ToHexString(SHA256.HashData(bytes)) + ".json");
        lock (_files)
        {
            if (File.Exists(path)) return true;
            var files = Directory.GetFiles(_directory);
            if (files.Length >= _maxItems || files.Sum(file => new FileInfo(file).Length) + bytes.Length * 2L + 1024 > _maxBytes)
            {
                _log("[RunHistoryUploadPatch] 待上传队列已满，保留现有队列和游戏本地历史");
                return false;
            }
            Write(path, new Pending { Payload = payload, NextAttempt = _clock() });
        }
        return true;
    }

    internal async Task FlushAsync()
    {
        await _worker.WaitAsync().ConfigureAwait(false);
        try
        {
            while (_enabled())
            {
                var items = Directory.GetFiles(_directory, "*.json").Select(path => (Path: path, Item: Read(path)))
                    .Where(entry => entry.Item != null && entry.Item.Attempts < MaxAttempts)
                    .OrderBy(entry => entry.Item!.NextAttempt).ToArray();
                if (items.Length == 0) return;
                var (path, pending) = items[0];
                TimeSpan wait = (pending!.NextAttempt > _nextRequest ? pending.NextAttempt : _nextRequest) - _clock();
                if (wait > TimeSpan.Zero)
                {
                    await _delay(wait < TimeSpan.FromSeconds(30) ? wait : TimeSpan.FromSeconds(30)).ConfigureAwait(false);
                    continue;
                }
                if (!_enabled()) return;
                _nextRequest = _clock() + _requestInterval;
                pending.Attempts++;
                // Persist before sending so a process exit cannot lose the original JSON.
                Write(path, pending);
                TimeSpan backoff = TimeSpan.FromSeconds(Math.Min(300, Math.Pow(2, pending.Attempts) * 2));
                try
                {
                    using var request = new HttpRequestMessage(HttpMethod.Post, _endpoint)
                    {
                        Content = new StringContent(pending.Payload, Encoding.UTF8, "application/json")
                    };
                    using var response = await _client.SendAsync(request).ConfigureAwait(false);
                    if (response.IsSuccessStatusCode || (_conflictIsSuccess && response.StatusCode == HttpStatusCode.Conflict))
                    {
                        File.Delete(path);
                        continue;
                    }
                    bool temporary = response.StatusCode is HttpStatusCode.RequestTimeout or HttpStatusCode.TooManyRequests ||
                                     (int)response.StatusCode >= 500;
                    if (!temporary)
                    {
                        File.Move(path, Path.ChangeExtension(path, ".rejected"), true);
                        _log($"[RunHistoryUploadPatch] 服务端拒绝上传（{(int)response.StatusCode}），本地队列记录已保留供排查");
                        continue;
                    }
                    TimeSpan? retryAfter = response.Headers.RetryAfter?.Delta;
                    if (response.Headers.RetryAfter?.Date is DateTimeOffset date) retryAfter = date - _clock();
                    if (retryAfter > backoff) backoff = retryAfter.Value;
                    _log($"[RunHistoryUploadPatch] 上传暂未成功（{(int)response.StatusCode}），已保留并安排重试");
                }
                catch (Exception error) when (error is HttpRequestException or TaskCanceledException)
                {
                    _log("[RunHistoryUploadPatch] 上传连接暂不可用，已保留并安排重试");
                }
                pending.NextAttempt = _clock() + (backoff > TimeSpan.FromHours(6) ? TimeSpan.FromHours(6) : backoff);
                if (_requestInterval > TimeSpan.Zero && pending.NextAttempt > _nextRequest)
                    _nextRequest = pending.NextAttempt;
                Write(path, pending);
            }
        }
        catch (Exception error)
        {
            _log($"[RunHistoryUploadPatch] 队列处理暂停，记录保留：{error.GetType().Name}");
        }
        finally
        {
            _worker.Release();
        }
    }

    private Pending? Read(string path)
    {
        lock (_files)
        {
            try
            {
                return JsonSerializer.Deserialize<Pending>(File.ReadAllText(path, Encoding.UTF8));
            }
            catch (Exception error) when (error is IOException or JsonException or UnauthorizedAccessException)
            {
                _log("[RunHistoryUploadPatch] 一项待上传记录暂不可读取，已保留");
                return null;
            }
        }
    }

    private void Write(string path, Pending pending)
    {
        lock (_files)
        {
            string temporary = path + ".tmp";
            try
            {
                using (var stream = new FileStream(temporary, FileMode.Create, FileAccess.Write, FileShare.None))
                {
                    JsonSerializer.Serialize(stream, pending);
                    stream.Flush(flushToDisk: true);
                }
                File.Move(temporary, path, true);
            }
            finally
            {
                if (File.Exists(temporary)) File.Delete(temporary);
            }
        }
    }
}
