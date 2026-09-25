using System.Numerics;
using CS2Cheat.Core;
using CS2Cheat.Data.Game;
using CS2Cheat.Utils;

namespace CS2Cheat.Features;

public class Rcs : ThreadedServiceBase
{
    private readonly GameProcess _gameProcess;
    private readonly GameData _gameData;

    private Vector2 _previousPunch = Vector2.Zero;
    private Vector2 _rcsAccumulated = Vector2.Zero;
    private const float RcsSensitivityScale = 10.0f; // Если слабо тянет — увеличь до 15.0f

    private static ConfigManager Config => ConfigManager.Load();

    protected override string ThreadName => nameof(Rcs);
    protected override TimeSpan ThreadFrameSleep { get; set; } = new(0, 0, 0, 0, 1);

    public Rcs(GameProcess gameProcess, GameData gameData)
    {
        _gameProcess = gameProcess;
        _gameData = gameData;
    }

    protected override void FrameAction()
    {
        try
        {
            if (!_gameProcess.IsValid || _gameData?.Player == null || !Config.RcsEnabled || !_gameData.Player.IsAlive())
            {
                _previousPunch = Vector2.Zero;
                _rcsAccumulated = Vector2.Zero;
                Thread.Sleep(10);
                return;
            }

            // Берем AimPunchAngle НАПРЯМУЮ из Player.cs
            Vector3 punchVec = _gameData.Player.AimPunchAngle;
            Vector2 currentPunch = new Vector2(punchVec.X, punchVec.Y);

            // Если отдачи нет (не стреляем) - сбрасываем
            if (currentPunch.Length() < 0.01f)
            {
                _previousPunch = currentPunch;
                _rcsAccumulated = Vector2.Zero;
                Thread.Sleep(2);
                return;
            }

            // Считаем дельту отдачи
            Vector2 punchDelta = new Vector2(
                currentPunch.X - _previousPunch.X,
                currentPunch.Y - _previousPunch.Y
            );

            // Масштабируем (WeaponRecoilScale = 2.0f)
            punchDelta.X *= Offsets.WeaponRecoilScale;
            punchDelta.Y *= Offsets.WeaponRecoilScale;

            // Преобразуем в движение мыши
            Vector2 compensation = new Vector2(
                -punchDelta.Y * RcsSensitivityScale,   // Горизонталь
                punchDelta.X * RcsSensitivityScale    // Вертикаль
            );

            // Накапливаем пиксели
            _rcsAccumulated += compensation;

            int moveX = (int)System.Math.Round(_rcsAccumulated.X);
            int moveY = (int)System.Math.Round(_rcsAccumulated.Y);

            _rcsAccumulated.X -= moveX;
            _rcsAccumulated.Y -= moveY;

            // Ограничиваем максимальный шаг
            moveX = System.Math.Clamp(moveX, -25, 25);
            moveY = System.Math.Clamp(moveY, -25, 25);

            // Двигаем мышь
            if (System.Math.Abs(moveX) > 0 || System.Math.Abs(moveY) > 0)
            {
                Utility.MouseMove(moveX, moveY);
            }

            _previousPunch = currentPunch;
        }
        catch { }

        Thread.Sleep(1);
    }
}