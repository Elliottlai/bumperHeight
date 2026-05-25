using System.Collections.Concurrent;
using PLC_IO.Interfaces;

namespace PLC_IO.Services;

/// <summary>
/// �ШD/�^�Ьy�{��� �X �޲z�R�O��C�P���ߴ`��
/// </summary>
public sealed class RequestReplyController<T> : IDisposable
{
    private readonly ICommandHandler<T> _handler;
    private readonly ConcurrentQueue<T> _commandQueue = new();
    private readonly Thread _messageLoopThread;
    private readonly CancellationTokenSource _cts = new();

    private volatile bool _stopping;
    private int _cycleInterval = 5;
    private int _timeoutMs = 200;

    public event Action<T>? OnTimeout;
    public event Action<Exception>? OnError;

    public int CycleInterval
    {
        get => _cycleInterval;
        set => _cycleInterval = value;
    }

    public int TimeoutMs
    {
        get => _timeoutMs;
        set => _timeoutMs = value;
    }

    public int WaitingCommandCount => _commandQueue.Count;

    public RequestReplyController(ICommandHandler<T> handler)
    {
        _handler = handler;
        _messageLoopThread = new Thread(MessageLoop)
        {
            IsBackground = true,
            Name = $"RequestReply-{typeof(T).Name}"
        };
        _messageLoopThread.Start();
    }

    public void AddRequest(T command)
    {
        _commandQueue.Enqueue(command);
    }

    private void MessageLoop()
    {
        T? currentCommand = default;
        long commandSendTicks = 0;
        bool answerGot = true;

        while (!_stopping)
        {
            try
            {
                // 前一個命令已回覆，送出下一個
                if (_commandQueue.Count > 0 && answerGot)
                {
                    if (_commandQueue.TryDequeue(out T? commandToSend))
                    {
                        answerGot = false;
                        _handler.SendCommand(commandToSend);
                        Thread.Sleep(20);
                        currentCommand = commandToSend;
                        commandSendTicks = DateTime.Now.Ticks;
                        Thread.Sleep(30);
                    }
                }

                // 檢查回覆與逾時
                if (!answerGot && currentCommand is not null)
                {
                    answerGot = _handler.CheckIsCommandAnswered(currentCommand);
                    if (DateTime.Now.Ticks - commandSendTicks > _timeoutMs * TimeSpan.TicksPerMillisecond)
                    {
                        OnTimeout?.Invoke(currentCommand);
                        answerGot = true;
                    }
                }

                // 空閒時觸發輪詢讀取
                if (answerGot && _commandQueue.IsEmpty)
                {
                    _handler.IdleProcess();
                }
            }
            catch (Exception ex) when (!_stopping)
            {
                OnError?.Invoke(ex);
                answerGot = true;
            }

            Thread.Sleep(_cycleInterval);
        }
    }

    public void Dispose()
    {
        _stopping = true;
        _messageLoopThread.Join(1000);
        _cts.Cancel();
        _cts.Dispose();
    }
}