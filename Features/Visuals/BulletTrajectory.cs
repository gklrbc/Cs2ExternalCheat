using System;
using System.Collections.Generic;
using System.Numerics;
using CS2Cheat.Data.Entity;
using CS2Cheat.Data.Game;
using CS2Cheat.Graphics;
using CS2Cheat.Utils;
using ImGuiNET;

namespace CS2Cheat.Features;

public class BulletTrajectory
{
    public Vector3 Start { get; set; }
    public Vector3 End { get; set; }
    public DateTime SpawnTime { get; set; }
}

public static class BulletTracer
{
    private static readonly List<BulletTrajectory> Trajectories = new();
    private static int _lastBulletCount = 0;
    private static readonly object _lock = new();
    private static int _frameCounter = 0;

    private static ConfigManager? _config;
    private static ConfigManager Config => _config ??= ConfigManager.Load();

    private const float TraceDurationSeconds = 2.0f;
    private const float TraceThickness = 1.5f;
    private const int BulletBufferSize = 16;
    private const int BulletDataSize = 0x40;

    public static void UpdateAndDraw(ImDrawListPtr drawList, GameData gameData, GameProcess gameProcess)
    {
        var player = gameData?.Player;
        if (player == null || !player.IsAlive() || player.AddressBase == IntPtr.Zero)
        {
            lock (_lock) Trajectories.Clear();
            _lastBulletCount = 0;
            return;
        }

        try
        {
            var process = gameProcess.Process;
            if (process == null) return;

            IntPtr bulletServices = process.Read<IntPtr>(player.AddressBase + Offsets.m_pBulletServices);
            if (bulletServices == IntPtr.Zero)
            {
                Console.WriteLine("[DEBUG] bulletServices is Zero");
                return;
            }

            IntPtr bulletArrayBase = process.Read<IntPtr>(bulletServices + Offsets.m_bulletData);

            // Пробуем разные смещения для счетчика (0x08, 0x0C, 0x10)
            int count08 = process.Read<int>(bulletServices + 0x08);
            int count0C = process.Read<int>(bulletServices + 0x0C);
            int count10 = process.Read<int>(bulletServices + 0x10);

            // Раз в секунду выводим отладку
            _frameCounter++;
            if (_frameCounter % 60 == 0)
            {
                Console.WriteLine($"[DEBUG] Counts: +0x08={count08}, +0x0C={count0C}, +0x10={count10}, Array=0x{bulletArrayBase:X}");
            }

            if (bulletArrayBase == IntPtr.Zero) return;

            // Используем +0x08 как наиболее вероятное (проверь логи!)
            int currentBulletCount = count08;

            if (currentBulletCount != _lastBulletCount)
            {
                Console.WriteLine($"[DEBUG] Changed! {currentBulletCount} (was {_lastBulletCount})");

                // Если уменьшился - новый раунд
                if (currentBulletCount < _lastBulletCount)
                {
                    _lastBulletCount = currentBulletCount;
                    return;
                }

                // Добавляем все новые выстрелы
                int newShots = currentBulletCount - _lastBulletCount;
                newShots = Math.Min(newShots, BulletBufferSize); // Защита

                for (int i = 0; i < newShots; i++)
                {
                    int idx = (_lastBulletCount + i) % BulletBufferSize;

                    // Структура: 0x00=start, 0x0C=impact, 0x18=exit
                    Vector3 startPos = process.Read<Vector3>(bulletArrayBase + (idx * BulletDataSize) + 0x00);
                    Vector3 impactPos = process.Read<Vector3>(bulletArrayBase + (idx * BulletDataSize) + 0x0C);

                    Console.WriteLine($"[DEBUG] Shot {idx}: impact={impactPos}");

                    if (impactPos.LengthSquared() > 0.01f && impactPos.LengthSquared() < 1000000f)
                    {
                        Vector3 viewOffset = process.Read<Vector3>(player.AddressBase + Offsets.m_vecViewOffset);
                        Vector3 localOrigin = process.Read<Vector3>(player.AddressBase + Offsets.m_vOldOrigin);
                        Vector3 eyePos = localOrigin + viewOffset;

                        lock (_lock)
                        {
                            Trajectories.Add(new BulletTrajectory
                            {
                                Start = eyePos,
                                End = impactPos,
                                SpawnTime = DateTime.Now
                            });

                            // Лимит 50 трейсеров
                            if (Trajectories.Count > 50)
                                Trajectories.RemoveAt(0);
                        }
                    }
                }

                _lastBulletCount = currentBulletCount;
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[BulletTracer] Error: {ex.Message}");
        }

        // ===== ОТРИСОВКА =====
        lock (_lock)
        {
            if (Trajectories.Count == 0) return;

            var matrix = player.MatrixViewProjectionViewport;
            var io = ImGui.GetIO();
            float screenW = io.DisplaySize.X;
            float screenH = io.DisplaySize.Y;

            var rawColor = Config.EspBoxColor;
            uint tracerColor = OverlayRenderer.ToColor(new Vector4(rawColor[0], rawColor[1], rawColor[2], 1.0f));

            for (int i = Trajectories.Count - 1; i >= 0; i--)
            {
                var traj = Trajectories[i];
                double age = (DateTime.Now - traj.SpawnTime).TotalSeconds;

                if (age > TraceDurationSeconds)
                {
                    Trajectories.RemoveAt(i);
                    continue;
                }

                if (WorldToScreen(matrix, traj.Start, out Vector2 screenStart, screenW, screenH) &&
                    WorldToScreen(matrix, traj.End, out Vector2 screenEnd, screenW, screenH))
                {
                    float alpha = 1.0f - (float)(age / TraceDurationSeconds);
                    uint fadedColor = ApplyAlpha(tracerColor, alpha);

                    drawList.AddLine(screenStart, screenEnd, fadedColor, TraceThickness);
                }
            }
        }
    }

    private static bool WorldToScreen(Matrix4x4 matrix, Vector3 worldPos, out Vector2 screenPos, float screenWidth, float screenHeight)
    {
        screenPos = Vector2.Zero;

        float w = matrix.M14 * worldPos.X + matrix.M24 * worldPos.Y + matrix.M34 * worldPos.Z + matrix.M44;
        if (w < 0.001f) return false;

        float x = matrix.M11 * worldPos.X + matrix.M21 * worldPos.Y + matrix.M31 * worldPos.Z + matrix.M41;
        float y = matrix.M12 * worldPos.X + matrix.M22 * worldPos.Y + matrix.M32 * worldPos.Z + matrix.M42;

        float invW = 1.0f / w;
        screenPos.X = (x * invW + 1.0f) * 0.5f * screenWidth;
        screenPos.Y = (1.0f - y * invW) * 0.5f * screenHeight;

        return true;
    }

    private static uint ApplyAlpha(uint color, float alphaMultiplier)
    {
        byte a = (byte)(255 * alphaMultiplier);
        byte r = (byte)((color >> 16) & 0xFF);
        byte g = (byte)((color >> 8) & 0xFF);
        byte b = (byte)(color & 0xFF);
        return (uint)((a << 24) | (r << 16) | (g << 8) | b);
    }
}