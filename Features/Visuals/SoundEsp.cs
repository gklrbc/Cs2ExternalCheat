using System;
using System.Collections.Generic;
using System.Numerics;
using CS2Cheat.Core;
using CS2Cheat.Data.Entity;
using CS2Cheat.Data.Game;
using CS2Cheat.Utils;

namespace CS2Cheat.Features;

public class SoundEsp
{
    public enum SoundType { Footstep, Gunshot }

    public struct SoundEvent
    {
        public Vector3 Position;
        public DateTime StartTime;
        public DateTime ExpireTime;
        public SoundType Type;
    }

    public List<SoundEvent> ActiveSounds { get; } = new();

    // Для отслеживания выстрелов
    private readonly Dictionary<IntPtr, int> _lastShotsFired = new();
    // Для отслеживания шагов (кулдаун, чтобы не спамить каждый кадр)
    private readonly Dictionary<IntPtr, DateTime> _lastFootstepTime = new();

    private const float FootstepCooldownMs = 400f; // Регистрируем шаг раз в 400 мс
    private const float SoundLifespanMs = 1000f;   // Звук живет 1 секунду

    public void Update(GameProcess gameProcess, GameData gameData)
    {
        CheckEntities(gameProcess, gameData);
        CleanupExpiredSounds();
    }

    private void CheckEntities(GameProcess gameProcess, GameData gameData)
    {
        foreach (var entity in gameData.Entities)
        {
            if (entity?.AddressBase == IntPtr.Zero || !entity.IsAlive())
                continue;

            try
            {
                // 1. Проверка на выстрелы (Громкий звук)
                int currentShots = gameProcess.Process.Read<int>(entity.AddressBase + Offsets.m_iShotsFired);

                if (!_lastShotsFired.ContainsKey(entity.AddressBase))
                {
                    _lastShotsFired[entity.AddressBase] = currentShots;
                }
                else if (currentShots > _lastShotsFired[entity.AddressBase])
                {
                    // Игрок выстрелил
                    Vector3 pos = entity.BonePos.GetValueOrDefault("head");
                    if (pos != Vector3.Zero)
                    {
                        ActiveSounds.Add(new SoundEvent
                        {
                            Position = pos,
                            StartTime = DateTime.Now,
                            ExpireTime = DateTime.Now.AddMilliseconds(SoundLifespanMs),
                            Type = SoundType.Gunshot
                        });
                    }
                }
                _lastShotsFired[entity.AddressBase] = currentShots;

                // 2. Проверка на бег (Звук шагов)
                // Читаем флаги, чтобы убедиться, что игрок на земле
                int flags = gameProcess.Process.Read<int>(entity.AddressBase + Offsets.m_fFlags);
                bool isOnGround = (flags & 1) != 0; // FL_ONGROUND = 1

                if (isOnGround && Offsets.m_vecAbsVelocity != 0)
                {
                    Vector3 velocity = gameProcess.Process.Read<Vector3>(entity.AddressBase + Offsets.m_vecAbsVelocity);
                    // Считаем горизонтальную скорость (без учета прыжков/падений Z)
                    float speed = new Vector2(velocity.X, velocity.Y).Length();

                    // Если скорость больше 100 (бег), регистрируем шаг
                    if (speed > 100f)
                    {
                        bool hasCooldown = _lastFootstepTime.TryGetValue(entity.AddressBase, out var lastTime);
                        if (!hasCooldown || (DateTime.Now - lastTime).TotalMilliseconds > FootstepCooldownMs)
                        {
                            Vector3 pos = entity.BonePos.GetValueOrDefault("pelvis");
                            if (pos == Vector3.Zero) pos = entity.BonePos.GetValueOrDefault("head");

                            if (pos != Vector3.Zero)
                            {
                                ActiveSounds.Add(new SoundEvent
                                {
                                    Position = pos,
                                    StartTime = DateTime.Now,
                                    ExpireTime = DateTime.Now.AddMilliseconds(SoundLifespanMs),
                                    Type = SoundType.Footstep
                                });
                                _lastFootstepTime[entity.AddressBase] = DateTime.Now;
                            }
                        }
                    }
                }
            }
            catch { }
        }
    }

    private void CleanupExpiredSounds()
    {
        for (int i = ActiveSounds.Count - 1; i >= 0; i--)
        {
            if (DateTime.Now > ActiveSounds[i].ExpireTime)
            {
                ActiveSounds.RemoveAt(i);
            }
        }
    }
}