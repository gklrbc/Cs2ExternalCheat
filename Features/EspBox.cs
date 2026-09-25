using System.Numerics;
using CS2Cheat.Core.Data;
using CS2Cheat.Data.Entity;
using CS2Cheat.Data.Game;
using CS2Cheat.Graphics;
using CS2Cheat.Utils;
using ImGuiNET;

namespace CS2Cheat.Features;

public static class EspBox
{
    private static readonly Dictionary<string, string> GunIcons = new(StringComparer.OrdinalIgnoreCase)
    {
        ["knife_ct"] = "]",
        ["knife_t"] = "[",
        ["deagle"] = "A",
        ["elite"] = "B",
        ["fiveseven"] = "C",
        ["glock"] = "D",
        ["revolver"] = "J",
        ["hkp2000"] = "E",
        ["p250"] = "F",
        ["usp_silencer"] = "G",
        ["tec9"] = "H",
        ["cz75a"] = "I",
        ["mac10"] = "K",
        ["ump45"] = "L",
        ["bizon"] = "M",
        ["mp7"] = "N",
        ["mp9"] = "R",
        ["p90"] = "O",
        ["galilar"] = "Q",
        ["famas"] = "R",
        ["m4a1_silencer"] = "T",
        ["m4a1"] = "S",
        ["aug"] = "U",
        ["sg556"] = "V",
        ["ak47"] = "W",
        ["g3sg1"] = "X",
        ["scar20"] = "Y",
        ["awp"] = "Z",
        ["ssg08"] = "a",
        ["xm1014"] = "b",
        ["sawedoff"] = "c",
        ["mag7"] = "d",
        ["nova"] = "e",
        ["negev"] = "f",
        ["m249"] = "g",
        ["taser"] = "h",
        ["flashbang"] = "i",
        ["hegrenade"] = "j",
        ["smokegrenade"] = "k",
        ["molotov"] = "l",
        ["decoy"] = "m",
        ["incgrenade"] = "n",
        ["c4"] = "o"
    };

    private static ConfigManager? _config;
    private static ConfigManager Config => _config ??= ConfigManager.Load();

    public static void Draw(ImDrawListPtr drawList, GameData gameData, GameProcess gameProcess)
    {
        var player = gameData?.Player;
        if (player == null || gameData?.Entities == null) return;

        bool teamCheck = Config.TeamCheck;
        bool visibleCheck = Config.EspVisibleCheck;

        var rawColor = Config.EspBoxColor;
        uint defaultBoxColor = OverlayRenderer.ToColor(
            new Vector4(rawColor[0], rawColor[1], rawColor[2], rawColor[3])
        );

        foreach (var entity in gameData.Entities)
        {
            if (!IsValidEntity(entity, player)) continue;
            if (teamCheck && entity.Team == player.Team) continue;

            // �������� ���� �� ������ ������ (head + pelvis) � �� ����������� �� �������� ���
            var boundingBox = GetStableBoundingBox(player, entity);
            if (!boundingBox.HasValue) continue;

            // �������� ���������
            uint color = defaultBoxColor;
            if (visibleCheck)
            {
                bool isVisible = IsEntityVisible(gameProcess, entity);
                color = isVisible
                    ? OverlayRenderer.ToColor(46, 204, 113)   // ������� � �����
                    : OverlayRenderer.ToColor(231, 76, 60);    // ������� � �� �����
            }

            int health = GetEntityHealth(gameProcess, entity);
            if (health <= 0) continue;

            // ������������ ��������� ��� ��������������� ������� �����
            float distance = GetDistance(player, entity);

            DrawBoxWithInfo(drawList, entity, color, boundingBox.Value, health, distance);
        }
    }

    private static bool IsValidEntity(Entity entity, Player player)
    {
        return entity.IsAlive() &&
               entity.AddressBase != player.AddressBase &&
               entity.BonePos != null &&
               entity.BonePos.Count > 0;
    }

    private static (Vector2 TopLeft, Vector2 BottomRight)? GetStableBoundingBox(Player player, Entity entity)
    {
        var bones = entity.BonePos;
        var matrix = player.MatrixViewProjectionViewport;

        if (bones == null || bones.Count == 0) return null;

        var legBones = new HashSet<string> { "leg_upper_L", "leg_lower_L", "ankle_L",
                                         "leg_upper_R", "leg_lower_R", "ankle_R" };

        float minX = float.MaxValue, maxX = float.MinValue;
        float minY = float.MaxValue, maxY = float.MinValue;
        bool hasAnyBone = false;

        foreach (var kvp in bones)
        {
            if (legBones.Contains(kvp.Key)) continue;

            var screen = matrix.Transform(kvp.Value);

            // ��������� ��� ����� ����� ������� (0 < Z < 1)
            if (screen.Z < 0 || screen.Z > 1) continue;

            minX = Math.Min(minX, screen.X);
            maxX = Math.Max(maxX, screen.X);
            minY = Math.Min(minY, screen.Y);
            maxY = Math.Max(maxY, screen.Y);
            hasAnyBone = true;
        }

        if (!hasAnyBone) return null;

        // �����������: ���� �� ������ 300x400 �������� (������ �� ������������)
        float width = maxX - minX;
        float height = maxY - minY;

        if (width > 300 || height > 400) return null; // ������� ������� � ����������

        // ��� clamp:
        width = Math.Min(width, 250);
        height = Math.Min(height, 350);

        // ���������� ������������ ������ � ���� ���� clamp ��������
        float centerX = (minX + maxX) / 2;
        float centerY = (minY + maxY) / 2;

        minX = centerX - width / 2;
        maxX = centerX + width / 2;
        minY = centerY - height / 2;
        maxY = centerY + height / 2;

        // ������������� �������� ��� ���
        float legOffset = height * 1.1f;

        Vector2 topLeft = new(minX, minY);
        Vector2 bottomRight = new(maxX, maxY + legOffset);

        return (topLeft, bottomRight);
    }

    private static void DrawBoxWithInfo(ImDrawListPtr drawList, Entity entity, uint color,
        (Vector2 TopLeft, Vector2 BottomRight) box, int health, float distance)
    {
        var (tl, br) = box;
        float width = br.X - tl.X;
        float height = br.Y - tl.Y;

        // ��������������� ������� ����� � ����������� �� ����������
        float thickness = Math.Clamp(3f - distance / 500f, 1f, 3f);
        float cornerLen = Math.Min(width, height) * 0.2f;

        // ������� (�����������)
        if (Config.EspFilledBox)
        {
            uint fillColor = (color & 0x00FFFFFF) | (25u << 24); // ��������� �����
            drawList.AddRectFilled(tl, br, fillColor, 4f);
        }

        // ������� ���� (�� ����������� ��� ��������)
        DrawCornerBox(drawList, tl, br, color, cornerLen, thickness);

        // Health bar �����
        DrawHealthBar(drawList, tl, br, health);

        // ���� ��� ������
        float centerX = (tl.X + br.X) / 2f;
        float currentY = br.Y + 5f;

        // ������
        if (Config.EspWeapon && !string.IsNullOrEmpty(entity.CurrentWeaponName))
        {
            string weaponText = GetWeaponDisplayText(entity.CurrentWeaponName);
            var size = ImGui.CalcTextSize(weaponText);
            DrawTextOutlined(drawList, new Vector2(centerX - size.X / 2f, currentY),
                OverlayRenderer.Colors.White, weaponText);
            currentY += size.Y + 2f;
        }

        // ���
        if (Config.EspName)
        {
            string name = entity.Name ?? "Player";
            var size = ImGui.CalcTextSize(name);
            DrawTextOutlined(drawList, new Vector2(centerX - size.X / 2f, tl.Y - size.Y - 2f),
                OverlayRenderer.Colors.White, name);
        }

        // ����� ������
        if (Config.EspFlags)
        {
            float flagX = br.X + 5f;
            float flagY = tl.Y;

            if (entity.IsInScope == 1)
            {
                DrawTextOutlined(drawList, new Vector2(flagX, flagY),
                    OverlayRenderer.ToColor(0, 168, 255), "SCOPED");
                flagY += 12f;
            }
            if (entity.FlashAlpha > 7)
            {
                DrawTextOutlined(drawList, new Vector2(flagX, flagY),
                    OverlayRenderer.ToColor(255, 200, 0), "FLASHED");
            }
        }
    }

    private static void DrawCornerBox(ImDrawListPtr dl, Vector2 tl, Vector2 br, uint color,
        float len, float thickness)
    {
        uint outline = OverlayRenderer.ToColor(0, 0, 0, 200);
        float outThick = thickness + 1.5f;

        void DrawLine(Vector2 a, Vector2 b)
        {
            dl.AddLine(a, b, outline, outThick);
            dl.AddLine(a, b, color, thickness);
        }

        // Top left
        DrawLine(new Vector2(tl.X, tl.Y + len), tl);
        DrawLine(tl, new Vector2(tl.X + len, tl.Y));

        // Top right  
        DrawLine(new Vector2(br.X - len, tl.Y), new Vector2(br.X, tl.Y));
        DrawLine(new Vector2(br.X, tl.Y), new Vector2(br.X, tl.Y + len));

        // Bottom left
        DrawLine(new Vector2(tl.X, br.Y - len), new Vector2(tl.X, br.Y));
        DrawLine(new Vector2(tl.X, br.Y), new Vector2(tl.X + len, br.Y));

        // Bottom right
        DrawLine(new Vector2(br.X - len, br.Y), br);
        DrawLine(br, new Vector2(br.X, br.Y - len));
    }

    private static void DrawHealthBar(ImDrawListPtr dl, Vector2 tl, Vector2 br, int health)
    {
        float pct = Math.Clamp(health / 100f, 0f, 1f);
        float width = 3f;
        float offset = 4f;

        Vector2 barTl = new(tl.X - offset - width, tl.Y);
        Vector2 barBr = new(tl.X - offset, br.Y);

        // ���
        dl.AddRectFilled(barTl - Vector2.One, barBr + Vector2.One,
            OverlayRenderer.ToColor(0, 0, 0, 200));

        // ����������
        float h = barBr.Y - barTl.Y;
        float fillH = h * pct;
        Vector2 fillTl = new(barTl.X, barBr.Y - fillH);

        uint col = GetHealthColor(pct);
        dl.AddRectFilled(fillTl, barBr, col);

        // ����� �� ���� < 100
        if (health < 100)
        {
            string txt = health.ToString();
            var sz = ImGui.CalcTextSize(txt);
            dl.AddText(new Vector2(barTl.X - sz.X - 2, fillTl.Y - sz.Y / 2),
                OverlayRenderer.Colors.White, txt);
        }
    }

    private static uint GetHealthColor(float pct)
    {
        byte r = (byte)(pct < 0.5f ? 255 : 255 * (1 - pct) * 2);
        byte g = (byte)(pct > 0.5f ? 220 : 220 * pct * 2);
        return OverlayRenderer.ToColor(r, g, 50);
    }

    private static string GetWeaponDisplayText(string weapon)
    {
        if (string.IsNullOrEmpty(weapon)) return "?";
        string clean = weapon.ToLower().Replace("weapon_", "");

        if (GunIcons.TryGetValue(clean, out var icon))
            return icon;

        return clean.Replace("silencer", "-S").ToUpper();
    }

    private static void DrawTextOutlined(ImDrawListPtr dl, Vector2 pos, uint col, string text)
    {
        uint black = OverlayRenderer.ToColor(0, 0, 0, 220);
        dl.AddText(pos + new Vector2(1, 0), black, text);
        dl.AddText(pos + new Vector2(-1, 0), black, text);
        dl.AddText(pos + new Vector2(0, 1), black, text);
        dl.AddText(pos + new Vector2(0, -1), black, text);
        dl.AddText(pos, col, text);
    }

    private static int GetEntityHealth(GameProcess gameProcess, Entity entity)
    {
        try
        {
            if (gameProcess?.Process == null) return 0;
            return gameProcess.Process.Read<int>(entity.AddressBase + Offsets.m_iHealth);
        }
        catch { return 0; }
    }

    private static bool IsEntityVisible(GameProcess gameProcess, Entity entity)
    {
        try
        {
            if (gameProcess?.Process == null) return false;
            IntPtr addr = entity.AddressBase + Offsets.m_entitySpottedState;
            return gameProcess.Process.Read<bool>(addr + 0x8);
        }
        catch { return false; }
    }

    private static float GetDistance(Player player, Entity entity)
    {
        // ������� �������� ����� Z-���������� ������ ��� ���������������
        if (player.MatrixViewProjectionViewport == null) return 0;

        if (entity.BonePos?.TryGetValue("head", out var head) == true)
        {
            var screen = player.MatrixViewProjectionViewport.Transform(head);
            // ��������������� ��������� ����� W ����������
            return screen.Z > 0 ? screen.Z : 0;
        }
        return 0;
    }
}