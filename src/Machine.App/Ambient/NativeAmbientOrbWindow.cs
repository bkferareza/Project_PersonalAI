using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Machine.App;

internal sealed class NativeAmbientOrbWindow : IDisposable
{
    private const int ExtendedStyle =
        0x00080000 | // WS_EX_LAYERED
        0x00000080 | // WS_EX_TOOLWINDOW
        0x00000008 | // WS_EX_TOPMOST
        0x08000000;  // WS_EX_NOACTIVATE
    private const uint WindowStyle = 0x80000000; // WS_POPUP
    private const int WindowLongUserData = -21;
    private const int WindowLongStyle = -16;
    private const int WindowLongExtendedStyle = -20;
    private const long WindowStyleCaption = 0x00C00000L;
    private const long WindowStyleThickFrame = 0x00040000L;
    private const long WindowExtendedStyleToolWindow = 0x00000080L;
    private const uint ShowNoActivate = 0x0010;
    private const uint ShowWindow = 0x0040;
    private const uint UpdateLayeredWindowAlpha = 0x00000002;
    private const uint MouseLeave = 0x0002;
    private const int WindowMessageNonClientCreate = 0x0081;
    private const int WindowMessageNonClientDestroy = 0x0082;
    private const int WindowMessageMouseMove = 0x0200;
    private const int WindowMessageMouseLeave = 0x02A3;
    private const int WindowMessageLeftButtonUp = 0x0202;
    private const int WindowMessageMouseActivate = 0x0021;
    private const int WindowMessageNonClientHitTest = 0x0084;
    private const int WindowMessageEraseBackground = 0x0014;
    private const int WindowMessageTimer = 0x0113;
    private const int WindowMessageSettingChange = 0x001A;
    private const int WindowMessageDisplayChange = 0x007E;
    private const int WindowMessageDpiChanged = 0x02E0;
    private const uint WindowMessageEvaluateFullscreen = 0x8042;
    private const uint AnimationTimerId = 1;
    private const uint FullscreenSafetyTimerId = 2;
    private const uint FullscreenEvaluationTimerId = 3;
    private const uint FullscreenSafetyIntervalMilliseconds = 1_500;
    private const uint FullscreenEventDelayMilliseconds = 120;
    private const uint EventSystemForeground = 0x0003;
    private const uint EventSystemMinimizeStart = 0x0016;
    private const uint EventSystemMinimizeEnd = 0x0017;
    private const uint EventObjectShow = 0x8002;
    private const uint EventObjectHide = 0x8003;
    private const uint EventObjectLocationChange = 0x800B;
    private const uint WinEventOutOfContext = 0x0000;
    private const uint WinEventSkipOwnProcess = 0x0002;
    private const int DwmExtendedFrameBounds = 9;
    private const int DwmCloaked = 14;
    private const uint MonitorDefaultToNearest = 2;
    private const uint GetWindowOwner = 4;
    private const int HitTestTransparent = -1;
    private const int HitTestClient = 1;
    private const int MouseActivateNoActivate = 3;
    private const byte AlphaFormatSourceAlpha = 1;
    private const byte BlendOperationSourceOver = 0;
    private const uint DibRgbColors = 0;
    private const uint BitmapCompressionRgb = 0;
    private const int ClassAlreadyExists = 1410;
    private const string WindowClassName = "Machine.NativeAmbientOrb";
    private static readonly object WindowClassLock = new();
    private static readonly WindowProcedure WindowProcedureDelegate =
        DispatchWindowProcedure;
    private static ushort _windowClassAtom;
    private readonly AmbientOrbLifecycle _lifecycle = new();
    private readonly Action _onOrbClicked;
    private readonly WinEventProcedure _winEventProcedure;
    private readonly byte[] _renderPixels = new byte[
        AmbientOrbFrameSequence.CanvasSize *
        AmbientOrbFrameSequence.CanvasSize * 4];
    private readonly AmbientOrbFrame _renderedFrame;
    private readonly long _phaseOriginTimestamp = Stopwatch.GetTimestamp();
    private CompactPresenceVisualState _visualState = new(
        CompactPresenceVisualMode.Stable,
        IsGenerating: false,
        HasNewUnseenInsight: false);
    private AmbientOrbBlendState _blendState =
        AmbientOrbTransitionModel.CreateTarget(
            new(
                CompactPresenceVisualMode.Stable,
                IsGenerating: false,
                HasNewUnseenInsight: false),
            isHovered: false);
    private IntPtr _windowHandle;
    private IntPtr _deviceContext;
    private IntPtr _bitmap;
    private IntPtr _previousBitmap;
    private IntPtr _pixelBuffer;
    private GCHandle _selfHandle;
    private UIntPtr _animationTimerHandle;
    private UIntPtr _fullscreenSafetyTimerHandle;
    private UIntPtr _fullscreenEvaluationTimerHandle;
    private IntPtr _foregroundEventHook;
    private IntPtr _minimizeEventHook;
    private IntPtr _visibilityEventHook;
    private IntPtr _locationEventHook;
    private AmbientFullscreenSuppressionState _fullscreenState;
    private AmbientForegroundClassification _lastForegroundClassification;
    private long? _lastRenderedTimestamp;
    private long? _wakeStartedTimestamp;
    private int _frameIndex;
    private int _requestedX;
    private int _requestedY;
    private bool _isHovered;
    private bool _isPresentationRequested;
    private bool _wakePending;

    public NativeAmbientOrbWindow(Action onOrbClicked)
    {
        _onOrbClicked = onOrbClicked ?? throw new ArgumentNullException(
            nameof(onOrbClicked));
        _winEventProcedure = OnWinEvent;
        _renderedFrame = new AmbientOrbFrame(
            AmbientOrbFrameSequence.CanvasSize,
            AmbientOrbFrameSequence.CanvasSize,
            _renderPixels);
    }

    public bool IsVisible => _lifecycle.IsVisible;

    public bool IsDisposed => _lifecycle.IsDisposed;

    public bool IsFullscreenSuppressed => _fullscreenState.IsSuppressed;

    public AmbientForegroundClassification LastForegroundClassification =>
        _lastForegroundClassification;

    public TimeSpan FrameInterval => TimeSpan.FromSeconds(
        1d / AmbientOrbFrameSequence.FramesPerSecond);

    public CompactPresenceVisualMode VisualMode => _visualState.Mode;

    public CompactPresenceVisualState VisualState => _visualState;

    public bool ShouldAnimate => _lifecycle.ShouldAnimate &&
        AmbientFullscreenPresentationPolicy.ShouldRasterize(
            _isPresentationRequested,
            _fullscreenState.IsSuppressed,
            _lifecycle.AnimationsEnabled);

    public bool IsAnimationTimerRunning =>
        _animationTimerHandle != UIntPtr.Zero && _lifecycle.IsTimerRunning;

    public int FrameIndex => _frameIndex;

    public long PresentedFrameCount { get; private set; }

    public DateTimeOffset? LastFramePresentedAt { get; private set; }

    public event EventHandler? NewInsightCompleted;

    public void SetVisualMode(CompactPresenceVisualMode mode)
    {
        SetVisualState(mode switch
        {
            CompactPresenceVisualMode.NewInsight => new(
                _visualState.PostureMode,
                _visualState.IsGenerating,
                HasNewUnseenInsight: true),
            CompactPresenceVisualMode.Generating => new(
                _visualState.PostureMode,
                IsGenerating: true,
                HasNewUnseenInsight: false),
            _ => new(
                mode,
                IsGenerating: false,
                HasNewUnseenInsight: false)
        });
    }

    public void SetVisualState(CompactPresenceVisualState state)
    {
        ObjectDisposedException.ThrowIf(IsDisposed, this);
        if (_visualState == state)
        {
            return;
        }

        var newlyUnseen = state.HasNewUnseenInsight &&
            !_visualState.HasNewUnseenInsight;
        _visualState = state;
        if (!state.HasNewUnseenInsight)
        {
            _wakePending = false;
        }
        else if (newlyUnseen && _lifecycle.AnimationsEnabled)
        {
            if (IsVisible)
            {
                _wakeStartedTimestamp = Stopwatch.GetTimestamp();
            }
            else
            {
                _wakePending = true;
            }
        }

        if (IsVisible)
        {
            PresentCurrentFrame();
        }
    }

    public void SetAnimationsEnabled(bool enabled)
    {
        ObjectDisposedException.ThrowIf(IsDisposed, this);
        if (_lifecycle.AnimationsEnabled == enabled)
        {
            return;
        }

        if (!enabled)
        {
            _wakeStartedTimestamp = null;
            _wakePending = false;
        }

        var timerTransition = _lifecycle.SetAnimationsEnabled(enabled);
        ApplyTimerTransition(timerTransition);
        if (IsVisible)
        {
            PresentCurrentFrame();
        }
    }

    public void Show(int x, int y)
    {
        ObjectDisposedException.ThrowIf(IsDisposed, this);
        EnsureWindow();
        _isPresentationRequested = true;
        _requestedX = x;
        _requestedY = y;
        SetWindowPos(
            _windowHandle,
            new IntPtr(-1),
            _requestedX,
            _requestedY,
            AmbientOrbFrameSequence.CanvasSize,
            AmbientOrbFrameSequence.CanvasSize,
            ShowNoActivate);
        EvaluateFullscreenSuppression();
        if (!AmbientFullscreenPresentationPolicy.ShouldPresent(
                _isPresentationRequested,
                _fullscreenState.IsSuppressed))
        {
            HideSurface();
            return;
        }

        ShowRequestedSurface();
    }

    private void ShowRequestedSurface()
    {
        var timerTransition = _lifecycle.ShowWithTimerTransition();
        if (_wakePending && _lifecycle.AnimationsEnabled &&
            _visualState.HasNewUnseenInsight)
        {
            _wakePending = false;
            _wakeStartedTimestamp = Stopwatch.GetTimestamp();
        }
        SetWindowPos(
            _windowHandle,
            new IntPtr(-1),
            _requestedX,
            _requestedY,
            AmbientOrbFrameSequence.CanvasSize,
            AmbientOrbFrameSequence.CanvasSize,
            ShowNoActivate | ShowWindow);
        PresentCurrentFrame();
        ApplyTimerTransition(timerTransition);
    }

    public void Hide()
    {
        _isPresentationRequested = false;
        HideSurface();
    }

    private void HideSurface()
    {
        if (IsDisposed || _windowHandle == IntPtr.Zero)
        {
            return;
        }

        ApplyTimerTransition(_lifecycle.Hide());
        if (_wakeStartedTimestamp is not null &&
            _visualState.HasNewUnseenInsight)
        {
            _wakeStartedTimestamp = null;
            _wakePending = true;
        }
        ShowNativeWindow(_windowHandle, 0);
    }

    public bool AdvanceFrame()
    {
        if (!ShouldAnimate || IsDisposed)
        {
            return false;
        }

        PresentCurrentFrame();
        return ShouldAnimate;
    }

    public void Dispose()
    {
        if (IsDisposed)
        {
            return;
        }

        ApplyTimerTransition(_lifecycle.Hide());
        StopAnimationTimer();
        StopFullscreenMonitoring();
        _lifecycle.Dispose();

        if (_windowHandle != IntPtr.Zero)
        {
            DestroyWindow(_windowHandle);
            _windowHandle = IntPtr.Zero;
        }

        if (_deviceContext != IntPtr.Zero)
        {
            if (_previousBitmap != IntPtr.Zero)
            {
                SelectObject(_deviceContext, _previousBitmap);
                _previousBitmap = IntPtr.Zero;
            }

            DeleteDC(_deviceContext);
            _deviceContext = IntPtr.Zero;
        }

        if (_bitmap != IntPtr.Zero)
        {
            DeleteObject(_bitmap);
            _bitmap = IntPtr.Zero;
        }

        if (_selfHandle.IsAllocated)
        {
            _selfHandle.Free();
        }
    }

    private void EnsureWindow()
    {
        if (_windowHandle != IntPtr.Zero)
        {
            return;
        }

        EnsureWindowClass();
        _selfHandle = GCHandle.Alloc(this);
        _windowHandle = CreateWindowEx(
            ExtendedStyle,
            WindowClassName,
            string.Empty,
            WindowStyle,
            0,
            0,
            AmbientOrbFrameSequence.CanvasSize,
            AmbientOrbFrameSequence.CanvasSize,
            IntPtr.Zero,
            IntPtr.Zero,
            GetModuleHandle(null),
            GCHandle.ToIntPtr(_selfHandle));
        if (_windowHandle == IntPtr.Zero)
        {
            _selfHandle.Free();
            throw new InvalidOperationException(
                "The transparent ambient-orb window could not be created.");
        }

        var bitmapInfo = new BitmapInfo
        {
            Header = new BitmapInfoHeader
            {
                Size = (uint)Marshal.SizeOf<BitmapInfoHeader>(),
                Width = AmbientOrbFrameSequence.CanvasSize,
                Height = -AmbientOrbFrameSequence.CanvasSize,
                Planes = 1,
                BitCount = 32,
                Compression = BitmapCompressionRgb
            }
        };
        _deviceContext = CreateCompatibleDC(IntPtr.Zero);
        _bitmap = CreateDIBSection(
            _deviceContext,
            ref bitmapInfo,
            DibRgbColors,
            out _pixelBuffer,
            IntPtr.Zero,
            0);
        if (_deviceContext == IntPtr.Zero || _bitmap == IntPtr.Zero ||
            _pixelBuffer == IntPtr.Zero)
        {
            Dispose();
            throw new InvalidOperationException(
                "The transparent ambient-orb surface could not be created.");
        }

        _previousBitmap = SelectObject(_deviceContext, _bitmap);
        StartFullscreenMonitoring();
    }

    private void PresentCurrentFrame()
    {
        var now = Stopwatch.GetTimestamp();
        var cycleProgress = GetCycleProgress(now);
        var insightProgress = GetInsightProgress(now, out var wakeCompleted);
        if (wakeCompleted)
        {
            _wakeStartedTimestamp = null;
        }

        var targetBlend = AmbientOrbTransitionModel.CreateTarget(
            _visualState,
            _isHovered);
        _blendState = _lastRenderedTimestamp is { } previousTimestamp
            ? AmbientOrbTransitionModel.Advance(
                _blendState,
                targetBlend,
                Stopwatch.GetElapsedTime(previousTimestamp, now),
                _lifecycle.AnimationsEnabled)
            : targetBlend;
        _lastRenderedTimestamp = now;

        _frameIndex = Math.Min(
            AmbientOrbFrameSequence.FrameCount - 1,
            (int)Math.Floor(
                cycleProgress * AmbientOrbFrameSequence.FrameCount));
        var modifier = _wakeStartedTimestamp is not null
            ? AmbientOrbInsightModifier.Wake
            : _visualState.HasNewUnseenInsight
                ? AmbientOrbInsightModifier.UnseenCue
                : AmbientOrbInsightModifier.None;
        AmbientOrbProceduralRenderer.Render(
            _renderPixels,
            _visualState.PostureMode,
            cycleProgress,
            0d,
            modifier,
            insightProgress,
            !_lifecycle.AnimationsEnabled,
            _blendState,
            GetSlowDriftProgress(now));
        Marshal.Copy(
            _renderPixels,
            0,
            _pixelBuffer,
            _renderPixels.Length);

        GetWindowRect(_windowHandle, out var windowRect);
        var destination = new Point(windowRect.Left, windowRect.Top);
        var source = new Point(0, 0);
        var size = new Size(_renderedFrame.Width, _renderedFrame.Height);
        var blend = new BlendFunction
        {
            BlendOperation = BlendOperationSourceOver,
            SourceConstantAlpha = 255,
            AlphaFormat = AlphaFormatSourceAlpha
        };
        if (UpdateLayeredWindow(
            _windowHandle,
            IntPtr.Zero,
            ref destination,
            ref size,
            _deviceContext,
            ref source,
            0,
            ref blend,
            UpdateLayeredWindowAlpha))
        {
            PresentedFrameCount++;
            LastFramePresentedAt = DateTimeOffset.UtcNow;
        }

        if (wakeCompleted)
        {
            NewInsightCompleted?.Invoke(this, EventArgs.Empty);
        }
    }

    private double GetCycleProgress(long timestamp)
    {
        var elapsed = Stopwatch.GetElapsedTime(
            _phaseOriginTimestamp,
            timestamp);
        return elapsed.TotalSeconds /
            AmbientOrbMotionModel.StableCycleDuration.TotalSeconds % 1d;
    }

    private double GetSlowDriftProgress(long timestamp)
    {
        var elapsed = Stopwatch.GetElapsedTime(
            _phaseOriginTimestamp,
            timestamp);
        return elapsed.TotalSeconds /
            AmbientOrbMotionModel.SlowDriftDuration.TotalSeconds % 1d;
    }

    private double GetInsightProgress(
        long timestamp,
        out bool wakeCompleted)
    {
        wakeCompleted = false;
        if (_wakeStartedTimestamp is not { } started)
        {
            return 0d;
        }

        var wakeDuration = TimeSpan.FromSeconds(
            AmbientOrbFrameSequence.WakeFrameCount /
            (double)AmbientOrbFrameSequence.FramesPerSecond);
        var progress = Stopwatch.GetElapsedTime(started, timestamp) /
            wakeDuration;
        if (progress < 1d)
        {
            return Math.Max(0d, progress);
        }

        wakeCompleted = true;
        return 1d;
    }

    private void StartFullscreenMonitoring()
    {
        if (_windowHandle == IntPtr.Zero ||
            _foregroundEventHook != IntPtr.Zero)
        {
            return;
        }

        var flags = WinEventOutOfContext | WinEventSkipOwnProcess;
        _foregroundEventHook = SetWinEventHook(
            EventSystemForeground,
            EventSystemForeground,
            IntPtr.Zero,
            _winEventProcedure,
            0,
            0,
            flags);
        _minimizeEventHook = SetWinEventHook(
            EventSystemMinimizeStart,
            EventSystemMinimizeEnd,
            IntPtr.Zero,
            _winEventProcedure,
            0,
            0,
            flags);
        _visibilityEventHook = SetWinEventHook(
            EventObjectShow,
            EventObjectHide,
            IntPtr.Zero,
            _winEventProcedure,
            0,
            0,
            flags);
        _locationEventHook = SetWinEventHook(
            EventObjectLocationChange,
            EventObjectLocationChange,
            IntPtr.Zero,
            _winEventProcedure,
            0,
            0,
            flags);
        _fullscreenSafetyTimerHandle = SetTimer(
            _windowHandle,
            new UIntPtr(FullscreenSafetyTimerId),
            FullscreenSafetyIntervalMilliseconds,
            IntPtr.Zero);
    }

    private void StopFullscreenMonitoring()
    {
        if (_fullscreenEvaluationTimerHandle != UIntPtr.Zero)
        {
            KillTimer(_windowHandle, _fullscreenEvaluationTimerHandle);
            _fullscreenEvaluationTimerHandle = UIntPtr.Zero;
        }
        if (_fullscreenSafetyTimerHandle != UIntPtr.Zero)
        {
            KillTimer(_windowHandle, _fullscreenSafetyTimerHandle);
            _fullscreenSafetyTimerHandle = UIntPtr.Zero;
        }

        Unhook(ref _foregroundEventHook);
        Unhook(ref _minimizeEventHook);
        Unhook(ref _visibilityEventHook);
        Unhook(ref _locationEventHook);

        static void Unhook(ref IntPtr hook)
        {
            if (hook == IntPtr.Zero)
            {
                return;
            }

            UnhookWinEvent(hook);
            hook = IntPtr.Zero;
        }
    }

    private void OnWinEvent(
        IntPtr hook,
        uint eventType,
        IntPtr windowHandle,
        int objectId,
        int childId,
        uint eventThread,
        uint eventTime)
    {
        if (eventType >= EventObjectShow &&
            (objectId != 0 || childId != 0))
        {
            return;
        }

        if (_windowHandle != IntPtr.Zero && !IsDisposed)
        {
            PostMessage(
                _windowHandle,
                WindowMessageEvaluateFullscreen,
                IntPtr.Zero,
                IntPtr.Zero);
        }
    }

    private void ScheduleFullscreenEvaluation(uint delayMilliseconds)
    {
        if (_windowHandle == IntPtr.Zero || IsDisposed ||
            _fullscreenEvaluationTimerHandle != UIntPtr.Zero)
        {
            return;
        }

        _fullscreenEvaluationTimerHandle = SetTimer(
            _windowHandle,
            new UIntPtr(FullscreenEvaluationTimerId),
            Math.Max(1u, delayMilliseconds),
            IntPtr.Zero);
    }

    private void EvaluateFullscreenSuppression()
    {
        if (_windowHandle == IntPtr.Zero || IsDisposed)
        {
            return;
        }

        _lastForegroundClassification =
            CaptureForegroundClassification();
        var wasSuppressed = _fullscreenState.IsSuppressed;
        var decision = AmbientFullscreenSuppressionPolicy.Evaluate(
            _fullscreenState,
            _lastForegroundClassification,
            DateTimeOffset.UnixEpoch + Stopwatch.GetElapsedTime(
                _phaseOriginTimestamp,
                Stopwatch.GetTimestamp()));
        _fullscreenState = decision.State;

        if (_fullscreenState.IsSuppressed != wasSuppressed)
        {
            if (!AmbientFullscreenPresentationPolicy.ShouldPresent(
                    _isPresentationRequested,
                    _fullscreenState.IsSuppressed))
            {
                HideSurface();
            }
            else
            {
                ShowRequestedSurface();
            }
        }

        if (decision.RecheckAfter is { } delay)
        {
            ScheduleFullscreenEvaluation((uint)Math.Clamp(
                Math.Ceiling(delay.TotalMilliseconds),
                1d,
                uint.MaxValue));
        }
    }

    private AmbientForegroundClassification
        CaptureForegroundClassification()
    {
        var foreground = GetForegroundWindow();
        if (foreground == IntPtr.Zero)
        {
            return AmbientForegroundClassification.Ignored;
        }

        var orbBounds = new Rect
        {
            Left = _requestedX,
            Top = _requestedY,
            Right = _requestedX + AmbientOrbFrameSequence.CanvasSize,
            Bottom = _requestedY + AmbientOrbFrameSequence.CanvasSize
        };
        var orbMonitor = MonitorFromRect(
            ref orbBounds,
            MonitorDefaultToNearest);
        var foregroundMonitor = MonitorFromWindow(
            foreground,
            MonitorDefaultToNearest);
        if (orbMonitor == IntPtr.Zero || foregroundMonitor == IntPtr.Zero)
        {
            return AmbientForegroundClassification.Ignored;
        }

        var monitorInfo = new MonitorInfo
        {
            Size = (uint)Marshal.SizeOf<MonitorInfo>()
        };
        if (!GetMonitorInfo(foregroundMonitor, ref monitorInfo))
        {
            return AmbientForegroundClassification.Ignored;
        }

        var windowBounds = default(Rect);
        if (DwmGetWindowAttribute(
                foreground,
                DwmExtendedFrameBounds,
                out windowBounds,
                Marshal.SizeOf<Rect>()) != 0 ||
            windowBounds.Right <= windowBounds.Left ||
            windowBounds.Bottom <= windowBounds.Top)
        {
            if (!GetWindowRect(foreground, out windowBounds))
            {
                return AmbientForegroundClassification.Ignored;
            }
        }

        var cloaked = 0u;
        _ = DwmGetWindowAttribute(
            foreground,
            DwmCloaked,
            out cloaked,
            sizeof(uint));
        _ = GetWindowThreadProcessId(foreground, out var processId);
        var style = GetWindowLongValue(foreground, WindowLongStyle);
        var extendedStyle = GetWindowLongValue(
            foreground,
            WindowLongExtendedStyle);
        var snapshot = new AmbientForegroundWindowSnapshot(
            foreground.ToInt64(),
            foregroundMonitor.ToInt64(),
            IsWindowVisible(foreground),
            cloaked != 0,
            processId == (uint)Environment.ProcessId,
            (extendedStyle & WindowExtendedStyleToolWindow) != 0,
            GetWindow(foreground, GetWindowOwner) != IntPtr.Zero,
            foreground == GetShellWindow() ||
                foreground == GetDesktopWindow(),
            IsZoomed(foreground),
            (style & WindowStyleCaption) != 0,
            (style & WindowStyleThickFrame) != 0,
            ToBounds(windowBounds),
            ToBounds(monitorInfo.Monitor),
            ToBounds(monitorInfo.WorkArea));
        return AmbientFullscreenClassifier.Classify(
            snapshot,
            orbMonitor.ToInt64());
    }

    private void ClampRequestedBoundsToTopology()
    {
        if (_windowHandle == IntPtr.Zero)
        {
            return;
        }

        var requestedBounds = new Rect
        {
            Left = _requestedX,
            Top = _requestedY,
            Right = _requestedX + AmbientOrbFrameSequence.CanvasSize,
            Bottom = _requestedY + AmbientOrbFrameSequence.CanvasSize
        };
        var monitor = MonitorFromRect(
            ref requestedBounds,
            MonitorDefaultToNearest);
        var monitorInfo = new MonitorInfo
        {
            Size = (uint)Marshal.SizeOf<MonitorInfo>()
        };
        if (monitor == IntPtr.Zero ||
            !GetMonitorInfo(monitor, ref monitorInfo))
        {
            return;
        }

        var maximumX = Math.Max(
            monitorInfo.WorkArea.Left,
            monitorInfo.WorkArea.Right -
                AmbientOrbFrameSequence.CanvasSize);
        var maximumY = Math.Max(
            monitorInfo.WorkArea.Top,
            monitorInfo.WorkArea.Bottom -
                AmbientOrbFrameSequence.CanvasSize);
        _requestedX = Math.Clamp(
            _requestedX,
            monitorInfo.WorkArea.Left,
            maximumX);
        _requestedY = Math.Clamp(
            _requestedY,
            monitorInfo.WorkArea.Top,
            maximumY);
        SetWindowPos(
            _windowHandle,
            new IntPtr(-1),
            _requestedX,
            _requestedY,
            AmbientOrbFrameSequence.CanvasSize,
            AmbientOrbFrameSequence.CanvasSize,
            ShowNoActivate);
    }

    private static AmbientScreenBounds ToBounds(Rect rect) => new(
        rect.Left,
        rect.Top,
        rect.Right,
        rect.Bottom);

    private static long GetWindowLongValue(IntPtr handle, int index) =>
        IntPtr.Size == 8
            ? GetWindowLongPtr64(handle, index).ToInt64()
            : GetWindowLong32(handle, index);

    private void ApplyTimerTransition(
        AmbientOrbTimerTransition transition)
    {
        switch (transition)
        {
            case AmbientOrbTimerTransition.Start:
                StartAnimationTimer();
                break;
            case AmbientOrbTimerTransition.Stop:
                StopAnimationTimer();
                break;
        }
    }

    private void StartAnimationTimer()
    {
        if (_animationTimerHandle != UIntPtr.Zero ||
            _windowHandle == IntPtr.Zero ||
            !ShouldAnimate)
        {
            return;
        }

        var intervalMilliseconds = Math.Max(
            1u,
            (uint)Math.Round(FrameInterval.TotalMilliseconds));
        _animationTimerHandle = SetTimer(
            _windowHandle,
            new UIntPtr(AnimationTimerId),
            intervalMilliseconds,
            IntPtr.Zero);
        if (_animationTimerHandle == UIntPtr.Zero)
        {
            _lifecycle.MarkTimerStartFailed();
        }
    }

    private void StopAnimationTimer()
    {
        if (_animationTimerHandle == UIntPtr.Zero)
        {
            return;
        }

        KillTimer(_windowHandle, _animationTimerHandle);
        _animationTimerHandle = UIntPtr.Zero;
    }

    private void SetHovered(bool isHovered)
    {
        if (_isHovered == isHovered)
        {
            return;
        }

        _isHovered = isHovered;
        PresentCurrentFrame();
    }

    private IntPtr ProcessWindowMessage(
        IntPtr handle,
        uint message,
        IntPtr wParam,
        IntPtr lParam)
    {
        switch (message)
        {
            case WindowMessageNonClientHitTest:
                return IsVisibleOrbPixel(handle, lParam)
                    ? new IntPtr(HitTestClient)
                    : new IntPtr(HitTestTransparent);
            case WindowMessageMouseMove:
                SetHovered(true);
                var trackingInfo = new TrackMouseEventInfo
                {
                    Size = (uint)Marshal.SizeOf<TrackMouseEventInfo>(),
                    Flags = MouseLeave,
                    WindowHandle = handle
                };
                TrackMouseEvent(ref trackingInfo);
                break;
            case WindowMessageMouseLeave:
                SetHovered(false);
                break;
            case WindowMessageLeftButtonUp:
                if (IsRenderedPixelVisible(
                    SignedLowWord(lParam),
                    SignedHighWord(lParam)))
                {
                    _onOrbClicked();
                }

                return IntPtr.Zero;
            case WindowMessageMouseActivate:
                return new IntPtr(MouseActivateNoActivate);
            case WindowMessageEraseBackground:
                return new IntPtr(1);
            case WindowMessageEvaluateFullscreen:
                ScheduleFullscreenEvaluation(
                    FullscreenEventDelayMilliseconds);
                return IntPtr.Zero;
            case WindowMessageSettingChange:
            case WindowMessageDisplayChange:
            case WindowMessageDpiChanged:
                ClampRequestedBoundsToTopology();
                ScheduleFullscreenEvaluation(1);
                break;
            case WindowMessageTimer:
                var timerIdentity = unchecked((ulong)wParam.ToInt64());
                if (timerIdentity == _animationTimerHandle.ToUInt64())
                {
                    AdvanceFrame();
                    return IntPtr.Zero;
                }
                if (timerIdentity ==
                    _fullscreenEvaluationTimerHandle.ToUInt64())
                {
                    KillTimer(
                        _windowHandle,
                        _fullscreenEvaluationTimerHandle);
                    _fullscreenEvaluationTimerHandle = UIntPtr.Zero;
                    EvaluateFullscreenSuppression();
                    return IntPtr.Zero;
                }
                if (timerIdentity ==
                    _fullscreenSafetyTimerHandle.ToUInt64())
                {
                    EvaluateFullscreenSuppression();
                    return IntPtr.Zero;
                }
                break;
            case WindowMessageNonClientDestroy:
                StopAnimationTimer();
                StopFullscreenMonitoring();
                SetWindowData(handle, IntPtr.Zero);
                break;
        }

        return DefWindowProc(handle, message, wParam, lParam);
    }

    private bool IsVisibleOrbPixel(IntPtr handle, IntPtr screenPoint)
    {
        GetWindowRect(handle, out var rect);
        var point = new Point(
            SignedLowWord(screenPoint) - rect.Left,
            SignedHighWord(screenPoint) - rect.Top);
        return IsRenderedPixelVisible(point.X, point.Y);
    }

    private bool IsRenderedPixelVisible(int x, int y) =>
        x >= 0 && y >= 0 &&
        x < _renderedFrame.Width && y < _renderedFrame.Height &&
        _renderedFrame.GetAlpha(x, y) >=
            AmbientOrbFrameSequence.HitTestAlphaThreshold;

    private static void EnsureWindowClass()
    {
        lock (WindowClassLock)
        {
            if (_windowClassAtom != 0)
            {
                return;
            }

            var windowClass = new WindowClass
            {
                Size = (uint)Marshal.SizeOf<WindowClass>(),
                WindowProcedure = Marshal.GetFunctionPointerForDelegate(
                    WindowProcedureDelegate),
                Instance = GetModuleHandle(null),
                ClassName = WindowClassName
            };
            _windowClassAtom = RegisterClassEx(ref windowClass);
            if (_windowClassAtom == 0 && Marshal.GetLastWin32Error() != ClassAlreadyExists)
            {
                throw new InvalidOperationException(
                    "The transparent ambient-orb window class could not be registered.");
            }
        }
    }

    private static IntPtr DispatchWindowProcedure(
        IntPtr handle,
        uint message,
        IntPtr wParam,
        IntPtr lParam)
    {
        if (message == WindowMessageNonClientCreate)
        {
            var create = Marshal.PtrToStructure<CreateStruct>(lParam);
            SetWindowData(handle, create.CreateParameters);
        }

        var data = GetWindowData(handle);
        if (data != IntPtr.Zero)
        {
            var instance = GCHandle.FromIntPtr(data).Target as NativeAmbientOrbWindow;
            if (instance is not null)
            {
                return instance.ProcessWindowMessage(handle, message, wParam, lParam);
            }
        }

        return DefWindowProc(handle, message, wParam, lParam);
    }

    private static int SignedLowWord(IntPtr value) =>
        unchecked((short)((long)value & 0xffff));

    private static int SignedHighWord(IntPtr value) =>
        unchecked((short)(((long)value >> 16) & 0xffff));

    private static IntPtr GetWindowData(IntPtr handle) => IntPtr.Size == 8
        ? GetWindowLongPtr64(handle, WindowLongUserData)
        : new IntPtr(GetWindowLong32(handle, WindowLongUserData));

    private static void SetWindowData(IntPtr handle, IntPtr value)
    {
        if (IntPtr.Size == 8)
        {
            SetWindowLongPtr64(handle, WindowLongUserData, value);
        }
        else
        {
            SetWindowLong32(handle, WindowLongUserData, value.ToInt32());
        }
    }

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate IntPtr WindowProcedure(
        IntPtr handle,
        uint message,
        IntPtr wParam,
        IntPtr lParam);

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate void WinEventProcedure(
        IntPtr hook,
        uint eventType,
        IntPtr windowHandle,
        int objectId,
        int childId,
        uint eventThread,
        uint eventTime);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WindowClass
    {
        public uint Size;
        public uint Style;
        public IntPtr WindowProcedure;
        public int ClassExtra;
        public int WindowExtra;
        public IntPtr Instance;
        public IntPtr Icon;
        public IntPtr Cursor;
        public IntPtr BackgroundBrush;
        public string? MenuName;
        public string ClassName;
        public IntPtr SmallIcon;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct CreateStruct
    {
        public IntPtr CreateParameters;
        public IntPtr Instance;
        public IntPtr Menu;
        public IntPtr Parent;
        public int Height;
        public int Width;
        public int Y;
        public int X;
        public int Style;
        public IntPtr Name;
        public IntPtr Class;
        public uint ExtendedStyle;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BitmapInfo
    {
        public BitmapInfoHeader Header;
        public uint Colors;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BitmapInfoHeader
    {
        public uint Size;
        public int Width;
        public int Height;
        public ushort Planes;
        public ushort BitCount;
        public uint Compression;
        public uint SizeImage;
        public int XPelsPerMeter;
        public int YPelsPerMeter;
        public uint ColorsUsed;
        public uint ColorsImportant;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Point
    {
        public Point(int x, int y)
        {
            X = x;
            Y = y;
        }

        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Size
    {
        public Size(int width, int height)
        {
            Width = width;
            Height = height;
        }

        public int Width;
        public int Height;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BlendFunction
    {
        public byte BlendOperation;
        public byte BlendFlags;
        public byte SourceConstantAlpha;
        public byte AlphaFormat;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct TrackMouseEventInfo
    {
        public uint Size;
        public uint Flags;
        public IntPtr WindowHandle;
        public uint HoverTime;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Rect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    private struct MonitorInfo
    {
        public uint Size;
        public Rect Monitor;
        public Rect WorkArea;
        public uint Flags;
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr CreateWindowEx(
        int extendedStyle,
        string className,
        string windowName,
        uint style,
        int x,
        int y,
        int width,
        int height,
        IntPtr parent,
        IntPtr menu,
        IntPtr instance,
        IntPtr parameter);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool DestroyWindow(IntPtr handle);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetWinEventHook(
        uint eventMinimum,
        uint eventMaximum,
        IntPtr eventHookModule,
        WinEventProcedure eventProcedure,
        uint processId,
        uint threadId,
        uint flags);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnhookWinEvent(IntPtr eventHook);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool PostMessage(
        IntPtr handle,
        uint message,
        IntPtr wParam,
        IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern IntPtr GetShellWindow();

    [DllImport("user32.dll")]
    private static extern IntPtr GetDesktopWindow();

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool IsWindowVisible(IntPtr handle);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool IsZoomed(IntPtr handle);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr GetWindow(IntPtr handle, uint command);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint GetWindowThreadProcessId(
        IntPtr handle,
        out uint processId);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr MonitorFromWindow(
        IntPtr handle,
        uint flags);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr MonitorFromRect(
        ref Rect rect,
        uint flags);

    [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern bool GetMonitorInfo(
        IntPtr monitor,
        ref MonitorInfo monitorInfo);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr DefWindowProc(
        IntPtr handle,
        uint message,
        IntPtr wParam,
        IntPtr lParam);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern ushort RegisterClassEx(ref WindowClass windowClass);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW", SetLastError = true)]
    private static extern IntPtr GetWindowLongPtr64(IntPtr handle, int index);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW", SetLastError = true)]
    private static extern IntPtr SetWindowLongPtr64(
        IntPtr handle,
        int index,
        IntPtr value);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongW", SetLastError = true)]
    private static extern int GetWindowLong32(IntPtr handle, int index);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongW", SetLastError = true)]
    private static extern int SetWindowLong32(IntPtr handle, int index, int value);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetWindowPos(
        IntPtr handle,
        IntPtr insertAfter,
        int x,
        int y,
        int width,
        int height,
        uint flags);

    [DllImport("user32.dll", EntryPoint = "ShowWindow", SetLastError = true)]
    private static extern bool ShowNativeWindow(IntPtr handle, int command);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool TrackMouseEvent(ref TrackMouseEventInfo trackingInfo);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern UIntPtr SetTimer(
        IntPtr handle,
        UIntPtr timerId,
        uint intervalMilliseconds,
        IntPtr timerProcedure);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool KillTimer(
        IntPtr handle,
        UIntPtr timerId);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool GetWindowRect(IntPtr handle, out Rect rect);

    [DllImport("dwmapi.dll")]
    private static extern int DwmGetWindowAttribute(
        IntPtr handle,
        int attribute,
        out Rect value,
        int valueSize);

    [DllImport("dwmapi.dll")]
    private static extern int DwmGetWindowAttribute(
        IntPtr handle,
        int attribute,
        out uint value,
        int valueSize);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UpdateLayeredWindow(
        IntPtr handle,
        IntPtr destinationDeviceContext,
        ref Point destination,
        ref Size size,
        IntPtr sourceDeviceContext,
        ref Point source,
        uint colorKey,
        ref BlendFunction blend,
        uint flags);

    [DllImport("gdi32.dll", SetLastError = true)]
    private static extern IntPtr CreateCompatibleDC(IntPtr deviceContext);

    [DllImport("gdi32.dll", SetLastError = true)]
    private static extern IntPtr CreateDIBSection(
        IntPtr deviceContext,
        ref BitmapInfo bitmapInfo,
        uint usage,
        out IntPtr bits,
        IntPtr section,
        uint offset);

    [DllImport("gdi32.dll", SetLastError = true)]
    private static extern IntPtr SelectObject(IntPtr deviceContext, IntPtr graphicsObject);

    [DllImport("gdi32.dll", SetLastError = true)]
    private static extern bool DeleteObject(IntPtr graphicsObject);

    [DllImport("gdi32.dll", SetLastError = true)]
    private static extern bool DeleteDC(IntPtr deviceContext);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr GetModuleHandle(string? moduleName);
}
