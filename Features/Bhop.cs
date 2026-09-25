using CS2Cheat.Core;
using CS2Cheat.Data.Game;
using CS2Cheat.Utils;

namespace CS2Cheat.Features;

public class Bhop : ThreadedServiceBase
{
    private const int FL_ONGROUND = 1;
    private const byte VK_SPACE = 0x20;

    private readonly GameProcess _gameProcess;
    private readonly GameData _gameData;

    // Кэш базового адреса client.dll
    private IntPtr _clientBase = IntPtr.Zero;
    private DateTime _lastModuleCheck = DateTime.MinValue;

    private static ConfigManager Config => ConfigManager.Load();

    protected override string ThreadName => nameof(Bhop);
    protected override TimeSpan ThreadFrameSleep { get; set; } = TimeSpan.Zero;

    public Bhop(GameProcess gameProcess, GameData gameData)
    {
        _gameProcess = gameProcess;
        _gameData = gameData;
    }

    protected override void FrameAction()
    {
        try
        {
            if (!Config.BunnyHop || !_gameProcess.IsValid || _gameData?.Player == null || !_gameData.Player.IsAlive())
            {
                Thread.Sleep(10);
                return;
            }

            // Обновляем базовый адрес client.dll
            if (_clientBase == IntPtr.Zero || (DateTime.Now - _lastModuleCheck).TotalSeconds > 5)
            {
                foreach (System.Diagnostics.ProcessModule m in _gameProcess.Process.Modules)
                    if (m.ModuleName == "client.dll") { _clientBase = m.BaseAddress; break; }
                _lastModuleCheck = DateTime.Now;
            }

            if (_clientBase == IntPtr.Zero) return;

            if ((User32.GetAsyncKeyState(VK_SPACE) & 0x8000) == 0)
            {
                Thread.Sleep(2);
                return;
            }

            int flags = _gameProcess.Process.Read<int>(_gameData.Player.AddressBase + Offsets.m_fFlags);
            bool isOnGround = (flags & FL_ONGROUND) != 0;

            // Если на земле — записываем +jump прямо в память движка
            if (isOnGround)
            {
                IntPtr jumpAddress = _clientBase + Offsets.jump;
                _gameProcess.Write(jumpAddress, 65537); // 65537 = +jump
            }
        }
        catch { }

        Thread.Yield();
    }
}