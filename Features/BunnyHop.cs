using System.Runtime.InteropServices;
using CS2Cheat.Core;
using CS2Cheat.Core.Data;
using CS2Cheat.Data.Game;
using CS2Cheat.Utils;
using Keys = Process.NET.Native.Types.Keys;

namespace CS2Cheat.Features;

public class BunnyHop : ThreadedServiceBase
{
    private const int BhopUpdateIntervalMs = 1;
    private const int FL_ONGROUND = (1 << 0);

    // Импорт функций Windows API для отправки нажатий клавиатуры
    [DllImport("user32.dll")]
    private static extern void keybd_event(byte bVk, byte bScan, uint dwFlags, int dwExtraInfo);

    private const byte VK_SPACE = 0x20;
    private const uint KEYEVENTF_KEYUP = 0x0002;

    public BunnyHop(GameProcess gameProcess, GameData gameData)
    {
        GameProcess = gameProcess;
        GameData = gameData;
    }

    private static ConfigManager Config => ConfigManager.Load();
    protected override string ThreadName => nameof(BunnyHop);
    private GameProcess? GameProcess { get; set; }
    private GameData? GameData { get; set; }

    public override void Dispose()
    {
        base.Dispose();
        GameData = null;
        GameProcess = null;
    }

    protected override void FrameAction()
    {
        try
        {
            if (GameProcess == null || !GameProcess.IsValid || GameData?.Player == null || !GameData.Player.IsAlive())
            {
                Thread.Sleep(5);
                return;
            }

            if (!Config.BunnyHop)
            {
                Thread.Sleep(10);
                return;
            }

            // Если пользователь зажал Пробел
            if (Keys.Space.IsKeyDown())
            {
                var player = GameData.Player;

                // Считываем флаги из памяти, чтобы узнать, на земле ли мы
                int flags = GameProcess.Process.Read<int>(player.AddressBase + Offsets.m_fFlags);

                // Если персонаж стоит на земле
                if ((flags & FL_ONGROUND) != 0)
                {
                    // Посылаем сигнал Windows: Нажать Пробел и сразу Отпустить Пробел
                    keybd_event(VK_SPACE, 0, 0, 0);
                    Thread.Sleep(10); // Минимальная задержка, чтобы игра успела зарегистрировать клик
                    keybd_event(VK_SPACE, 0, KEYEVENTF_KEYUP, 0);
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Bhop Error] {ex.Message}");
        }

        Thread.Sleep(BhopUpdateIntervalMs);
    }
}