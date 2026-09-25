using System;
using System.Collections.Generic;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Text;
using CS2Cheat.Core;
using CS2Cheat.Data.Game;
using CS2Cheat.Utils;

namespace CS2Cheat.Features;

public class GrenadeTracker
{
    public enum GrenadeType { Smoke, Flash, HE, Molotov, Decoy, Unknown }

    public struct TrackedGrenade
    {
        public Vector3 Position;
        public GrenadeType Type;
        public IntPtr EntityPtr;
    }

    public List<TrackedGrenade> ActiveGrenades { get; } = new();

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool ReadProcessMemory(IntPtr hProcess, IntPtr lpBaseAddress, [Out] byte[] lpBuffer, int dwSize, out int lpNumberOfBytesRead);

    public void Update(GameProcess gameProcess, GameData gameData)
    {
        ActiveGrenades.Clear();

        if (gameProcess?.ModuleClient == null || gameProcess?.Process == null) return;

        try
        {
            IntPtr entityList = gameProcess.ModuleClient.Read<IntPtr>(Offsets.dwEntityList);
            if (entityList == IntPtr.Zero) return;

            IntPtr hProcess = gameProcess.Process.Handle;

            // Стандартный оффсет m_designerName в CEntityInstance
            int designerNameOffset = Offsets.m_designerName != 0 ? Offsets.m_designerName : 0x20;

            for (int i = 0; i < 512; i++)
            {
                int chunkIndex = i >> 9;
                int entityIndex = i & 0x1FF;

                IntPtr listEntry = gameProcess.Process.Read<IntPtr>(entityList + 8 * chunkIndex + 16);
                if (listEntry == IntPtr.Zero) continue;

                // ИСПРАВЛЕНО: Шаг 120 (0x78), а не 112
                IntPtr entity = gameProcess.Process.Read<IntPtr>(listEntry + 120 * entityIndex);
                if (entity == IntPtr.Zero) continue;

                IntPtr namePtr = gameProcess.Process.Read<IntPtr>(entity + designerNameOffset);
                if (namePtr == IntPtr.Zero) continue;

                byte[] buffer = new byte[64];
                ReadProcessMemory(hProcess, namePtr, buffer, 64, out _);

                int length = 0;
                for (int j = 0; j < buffer.Length; j++)
                {
                    if (buffer[j] == 0) { length = j; break; }
                }

                if (length == 0) continue;

                string name = Encoding.UTF8.GetString(buffer, 0, length);
                string lowerName = name.ToLower();

                if (!lowerName.Contains("projectile")) continue;

                GrenadeType type = GrenadeType.Unknown;
                if (lowerName.Contains("smoke")) type = GrenadeType.Smoke;
                else if (lowerName.Contains("flash")) type = GrenadeType.Flash;
                else if (lowerName.Contains("hegrenade")) type = GrenadeType.HE;
                else if (lowerName.Contains("molotov") || lowerName.Contains("incgrenade")) type = GrenadeType.Molotov;
                else if (lowerName.Contains("decoy")) type = GrenadeType.Decoy;

                if (type == GrenadeType.Unknown) continue;

                // ИСПРАВЛЕНО: Читаем позицию через CGameSceneNode, как у игроков
                Vector3 pos = Vector3.Zero;
                try
                {
                    IntPtr gameSceneNode = gameProcess.Process.Read<IntPtr>(entity + Offsets.m_pGameSceneNode);
                    if (gameSceneNode != IntPtr.Zero)
                    {
                        pos = gameProcess.Process.Read<Vector3>(gameSceneNode + Offsets.m_vOldOrigin);
                    }
                }
                catch { }

                if (pos != Vector3.Zero)
                {
                    ActiveGrenades.Add(new TrackedGrenade
                    {
                        Position = pos,
                        Type = type,
                        EntityPtr = entity
                    });
                }
            }
        }
        catch { }
    }
}