using System;
using System.Collections.Generic;
using System.Numerics;
using CS2Cheat.Core;
using CS2Cheat.Data.Entity;
using CS2Cheat.Data.Game;
using CS2Cheat.Utils;

namespace CS2Cheat.Features;

public class BulletTracers
{
    public enum TracerSource
    {
        LocalPlayer,
        Enemy,
        Teammate
    }

    public struct Tracer
    {
        public Vector3 StartPos;
        public Vector3 EndPos;
        public DateTime CreationTime;
        public DateTime ExpireTime;
        public TracerSource Source;
    }

    public List<Tracer> ActiveTracers { get; } = new();
    private readonly Dictionary<IntPtr, int> _lastShotsFired = new();
    private const float TracerDurationMs = 2000f;

    public void Update(GameProcess gameProcess, GameData gameData)
    {
        if (gameData?.Player == null || gameData?.Entities == null) return;
        CheckForShots(gameProcess, gameData);
        CleanupExpiredTracers();
    }

    private void CheckForShots(GameProcess gameProcess, GameData gameData)
    {
        // ===== 1. ЛОКАЛЬНЫЙ ИГРОК =====
        try
        {
            int localShots = gameData.Player.ShotsFired;

            if (!_lastShotsFired.ContainsKey(gameData.Player.AddressBase))
            {
                _lastShotsFired[gameData.Player.AddressBase] = localShots;
            }
            else if (localShots > _lastShotsFired[gameData.Player.AddressBase])
            {
                // БЕРЕМ ПОЗИЦИЮ ИЗ КОСТИ ГОЛОВЫ, А НЕ ИЗ EyePosition (которая может быть 0,0,0)
                Vector3 startPos = GetLocalHeadBonePosition(gameProcess, gameData);
                Vector3 viewAngles = gameData.Player.ViewAngles;
                Vector3 aimPunch = gameData.Player.AimPunchAngle;

                if (startPos != Vector3.Zero && viewAngles != Vector3.Zero)
                {
                    float pitch = viewAngles.X + (aimPunch.X * 2.0f);
                    float yaw = viewAngles.Y + (aimPunch.Y * 2.0f);

                    Vector3 direction = AngleToDirection(pitch, yaw);
                    Vector3 traceStart = startPos + (direction * 6.0f);
                    Vector3 traceEnd = startPos + (direction * 10000.0f);

                    ActiveTracers.Add(new Tracer
                    {
                        StartPos = traceStart,
                        EndPos = traceEnd,
                        CreationTime = DateTime.Now,
                        ExpireTime = DateTime.Now.AddMilliseconds(TracerDurationMs),
                        Source = TracerSource.LocalPlayer
                    });
                }
            }
            _lastShotsFired[gameData.Player.AddressBase] = localShots;
        }
        catch { }

        // ===== 2. ВРАГИ И СОЮЗНИКИ =====
        foreach (var entity in gameData.Entities)
        {
            if (entity?.AddressBase == IntPtr.Zero || !entity.IsAlive()) continue;
            if (entity.AddressBase == gameData.Player.AddressBase) continue;

            try
            {
                int currentShots = entity.ShotsFired;

                if (!_lastShotsFired.ContainsKey(entity.AddressBase))
                {
                    _lastShotsFired[entity.AddressBase] = currentShots;
                    continue;
                }

                if (currentShots > _lastShotsFired[entity.AddressBase])
                {
                    // БЕРЕМ ПОЗИЦИЮ ИЗ КОСТИ ГОЛОВЫ ВРАГА
                    Vector3 startPos = entity.BonePos.GetValueOrDefault("head");
                    if (startPos == Vector3.Zero)
                    {
                        _lastShotsFired[entity.AddressBase] = currentShots;
                        continue;
                    }

                    Vector3 viewAngles = GetEnemyViewAngles(gameProcess, entity);
                    Vector2 aimPunch = GetEnemyAimPunch(gameProcess, entity);

                    float pitch = viewAngles.X + (aimPunch.X * 2.0f);
                    float yaw = viewAngles.Y + (aimPunch.Y * 2.0f);

                    Vector3 direction = AngleToDirection(pitch, yaw);
                    Vector3 traceStart = startPos + (direction * 6.0f);
                    Vector3 traceEnd = startPos + (direction * 10000.0f);

                    TracerSource source = ((int)entity.Team == (int)gameData.Player.Team) ? TracerSource.Teammate : TracerSource.Enemy;

                    ActiveTracers.Add(new Tracer
                    {
                        StartPos = traceStart,
                        EndPos = traceEnd,
                        CreationTime = DateTime.Now,
                        ExpireTime = DateTime.Now.AddMilliseconds(TracerDurationMs),
                        Source = source
                    });
                }

                _lastShotsFired[entity.AddressBase] = currentShots;
            }
            catch { }
        }
    }

    private Vector3 GetLocalHeadBonePosition(GameProcess gameProcess, GameData gameData)
    {
        try
        {
            IntPtr gameSceneNode = gameProcess.Process.Read<IntPtr>(gameData.Player.AddressBase + Offsets.m_pGameSceneNode);
            if (gameSceneNode != IntPtr.Zero)
            {
                IntPtr boneArray = gameProcess.Process.Read<IntPtr>(gameSceneNode + Offsets.m_modelState + 128);
                if (boneArray != IntPtr.Zero)
                {
                    // Индекс кости головы = 7 (из твоего Offsets.Bones)
                    return gameProcess.Process.Read<Vector3>(boneArray + 7 * 32);
                }
            }
        }
        catch { }

        // Fallback на EyePosition, если кости не прогрузились
        return gameData.Player.EyePosition;
    }

    private Vector3 GetEnemyViewAngles(GameProcess gameProcess, Entity entity)
    {
        if (Offsets.m_vOldViewAngles == 0) return Vector3.Zero;
        try
        {
            float pitch = gameProcess.Process.Read<float>(entity.AddressBase + Offsets.m_vOldViewAngles);
            float yaw = gameProcess.Process.Read<float>(entity.AddressBase + Offsets.m_vOldViewAngles + 4);
            return new Vector3(pitch, yaw, 0f);
        }
        catch { return Vector3.Zero; }
    }

    private Vector2 GetEnemyAimPunch(GameProcess gameProcess, Entity entity)
    {
        try
        {
            var aimPunchService = gameProcess.Process.Read<IntPtr>(entity.AddressBase + Offsets.m_pAimPunchServices);
            if (aimPunchService != IntPtr.Zero)
            {
                var punch = gameProcess.Process.Read<Vector3>(aimPunchService + Offsets.m_vecCsViewPunchAngle);
                return new Vector2(punch.X, punch.Y);
            }

            var cacheAddress = entity.AddressBase + Offsets.m_AimPunchCache;
            int count = gameProcess.Process.Read<int>(cacheAddress);
            var cacheData = gameProcess.Process.Read<IntPtr>(cacheAddress + 0x8);

            if (count > 0 && count < 128 && cacheData != IntPtr.Zero)
            {
                var punch = gameProcess.Process.Read<Vector3>(cacheData + (count - 1) * 12);
                return new Vector2(punch.X, punch.Y);
            }

            var direct = gameProcess.Process.Read<Vector3>(entity.AddressBase + Offsets.m_AimPunchAngle);
            return new Vector2(direct.X, direct.Y);
        }
        catch { return Vector2.Zero; }
    }

    private Vector3 AngleToDirection(float pitch, float yaw)
    {
        float radPitch = pitch * (MathF.PI / 180f);
        float radYaw = yaw * (MathF.PI / 180f);

        float cosPitch = MathF.Cos(radPitch);
        float x = MathF.Cos(radYaw) * cosPitch;
        float y = MathF.Sin(radYaw) * cosPitch;
        float z = -MathF.Sin(radPitch);

        return new Vector3(x, y, z);
    }

    private void CleanupExpiredTracers()
    {
        ActiveTracers.RemoveAll(t => DateTime.Now > t.ExpireTime);
    }
}