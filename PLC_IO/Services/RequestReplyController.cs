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

    // ── 優先佇列：給 IdleProcess()（ReadX/ReadY 等狀態輪詢）專用 ──
    // 背景：曾發生某個一般命令（例如 WriteY）持續逾時、不斷被重新排入 _commandQueue，
    // 因為迴圈只有在 _commandQueue 清空時才會呼叫 IdleProcess() 補新的讀取請求，
    // 導致寫入命令長時間卡住佇列，ReadX/ReadY 完全排不進去、送不出去——結果是
    // X12/X13（載台側別）、X7（光閘）、X3/X11（板子感測）這些依賴讀取的功能全部
    // 停止更新，卡在最後一次成功讀到的值，直到寫入問題自己恢復為止。
    // 用獨立佇列 + 每次「準備送下一個命令」前都優先補讀取，讓讀取不會被寫入的
    // 佇列擠壓拖垮：不管一般佇列（寫入等）現在堆了多少命令，讀取永遠有機會定期執行。
    private readonly ConcurrentQueue<T> _priorityQueue = new();

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

    public int WaitingCommandCount => _commandQueue.Count + _priorityQueue.Count;

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

    /// <summary>
    /// 高優先命令（目前只給 <see cref="ICommandHandler{T}.IdleProcess"/> 內部的狀態輪詢用）。
    /// 一般命令（例如寫入）堆積再多，這個佇列裡的命令都會優先送出，不會被卡住。
    /// </summary>
    public void AddPriorityRequest(T command)
    {
        _priorityQueue.Enqueue(command);
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
                if (answerGot)
                {
                    // 準備送下一個命令前，優先佇列若已空就先補新的讀取請求進去——
                    // 這一步刻意不看一般佇列（寫入等）還剩多少命令，讀取的補充頻率
                    // 只取決於「上一輪讀取有沒有處理完」，不受寫入積壓影響。
                    if (_priorityQueue.IsEmpty)
                        _handler.IdleProcess();

                    bool dequeued = _priorityQueue.TryDequeue(out T? commandToSend)
                        || _commandQueue.TryDequeue(out commandToSend);

                    if (dequeued)
                    {
                        answerGot = false;
                        _handler.SendCommand(commandToSend!);
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