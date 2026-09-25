using System.Numerics;
using System.Runtime.InteropServices;
using CS2Cheat.Core;
using CS2Cheat.Data.Entity;
using CS2Cheat.Data.Game;
using CS2Cheat.Utils;
using Keys = Process.NET.Native.Types.Keys;

namespace CS2Cheat.Features;

public class SilentAim : ThreadedServiceBase
{
    private const int UpdateIntervalMs = 1;

    private GameProcess? GameProcess { get; set; }
    private GameData? GameData { get; set; }

    private Vector3 _savedAngles = Vector3.Zero;
    private bool _anglesModified = false;
    private bool _keyWasDown = false;

    private IntPtr _clientBase = IntPtr.Zero;
    private DateTime _lastModuleCheck = DateTime.MinValue;

    // Локальные данные (читаем напрямую для скорости)
    private float[] _matrix = new float[16];
    private int _centerX, _centerY;

    [DllImport("user32.dll")] private static extern bool GetClientRect(IntPtr hWnd, out RECT lpRect);
    [StructLayout(LayoutKind.Sequential)]
    private struct RECT { public int Left, Top, Right, Bottom; }

    private static ConfigManager Config => ConfigManager.Load();

    protected override string ThreadName => nameof(SilentAim);
    protected override TimeSpan ThreadFrameSleep { get; set; } = new(0, 0, 0, 0, UpdateIntervalMs);

    public SilentAim(GameProcess gameProcess, GameData gameData)
    {
        GameProcess = gameProcess;
        GameData = gameData;
    }

    protected override void FrameAction()
    {
        try
        {
            if (GameProcess?.IsValid != true || GameData?.Player?.IsAlive() != true)
            {
                if (_anglesModified) RestoreAngles();
                _keyWasDown = false;
                return;
            }

            if (!Config.SilentAim)
            {
                if (_anglesModified) RestoreAngles();
                _keyWasDown = false;
                return;
            }

            UpdateClientBase();
            if (_clientBase == IntPtr.Zero) return;

            UpdateScreen();
            UpdateMatrix();

            // Читаем текущие углы НАПРЯМУЮ из памяти
            Vector3 currentAngles = ReadViewAngles();
            if (currentAngles == Vector3.Zero) return;

            bool keyDown = Config.SilentAimKey.IsKeyDown();

            if (keyDown && !_keyWasDown)
            {
                _savedAngles = currentAngles;
                _keyWasDown = true;
            }

            if (keyDown)
            {
                // Ищем таргет используя WorldToScreen (как в AimBot)
                var target = GetBestTarget();
                if (target != null)
                {
                    // Берем кость (с той же поправкой +3.0f для головы, что и в аиме)
                    string boneName = GetBoneName(Config.SilentAimBoneIndex);
                    Vector3 aimPos = target.BonePos.GetValueOrDefault(boneName);
                    if (boneName == "head" && aimPos != Vector3.Zero)
                        aimPos.Z += 3.0f;

                    if (aimPos != Vector3.Zero)
                    {
                        Vector3 eyePos = ReadEyePosition();

                        // Считаем угол (возвращает [yaw, pitch])
                        Vector2 aimAngle = CalcAngle(eyePos, aimPos);

                        // Компенсация отдачи
                        Vector2 punch = ReadAimPunch();
                        aimAngle.X -= punch.Y * Offsets.WeaponRecoilScale;
                        aimAngle.Y -= punch.X * Offsets.WeaponRecoilScale;

                        // Нормализация
                        while (aimAngle.X > 180f) aimAngle.X -= 360f;
                        while (aimAngle.X < -180f) aimAngle.X += 360f;
                        while (aimAngle.Y > 89f) aimAngle.Y -= 360f;
                        while (aimAngle.Y < -89f) aimAngle.Y += 360f;

                        // Конвертируем в [pitch, yaw] для записи в память
                        Vector3 memTarget = new(aimAngle.Y, aimAngle.X, 0f);
                        WriteViewAngles(memTarget);
                        _anglesModified = true;
                    }
                }
            }
            else
            {
                if (_anglesModified) RestoreAngles();
                _keyWasDown = false;
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[SilentAim] {ex.Message}");
        }

        Thread.Sleep(UpdateIntervalMs);
    }

    private Entity? GetBestTarget()
    {
        Entity? best = null;
        float bestScore = float.MaxValue;
        int myTeam = (int)GameData!.Player.Team;

        foreach (var entity in GameData.Entities)
        {
            if (entity?.AddressBase == IntPtr.Zero || !entity.IsAlive()) continue;
            if (Config.TeamCheck && (int)entity.Team == myTeam) continue;

            if (Offsets.m_pGameSceneNode != 0)
            {
                try
                {
                    IntPtr gsn = GameProcess!.Process.Read<IntPtr>(entity.AddressBase + Offsets.m_pGameSceneNode);
                    if (gsn != IntPtr.Zero && GameProcess.Process.Read<bool>(gsn + Offsets.m_bDormant))
                        continue;
                }
                catch { continue; }
            }

            string boneName = GetBoneName(Config.SilentAimBoneIndex);
            Vector3 pos = entity.BonePos.GetValueOrDefault(boneName);
            if (boneName == "head" && pos != Vector3.Zero)
                pos.Z += 3.0f;

            if (pos == Vector3.Zero) continue;
            if (!WorldToScreen(pos, out var screen)) continue;

            var delta = new Vector2(screen.X - _centerX, screen.Y - _centerY);
            float screenDist = delta.Length();

            float maxFov = Config.SilentAimFov * 10f;
            if (screenDist > maxFov) continue;

            float dist3D = Vector3.Distance(ReadEyePosition(), pos);
            float score = screenDist * 2f + dist3D * 0.005f - (100 - entity.Health) * 0.1f;

            if (score < bestScore)
            {
                bestScore = score;
                best = entity;
            }
        }

        return best;
    }

    private Vector3 ReadEyePosition()
    {
        try
        {
            Vector3 origin = GameProcess!.Process.Read<Vector3>(GameData!.Player.AddressBase + Offsets.m_vOldOrigin);
            Vector3 viewOffset = GameProcess.Process.Read<Vector3>(GameData.Player.AddressBase + Offsets.m_vecViewOffset);
            return origin + viewOffset;
        }
        catch { return Vector3.Zero; }
    }

    private Vector2 ReadAimPunch()
    {
        try
        {
            var aimPunchService = GameProcess!.Process.Read<IntPtr>(GameData!.Player.AddressBase + Offsets.m_pAimPunchServices);
            if (aimPunchService != IntPtr.Zero)
            {
                var punch = GameProcess.Process.Read<Vector3>(aimPunchService + Offsets.m_vecCsViewPunchAngle);
                return new Vector2(punch.X, punch.Y);
            }

            var cacheAddress = GameData!.Player.AddressBase + Offsets.m_AimPunchCache;
            int count = GameProcess.Process.Read<int>(cacheAddress);
            var cacheData = GameProcess.Process.Read<IntPtr>(cacheAddress + 0x8);
            if (count > 0 && count < 128 && cacheData != IntPtr.Zero)
            {
                var punch = GameProcess.Process.Read<Vector3>(cacheData + (count - 1) * 12);
                return new Vector2(punch.X, punch.Y);
            }
        }
        catch { }
        return Vector2.Zero;
    }

    private Vector3 ReadViewAngles()
    {
        try
        {
            float pitch = GameProcess!.Process.Read<float>(_clientBase + Offsets.dwViewAngles);
            float yaw = GameProcess.Process.Read<float>(_clientBase + Offsets.dwViewAngles + 4);
            return new Vector3(pitch, yaw, 0f);
        }
        catch { return Vector3.Zero; }
    }

    private void WriteViewAngles(Vector3 memAngles)
    {
        try
        {
            GameProcess!.Write(_clientBase + Offsets.dwViewAngles, memAngles.X);
            GameProcess.Write(_clientBase + Offsets.dwViewAngles + 4, memAngles.Y);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[SilentAim Write] {ex.Message}");
        }
    }

    private void RestoreAngles()
    {
        try
        {
            GameProcess!.Write(_clientBase + Offsets.dwViewAngles, _savedAngles.X);
            GameProcess.Write(_clientBase + Offsets.dwViewAngles + 4, _savedAngles.Y);
            _anglesModified = false;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[SilentAim Restore] {ex.Message}");
            _anglesModified = false;
        }
    }

    private void UpdateClientBase()
    {
        if (_clientBase == IntPtr.Zero || (DateTime.Now - _lastModuleCheck).TotalSeconds > 5)
        {
            foreach (System.Diagnostics.ProcessModule m in GameProcess!.Process.Modules)
                if (m.ModuleName == "client.dll") { _clientBase = m.BaseAddress; break; }
            _lastModuleCheck = DateTime.Now;
        }
    }

    private void UpdateScreen()
    {
        if (GameProcess?.Process?.MainWindowHandle != IntPtr.Zero &&
            GetClientRect(GameProcess.Process.MainWindowHandle, out RECT rect))
        {
            _centerX = (rect.Right - rect.Left) / 2;
            _centerY = (rect.Bottom - rect.Top) / 2;
        }
    }

    private void UpdateMatrix()
    {
        try
        {
            for (int i = 0; i < 16; i++)
            {
                _matrix[i] = GameProcess!.Process.Read<float>(_clientBase + Offsets.dwViewMatrix + (i * 4));
            }
        }
        catch { }
    }

    private bool WorldToScreen(Vector3 worldPos, out Vector2 screenPos)
    {
        screenPos = Vector2.Zero;
        if (_matrix.Length < 16) return false;

        float w = _matrix[12] * worldPos.X + _matrix[13] * worldPos.Y + _matrix[14] * worldPos.Z + _matrix[15];
        if (w < 0.001f) return false;

        float invW = 1f / w;
        float x = (_matrix[0] * worldPos.X + _matrix[1] * worldPos.Y + _matrix[2] * worldPos.Z + _matrix[3]) * invW;
        float y = (_matrix[4] * worldPos.X + _matrix[5] * worldPos.Y + _matrix[6] * worldPos.Z + _matrix[7]) * invW;

        screenPos = new Vector2(_centerX + x * _centerX, _centerY - y * _centerY);
        return true;
    }

    private Vector2 CalcAngle(Vector3 src, Vector3 dst)
    {
        float[] delta = { dst.X - src.X, dst.Y - src.Y, dst.Z - src.Z };
        float hyp = MathF.Sqrt(delta[0] * delta[0] + delta[1] * delta[1]);

        float yaw = MathF.Atan2(delta[1], delta[0]) * 180f / MathF.PI;
        float pitch = -MathF.Atan2(delta[2], hyp) * 180f / MathF.PI; // Минус для CS2

        return new Vector2(yaw, pitch);
    }

    private string GetBoneName(int index) => index switch
    {
        0 => "head",
        1 => "neck_0",
        2 => "spine_1",
        3 => "pelvis",
        _ => "head"
    };
}