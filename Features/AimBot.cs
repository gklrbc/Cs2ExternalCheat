using System.Drawing;
using System.Numerics;
using System.Runtime.InteropServices;
using CS2Cheat.Core;
using CS2Cheat.Core.Data;
using CS2Cheat.Data.Entity;
using CS2Cheat.Data.Game;
using CS2Cheat.Utils;

namespace CS2Cheat.Features;

public class AimBot : ThreadedServiceBase
{
    private const int AimUpdateIntervalMs = 1;

    private DateTime _lastTargetDeathTime = DateTime.MinValue;
    private IntPtr _currentTargetAddress = IntPtr.Zero;

    private int _screenCenterX;
    private int _screenCenterY;
    private DateTime _lastGeometryUpdate = DateTime.MinValue;

    private IntPtr _clientModuleBase = IntPtr.Zero;
    private DateTime _lastModuleCheck = DateTime.MinValue;

    private bool _hasFlickedInLastFrame = false;
    private int _lastFlickDeltaX = 0;
    private int _lastFlickDeltaY = 0;

    // Свойство для оверлея, чтобы рисовать ауру на цели
    public IntPtr LockedTargetAddress => _currentTargetAddress;

    [DllImport("user32.dll")]
    private static extern bool GetClientRect(IntPtr hWnd, out RECT lpRect);

    [DllImport("winmm.dll", EntryPoint = "timeBeginPeriod")]
    private static extern uint timeBeginPeriod(uint uPeriod);

    [DllImport("winmm.dll", EntryPoint = "timeEndPeriod")]
    private static extern uint timeEndPeriod(uint uPeriod);

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    public AimBot(GameProcess gameProcess, GameData gameData)
    {
        GameProcess = gameProcess;
        GameData = gameData;
        UpdateScreenCenter(true);

        timeBeginPeriod(1);
    }

    private static ConfigManager Config => ConfigManager.Load();
    protected override string ThreadName => nameof(AimBot);
    private GameProcess? GameProcess { get; set; }
    private GameData? GameData { get; set; }

    public override void Dispose()
    {
        timeEndPeriod(1);

        base.Dispose();
        GameData = null;
        GameProcess = null;
    }

    private void UpdateScreenCenter(bool force = false)
    {
        if (!force && (DateTime.Now - _lastGeometryUpdate).TotalSeconds < 3) return;

        if (GameProcess?.Process?.MainWindowHandle != IntPtr.Zero)
        {
            if (GetClientRect(GameProcess.Process.MainWindowHandle, out RECT rect))
            {
                int width = rect.Right - rect.Left;
                int height = rect.Bottom - rect.Top;

                if (width > 0 && height > 0)
                {
                    _screenCenterX = width / 2;
                    _screenCenterY = height / 2;
                    _lastGeometryUpdate = DateTime.Now;
                    return;
                }
            }
        }

        _screenCenterX = 1920 / 2;
        _screenCenterY = 1080 / 2;
    }

    private IntPtr GetClientModule()
    {
        if (_clientModuleBase != IntPtr.Zero && (DateTime.Now - _lastModuleCheck).TotalSeconds < 5)
            return _clientModuleBase;

        if (GameProcess?.Process == null) return IntPtr.Zero;

        try
        {
            foreach (System.Diagnostics.ProcessModule module in GameProcess.Process.Modules)
            {
                if (module.ModuleName == "client.dll")
                {
                    _clientModuleBase = module.BaseAddress;
                    _lastModuleCheck = DateTime.Now;
                    return _clientModuleBase;
                }
            }
        }
        catch { }

        return IntPtr.Zero;
    }

    protected override void FrameAction()
    {
        try
        {
            if (_hasFlickedInLastFrame)
            {
                Utility.MouseMove(-_lastFlickDeltaX, -_lastFlickDeltaY);
                _hasFlickedInLastFrame = false;
                _lastFlickDeltaX = 0;
                _lastFlickDeltaY = 0;
                Thread.Sleep(AimUpdateIntervalMs);
                return;
            }

            if (GameProcess == null || !GameProcess.IsValid || GameData?.Player == null || !GameData.Player.IsAlive())
            {
                ResetTarget();
                Thread.Sleep(5);
                return;
            }

            if (!Config.AimBot)
            {
                ResetTarget();
                Thread.Sleep(5);
                return;
            }

            var aimKey = Config.AimBotKey;
            if (!aimKey.IsKeyDown())
            {
                ResetTarget();
                Thread.Sleep(2);
                return;
            }

            var player = GameData.Player;

            try
            {
                float flashDuration = GameProcess.Process.Read<float>(player.AddressBase + 0x135C);
                if (flashDuration > 0.4f)
                {
                    ResetTarget();
                    Thread.Sleep(5);
                    return;
                }
            }
            catch { }

            if (player.IsGrenade())
            {
                ResetTarget();
                Thread.Sleep(5);
                return;
            }

            UpdateScreenCenter();

            IntPtr clientModule = GetClientModule();
            if (clientModule == IntPtr.Zero) return;

            Entity? target = GetBestTarget(out Vector3 targetWorldPos);

            if (target != null && targetWorldPos != Vector3.Zero)
            {
                if (WorldToScreen(targetWorldPos, out Vector2 screenPos))
                {
                    float deltaX = screenPos.X - _screenCenterX;
                    float deltaY = screenPos.Y - _screenCenterY;

                    int moveX;
                    int moveY;

                    // ===== RAGE MODE =====
                    if (!Config.AimLegitMode)
                    {
                        moveX = (int)Math.Round(deltaX);
                        moveY = (int)Math.Round(deltaY);

                        if (moveX != 0 || moveY != 0)
                        {
                            moveX = Math.Clamp(moveX, -500, 500);
                            moveY = Math.Clamp(moveY, -500, 500);
                            Utility.MouseMove(moveX, moveY);
                        }
                    }
                    else if (Config.AimSilentFlick)
                    {
                        moveX = (int)Math.Round(deltaX);
                        moveY = (int)Math.Round(deltaY);

                        if (moveX != 0 || moveY != 0)
                        {
                            Utility.MouseMove(moveX, moveY);
                            _lastFlickDeltaX = moveX;
                            _lastFlickDeltaY = moveY;
                            _hasFlickedInLastFrame = true;
                        }
                    }
                    // ===== LEGIT MODE =====
                    else
                    {
                        float smooth = Math.Clamp(Config.AimSmoothing, 0.0f, 100f);
                        if (smooth > 0f)
                        {
                            deltaX /= (smooth * 0.5f + 1f);
                            deltaY /= (smooth * 0.5f + 1f);
                        }

                        moveX = (int)Math.Round(deltaX);
                        moveY = (int)Math.Round(deltaY);

                        if (moveX != 0 || moveY != 0)
                        {
                            int maxStep = 60;
                            moveX = Math.Clamp(moveX, -maxStep, maxStep);
                            moveY = Math.Clamp(moveY, -maxStep, maxStep);

                            Utility.MouseMove(moveX, moveY);
                        }
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[AimBot Critical Frame Error] {ex.Message}");
        }

        Thread.Sleep(AimUpdateIntervalMs);
    }

    private void ResetTarget()
    {
        _currentTargetAddress = IntPtr.Zero;
    }

    private Entity? GetBestTarget(out Vector3 bestTargetPos)
    {
        bestTargetPos = Vector3.Zero;
        if (GameData == null || GameData.Player == null) return null;

        if ((DateTime.Now - _lastTargetDeathTime).TotalMilliseconds < 150)
            return null;

        Entity? bestTarget = null;
        float minPixelDistance = float.MaxValue;

        float aimFov = Config.AimFov > 0 ? Config.AimFov : 15f;
        float maxFovPixels = aimFov * 8.0f;

        string boneName = GetBoneNameByIndex(Config.AimBoneIndex);

        if (_currentTargetAddress != IntPtr.Zero)
        {
            var current = GameData.Entities.FirstOrDefault(e => e != null && e.AddressBase == _currentTargetAddress);
            if (current == null || !current.IsAlive() || (Config.AimLegitMode && !IsEntityVisibleToMe(current)))
            {
                ResetTarget();
            }
        }

        foreach (var entity in GameData.Entities)
        {
            if (entity == null || !entity.IsAlive() || entity.AddressBase == IntPtr.Zero) continue;
            if (Config.TeamCheck && entity.Team == GameData.Player.Team) continue;
            if (_currentTargetAddress != IntPtr.Zero && entity.AddressBase != _currentTargetAddress) continue;
            if (!entity.BonePos.TryGetValue(boneName, out var baseBonePos) || baseBonePos == Vector3.Zero) continue;

            float distance3D = Vector3.Distance(GameData.Player.EyePosition, baseBonePos);
            Vector3[] multipoints = GetMultipointsForBone(baseBonePos, distance3D);

            foreach (var point in multipoints)
            {
                if (Config.AimLegitMode && !IsEntityVisibleToMe(entity)) continue;

                Vector3 relativeVelocity = entity.Velocity - GameData.Player.Velocity;
                float predictionTime = Math.Clamp(distance3D / 15000f, 0.005f, 0.02f);
                Vector3 predictedPointPos = point + (relativeVelocity * predictionTime);

                if (WorldToScreen(predictedPointPos, out Vector2 screenPos))
                {
                    float pixelDistance = Vector2.Distance(screenPos, new Vector2(_screenCenterX, _screenCenterY));

                    if (pixelDistance < maxFovPixels && pixelDistance < minPixelDistance)
                    {
                        minPixelDistance = pixelDistance;
                        bestTargetPos = predictedPointPos;
                        bestTarget = entity;
                    }
                }
            }
        }

        if (bestTarget != null)
        {
            _currentTargetAddress = bestTarget.AddressBase;
        }
        else
        {
            ResetTarget();
        }

        return bestTarget;
    }

    private Vector3[] GetMultipointsForBone(Vector3 baseBone, float distance)
    {
        float scale = Math.Clamp(4.5f / (distance / 200f), 0.8f, 4.5f);

        return new Vector3[]
        {
            baseBone,
            new(baseBone.X + scale, baseBone.Y, baseBone.Z),
            new(baseBone.X - scale, baseBone.Y, baseBone.Z),
            new(baseBone.X, baseBone.Y + scale, baseBone.Z),
            new(baseBone.X, baseBone.Y, baseBone.Z + (scale * 0.7f))
        };
    }

    private string GetBoneNameByIndex(int index)
    {
        return index switch
        {
            0 => "head",
            1 => "neck",
            2 => "spine",
            3 => "pelvis",
            _ => "head"
        };
    }

    private bool IsEntityVisibleToMe(Entity entity)
    {
        if (GameProcess?.Process == null || entity.AddressBase == IntPtr.Zero) return false;
        try
        {
            IntPtr spottedStateAddr = entity.AddressBase + Offsets.m_entitySpottedState;
            return GameProcess.Process.Read<bool>(spottedStateAddr + 0x8);
        }
        catch
        {
            return false;
        }
    }

    private bool WorldToScreen(Vector3 worldPos, out Vector2 screenPos)
    {
        screenPos = Vector2.Zero;

        if (GameProcess?.Process == null) return false;

        IntPtr clientModule = GetClientModule();
        if (clientModule == IntPtr.Zero) return false;

        float[] viewMatrix = new float[16];
        for (int i = 0; i < 16; i++)
        {
            viewMatrix[i] = GameProcess.Process.Read<float>(clientModule + Offsets.dwViewMatrix + (i * sizeof(float)));
        }

        if (viewMatrix == null || viewMatrix.Length < 16) return false;

        float w = viewMatrix[12] * worldPos.X + viewMatrix[13] * worldPos.Y + viewMatrix[14] * worldPos.Z + viewMatrix[15];

        if (w < 0.01f) return false;

        float x = viewMatrix[0] * worldPos.X + viewMatrix[1] * worldPos.Y + viewMatrix[2] * worldPos.Z + viewMatrix[3];
        float y = viewMatrix[4] * worldPos.X + viewMatrix[5] * worldPos.Y + viewMatrix[6] * worldPos.Z + viewMatrix[7];

        float invw = 1.0f / w;
        x *= invw;
        y *= invw;

        int width = _screenCenterX * 2;
        int height = _screenCenterY * 2;

        float screenX = _screenCenterX + (0.5f * x * width + 0.5f);
        float screenY = _screenCenterY - (0.5f * y * height + 0.5f);

        screenPos = new Vector2(screenX, screenY);
        return true;
    }
}