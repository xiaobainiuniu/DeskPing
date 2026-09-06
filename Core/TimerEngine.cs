using System.Diagnostics;

namespace DeskPing.Core;

public enum TimerMode { CountUp, CountDown, TargetTime }

public enum RunState { Stopped, Running, Paused, Alerted }

/// <summary>
/// 计时核心：纯逻辑、无 UI 依赖。
/// 由单个 1Hz 节拍器驱动——每秒只做一次比较，空闲时近乎零 CPU 开销，
/// 这是整机"功耗最低"的关键设计。
/// </summary>
public sealed class TimerEngine : IDisposable
{
    private readonly System.Threading.Timer _ticker;
    private readonly Stopwatch _clock = new();

    private TimeSpan _elapsedBase; // 暂停时累计的已过时间
    private TimeSpan _elapsed;     // 当前周期已过时间

    public TimerEngine()
    {
        _ticker = new System.Threading.Timer(OnTick, null, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1));
    }

    /// <summary>每秒节拍（仅运行中触发，供 UI 刷新数字）。</summary>
    public event Action? Ticked;

    /// <summary>模式 / 运行状态 / 设置变化。</summary>
    public event Action? StateChanged;

    /// <summary>到点提醒。每个运行周期只触发一次。</summary>
    public event Action? Alarm;

    public TimerMode Mode { get; private set; } = TimerMode.CountDown;
    public RunState State { get; private set; } = RunState.Stopped;
    public bool SoundOn { get; set; } = true;

    /// <summary>倒计时时长。</summary>
    public TimeSpan Duration { get; private set; } = TimeSpan.FromMinutes(25);

    /// <summary>目标时刻（本地时间）。</summary>
    public DateTime Target { get; private set; } = DateTime.Today.AddHours(18);

    /// <summary>目标时刻是否每日重复。</summary>
    public bool DailyRepeat { get; set; }

    public TimeSpan Elapsed => _elapsed;

    /// <summary>当前应向用户展示的值：正计时=经过时间；其余=剩余时间。</summary>
    public TimeSpan DisplayValue => Mode switch
    {
        TimerMode.CountUp => _elapsed,
        TimerMode.CountDown => Duration - _elapsed,
        _ => Target - DateTime.Now,
    };

    public void SetMode(TimerMode mode)
    {
        if (Mode == mode) return;
        Mode = mode;
        Reset();
    }

    /// <summary>设置倒计时时长（会先停止当前计时）。</summary>
    public void SetCountdown(TimeSpan duration)
    {
        Duration = duration;
        if (State != RunState.Stopped) Reset();
        else StateChanged?.Invoke();
    }

    /// <summary>设置目标时刻（会先停止当前计时）。</summary>
    public void SetTarget(DateTime target, bool dailyRepeat)
    {
        Target = target;
        DailyRepeat = dailyRepeat;
        if (State != RunState.Stopped) Reset();
        else StateChanged?.Invoke();
    }

    public void Start()
    {
        if (State == RunState.Running) return;

        if (Mode == TimerMode.TargetTime)
        {
            // 目标已过则顺延到下一个匹配时刻。
            var now = DateTime.Now;
            while (Target <= now) Target = Target.AddDays(1);
        }

        _clock.Start();
        State = RunState.Running;
        StateChanged?.Invoke();
        Ticked?.Invoke();
    }

    public void Pause()
    {
        if (State != RunState.Running) return;
        _clock.Stop();
        _elapsedBase += _clock.Elapsed;
        _elapsed = _elapsedBase;
        State = RunState.Paused;
        StateChanged?.Invoke();
    }

    public void Reset()
    {
        _clock.Reset();
        _elapsedBase = TimeSpan.Zero;
        _elapsed = TimeSpan.Zero;
        State = RunState.Stopped;
        StateChanged?.Invoke();
        Ticked?.Invoke();
    }

    private void OnTick(object? _)
    {
        if (State != RunState.Running) return;

        _elapsed = _elapsedBase + _clock.Elapsed;

        if (Mode == TimerMode.CountDown && _elapsed >= Duration)
        {
            FireAlarm();
        }
        else if (Mode == TimerMode.TargetTime && DateTime.Now >= Target)
        {
            FireAlarm();
        }
        else
        {
            Ticked?.Invoke();
        }
    }

    private void FireAlarm()
    {
        if (Mode == TimerMode.TargetTime && DailyRepeat)
        {
            // 每日重复：提醒后自动顺延一天，继续运行，不打断用户工作流。
            Target = Target.AddDays(1);
            _elapsed = _elapsedBase + _clock.Elapsed;
        }
        else
        {
            _clock.Stop();
            _elapsed = _elapsedBase + _clock.Elapsed;
            State = RunState.Alerted;
            StateChanged?.Invoke();
        }
        Alarm?.Invoke();
    }

    public void Dispose() => _ticker.Dispose();
}
