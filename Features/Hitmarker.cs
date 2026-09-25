using System.Numerics;
using System.Runtime.InteropServices;
using System.Media;
using System.IO;
using CS2Cheat.Data.Game;
using CS2Cheat.Graphics;
using CS2Cheat.Utils;
using ImGuiNET;
using System.Collections.Generic;

namespace CS2Cheat.Features;

public static class Hitmarker
{
    private static float _hitTime = -1f;
    private static int _lastShotsFired = 0;
    private static float _shotWindow = 0f;
    private static Dictionary<nint, int> _healthBeforeShot = new();

    private static SoundPlayer _hitSoundPlayer;
    private static bool _soundInitialized = false;

    [DllImport("winmm.dll")]
    private static extern bool PlaySound(string pszSound, IntPtr hmod, uint fdwSound);
    private const uint SND_ASYNC = 0x0001;
    private const uint SND_FILENAME = 0x00020000;

    public static void InitializeSound()
    {
        if (_soundInitialized) return;
        try
        {
            string soundPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "hitsound.wav");
            if (File.Exists(soundPath))
            {
                _hitSoundPlayer = new SoundPlayer(soundPath);
                _hitSoundPlayer.Load();
            }
        }
        catch { }
        _soundInitialized = true;
    }

    private static void PlayHitSound()
    {
        try
        {
            if (_hitSoundPlayer != null) { _hitSoundPlayer.Play(); return; }
            string soundPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "hitsound.wav");
            if (File.Exists(soundPath)) { PlaySound(soundPath, IntPtr.Zero, SND_ASYNC | SND_FILENAME); return; }
            Console.Beep(1200, 80);
        }
        catch { }
    }

    public static void TriggerPreview()
    {
        _hitTime = (float)ImGui.GetTime();
        if (ConfigManager.Load().HitmarkerSound) PlayHitSound();
    }

    public static void Draw(ImDrawListPtr drawList, GameData gameData, GameProcess gameProcess)
    {
        if (gameData?.Player == null) return;
        var config = ConfigManager.Load();
        if (!config.Hitmarker) return;

        if (!_soundInitialized) InitializeSound();
        UpdateHitState(gameProcess, gameData, config.HitmarkerSound);

        float timeSinceHit = ((float)ImGui.GetTime()) - _hitTime;
        if (timeSinceHit < config.HitmarkerDuration && _hitTime > 0)
        {
            RenderHitmarker(drawList, timeSinceHit, config);
        }
    }

    private static void UpdateHitState(GameProcess gameProcess, GameData gameData, bool playSound = true)
    {
        try
        {
            if (gameProcess?.Process == null || gameData?.Player == null) return;
            var process = gameProcess.Process;
            var player = gameData.Player;
            float currentTime = (float)ImGui.GetTime();

            // 1. Детектим выстрел и записываем здоровье всех врагов
            if (Offsets.m_iShotsFired != 0)
            {
                int currentShots = process.Read<int>(player.AddressBase + Offsets.m_iShotsFired);
                if (currentShots > _lastShotsFired)
                {
                    _shotWindow = currentTime + 0.3f; // Окно в 300мс для регистрации хита
                    _healthBeforeShot.Clear();
                    foreach (var entity in gameData.Entities)
                    {
                        if (entity?.AddressBase == nint.Zero || entity.Team == player.Team) continue;
                        try { _healthBeforeShot[entity.AddressBase] = process.Read<int>(entity.AddressBase + Offsets.m_iHealth); } catch { }
                    }
                }
                _lastShotsFired = currentShots;
            }

            // 2. Ищем хит в пределах окна
            if (currentTime < _shotWindow)
            {
                foreach (var entity in gameData.Entities)
                {
                    if (entity?.AddressBase == nint.Zero || entity.Team == player.Team) continue;
                    if (!_healthBeforeShot.ContainsKey(entity.AddressBase)) continue;

                    try
                    {
                        int currentHealth = process.Read<int>(entity.AddressBase + Offsets.m_iHealth);
                        int oldHealth = _healthBeforeShot[entity.AddressBase];

                        if (currentHealth < oldHealth && currentHealth >= 0)
                        {
                            _hitTime = currentTime;
                            _shotWindow = 0f; // Сбрасываем окно, хит зарегистрирован
                            _healthBeforeShot[entity.AddressBase] = currentHealth; // Обновляем, чтобы не спамить
                            if (playSound) PlayHitSound();
                            break;
                        }
                    }
                    catch { }
                }
            }
        }
        catch { }
    }

    private static void RenderHitmarker(ImDrawListPtr drawList, float timeSinceHit, ConfigManager config)
    {
        float progress = timeSinceHit / config.HitmarkerDuration;
        float alpha = 1f - (progress * progress);
        float expand = progress * config.HitmarkerExpand;
        float currentGap = config.HitmarkerGap + expand;
        float currentLength = config.HitmarkerLength * (1f - progress * 0.3f);

        var io = ImGui.GetIO();
        var center = new Vector2(io.DisplaySize.X / 2, io.DisplaySize.Y / 2);

        uint color = OverlayRenderer.ToColor(new Vector4(config.HitmarkerColor.X, config.HitmarkerColor.Y, config.HitmarkerColor.Z, config.HitmarkerColor.W * alpha));
        uint outlineColor = OverlayRenderer.ToColor(new Vector4(0f, 0f, 0f, config.HitmarkerOutlineAlpha * alpha));

        DrawLine(drawList, center, -1, -1, currentGap, currentLength, color, outlineColor, config.HitmarkerThickness, config.HitmarkerOutline);
        DrawLine(drawList, center, 1, -1, currentGap, currentLength, color, outlineColor, config.HitmarkerThickness, config.HitmarkerOutline);
        DrawLine(drawList, center, -1, 1, currentGap, currentLength, color, outlineColor, config.HitmarkerThickness, config.HitmarkerOutline);
        DrawLine(drawList, center, 1, 1, currentGap, currentLength, color, outlineColor, config.HitmarkerThickness, config.HitmarkerOutline);
    }

    private static void DrawLine(ImDrawListPtr drawList, Vector2 center, int dirX, int dirY, float gap, float length, uint color, uint outlineColor, float thickness, bool outline)
    {
        Vector2 start = new(center.X + dirX * gap, center.Y + dirY * gap);
        Vector2 end = new(start.X + dirX * length * 0.7f, start.Y + dirY * length * 0.7f);
        if (outline) drawList.AddLine(start, end, outlineColor, thickness + 1.5f);
        drawList.AddLine(start, end, color, thickness);
    }
}