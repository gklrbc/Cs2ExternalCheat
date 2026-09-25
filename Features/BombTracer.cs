using System.Numerics;
using CS2Cheat.Core.Data;
using CS2Cheat.Data.Game;
using CS2Cheat.Graphics;
using CS2Cheat.Utils;
using ImGuiNET;

namespace CS2Cheat.Features;

public static class BombTracer
{
    public static void Draw(ImDrawListPtr dl, GameData gameData, GameProcess gameProcess)
    {
        var player = gameData.Player;
        if (player == null || gameProcess?.Process == null || gameProcess.ModuleClient == null) return;

        IntPtr plantedBombPtr = IntPtr.Zero;
        try
        {
            // Прямое чтение указателя без сторонних проверок на установку, которые могут сбоить
            IntPtr tempC4 = gameProcess.ModuleClient.Read<IntPtr>(Offsets.dwPlantedC4);
            plantedBombPtr = gameProcess.Process.Read<IntPtr>(tempC4);
        }
        catch
        {
            return;
        }

        // Если бомба не установлена или взорвана, указатель будет равен чистой структуре или Zero
        if (plantedBombPtr == IntPtr.Zero) return;

        Vector3 bombWorldPos = Vector3.Zero;
        try
        {
            // 1. Читаем указатель на GameSceneNode (m_pGameSceneNode обычно равен 0x310 или 0x318 в CS2)
            IntPtr gameSceneNode = gameProcess.Process.Read<IntPtr>(plantedBombPtr + 0x310);
            if (gameSceneNode == IntPtr.Zero)
            {
                gameSceneNode = gameProcess.Process.Read<IntPtr>(plantedBombPtr + 0x318);
            }

            if (gameSceneNode != IntPtr.Zero)
            {
                // 2. Читаем m_vecAbsOrigin (абсолютные координаты объекта на карте, смещение 0xC0 или 0xC8)
                bombWorldPos = gameProcess.Process.Read<Vector3>(gameSceneNode + 0xC0);
                if (bombWorldPos == Vector3.Zero)
                {
                    bombWorldPos = gameProcess.Process.Read<Vector3>(gameSceneNode + 0xC8);
                }
            }

            // Резервный вариант: если GameSceneNode пуст, пробуем прочитать координаты напрямую из структуры C4
            if (bombWorldPos == Vector3.Zero)
            {
                bombWorldPos = gameProcess.Process.Read<Vector3>(plantedBombPtr + 0x1274);
            }
        }
        catch
        {
            return;
        }

        // Если координаты всё ещё не найдены на карте — останавливаем отрисовку
        if (bombWorldPos == Vector3.Zero) return;

        // Трансформируем 3D-вектор бомбы в 2D-координаты твоего экрана
        var matrix = player.MatrixViewProjectionViewport;
        var transformed = matrix.Transform(bombWorldPos);

        // Проверка: если бомба находится за пределами обзора камеры (сзади игрока)
        if (transformed.Z >= 1) return;

        var io = ImGui.GetIO();
        Vector2 bombScreenPos = new Vector2(transformed.X, transformed.Y);
        Vector2 startPos = new Vector2(io.DisplaySize.X / 2f, io.DisplaySize.Y);

        // Рендерим оранжевую линию от центра низа экрана к бомбе
        dl.AddLine(startPos, bombScreenPos, OverlayRenderer.ToColor(255, 140, 0), 1.5f);

        // Рендерим маркер
        dl.AddRectFilled(
            new Vector2(bombScreenPos.X - 4, bombScreenPos.Y - 4),
            new Vector2(bombScreenPos.X + 4, bombScreenPos.Y + 4),
            OverlayRenderer.Colors.Red
        );
    }
}