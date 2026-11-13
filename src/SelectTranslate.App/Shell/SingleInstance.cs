using System.IO;
using System.IO.Pipes;

namespace SelectTranslate.App.Shell;

/// <summary>
/// 单实例：命名互斥体保证唯一，命名管道把"再次启动"转成"唤起设置"。
/// </summary>
public sealed class SingleInstance : IDisposable
{
    private const string MutexName = @"Local\SelectTranslate.SingleInstance";
    private const string PipeName = "SelectTranslate.Activate";

    private readonly Mutex _mutex;
    private readonly CancellationTokenSource _cts = new();

    private Task? _listenTask;
    private bool _disposed;

    /// <param name="waitForExistingToExit">
    /// 提权重启场景：旧进程还在收尾时，稍等一会儿再判定，避免新实例误判为"第二次启动"。
    /// </param>
    public SingleInstance(TimeSpan? waitForExistingToExit = null)
    {
        var deadline = DateTime.UtcNow + (waitForExistingToExit ?? TimeSpan.Zero);

        while (true)
        {
            _mutex = new Mutex(initiallyOwned: true, MutexName, out var createdNew);
            if (createdNew)
            {
                IsFirstInstance = true;
                return;
            }

            _mutex.Dispose();

            if (DateTime.UtcNow >= deadline)
            {
                _mutex = new Mutex(initiallyOwned: false, MutexName);
                IsFirstInstance = false;
                return;
            }

            Thread.Sleep(100);
        }
    }

    public bool IsFirstInstance { get; }

    public event Action? ActivationRequested;

    public void StartListening()
    {
        if (!IsFirstInstance)
        {
            return;
        }

        _listenTask = Task.Run(ListenLoopAsync);
    }

    private async Task ListenLoopAsync()
    {
        while (!_cts.IsCancellationRequested)
        {
            try
            {
                await using var server = new NamedPipeServerStream(
                    PipeName,
                    PipeDirection.In,
                    maxNumberOfServerInstances: 1,
                    PipeTransmissionMode.Byte,
                    PipeOptions.Asynchronous);

                await server.WaitForConnectionAsync(_cts.Token);

                using var reader = new StreamReader(server);
                await reader.ReadLineAsync(_cts.Token);

                ActivationRequested?.Invoke();
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (IOException)
            {
                // 客户端提前断开，继续等下一条连接。
            }
        }
    }

    /// <summary>由第二个实例调用：通知已运行实例唤起设置。</summary>
    public static void SignalExisting()
    {
        try
        {
            using var client = new NamedPipeClientStream(".", PipeName, PipeDirection.Out);
            client.Connect(1500);

            using var writer = new StreamWriter(client);
            writer.WriteLine("activate");
            writer.Flush();
        }
        catch (Exception)
        {
            // 唤醒失败（未开始监听、权限/完整性不同、管道被占用）都不该影响本次启动。
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _cts.Cancel();

        try
        {
            _listenTask?.Wait(TimeSpan.FromMilliseconds(500));
        }
        catch (AggregateException)
        {
            // 取消导致的异常忽略。
        }

        _mutex.Dispose();
        _cts.Dispose();
    }
}