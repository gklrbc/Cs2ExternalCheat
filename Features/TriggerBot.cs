using CS2Cheat.Core.Data;
using CS2Cheat.Data.Game;
using CS2Cheat.Utils;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Keys = Process.NET.Native.Types.Keys;

namespace CS2Cheat.Features;

public sealed class TriggerBot : ThreadedServiceBase
{
    private const int EntityListMultiplier = 0x8;
    private const int EntityEntryOffset = 0x10;
    private const int EntityStride = 0x70;
    private const int EntityIndexMask = 0x1FF;
    private const int EntityIndexShift = 9;

    private readonly GameData _gameData;
    private readonly GameProcess _gameProcess;
    private readonly Stopwatch _stopwatch = new();

    private ConfigManager _cachedConfig;
    private long _lastConfigUpdate;
    private const long ConfigCacheTime = 500;

    private IntPtr _cachedEntityList;
    private IntPtr _cachedLocalPawn;
    private int _cachedLocalTeam;
    private long _cacheTimestamp;
    private const long CacheValidityMs = 8;

    private long _lastShotTime = 0;

    public TriggerBot(GameProcess gameProcess, GameData gameData)
    {
        _gameProcess = gameProcess ?? throw new ArgumentNullException(nameof(gameProcess));
        _gameData = gameData ?? throw new ArgumentNullException(nameof(gameData));
        _cachedConfig = ConfigManager.Load();
        _stopwatch.Start();
    }

    protected override string ThreadName => nameof(TriggerBot);
    protected override TimeSpan ThreadFrameSleep { get; set; } = TimeSpan.Zero; // Максимальная скорость потока

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    protected override void FrameAction()
    {
        var currentTime = _stopwatch.ElapsedMilliseconds;
        if (currentTime - _lastConfigUpdate > ConfigCacheTime)
        {
            _cachedConfig = ConfigManager.Load();
            _lastConfigUpdate = currentTime;
        }

        if (!_cachedConfig.TriggerBot)
        {
            Thread.Sleep(2);
            return;
        }

        if (!ShouldExecute())
        {
            _lastShotTime = 0;
            Thread.Sleep(2);
            return;
        }

        var target = GetTargetEntity();
        if (target == IntPtr.Zero)
        {
            Thread.Yield();
            return;
        }

        if (!ShouldTriggerOnEntity(target))
        {
            Thread.Yield();
            return;
        }

        if (_lastShotTime == 0 || (currentTime - _lastShotTime) >= (long)_cachedConfig.TriggerDelay)
        {
            ExecuteInstantShot();
            _lastShotTime = currentTime;
        }

        Thread.Yield();
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private bool ShouldExecute()
    {
        return _gameProcess.IsValid &&
               _cachedConfig.TriggerBotKey.IsKeyDown() &&
               _gameData.Player != null;
    }

    private IntPtr GetTargetEntity()
    {
        var currentTime = _stopwatch.ElapsedMilliseconds;

        if (currentTime - _cacheTimestamp > CacheValidityMs || _cachedLocalPawn == IntPtr.Zero)
        {
            _cachedEntityList = _gameProcess.ModuleClient.Read<IntPtr>(Offsets.dwEntityList);
            _cachedLocalPawn = _gameProcess.ModuleClient.Read<IntPtr>(Offsets.dwLocalPlayerPawn);
            _cachedLocalTeam = (int)_gameData.Player.Team;
            _cacheTimestamp = currentTime;
        }

        if (_cachedLocalPawn == IntPtr.Zero) return IntPtr.Zero;

        var entityId = _gameProcess.Process.Read<int>(_cachedLocalPawn + Offsets.m_iIDEntIndex);
        if (entityId < 0) return IntPtr.Zero;

        var entityEntry = _gameProcess.Process.Read<IntPtr>(
            _cachedEntityList + EntityListMultiplier * (entityId >> EntityIndexShift) + EntityEntryOffset);

        if (entityEntry == IntPtr.Zero) return IntPtr.Zero;

        return _gameProcess.Process.Read<IntPtr>(
            entityEntry + EntityStride * (entityId & EntityIndexMask));
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private bool ShouldTriggerOnEntity(IntPtr targetEntity)
    {
        if (_gameData.Player == null) return false;

        try
        {
            var entityTeam = _gameProcess.Process.Read<int>(targetEntity + Offsets.m_iTeamNum);
            var entityHealth = _gameProcess.Process.Read<int>(targetEntity + Offsets.m_iHealth);

            if (entityHealth > 0 && entityTeam != _cachedLocalTeam)
            {
                return true;
            }
        }
        catch { }

        return false;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void ExecuteInstantShot()
    {
        // Мгновенный выстрел (DOWN и UP подряд, без задержек)
        NativeMethods.mouse_event(0x0002, 0, 0, 0, UIntPtr.Zero);
        NativeMethods.mouse_event(0x0004, 0, 0, 0, UIntPtr.Zero);
    }

    public override void Dispose()
    {
        _stopwatch.Stop();
        base.Dispose();
        GC.SuppressFinalize(this);
    }

    private static class NativeMethods
    {
        [DllImport("user32.dll")]
        public static extern void mouse_event(uint dwFlags, int dx, int dy, uint dwData, UIntPtr dwExtraInfo);
    }
}