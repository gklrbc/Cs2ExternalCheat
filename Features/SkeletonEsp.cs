using System.Numerics;
using CS2Cheat.Core.Data;
using CS2Cheat.Data.Entity;
using CS2Cheat.Data.Game;
using CS2Cheat.Graphics;
using ImGuiNET;

namespace CS2Cheat.Features;

public static class SkeletonEsp
{
    private static readonly (string Start, string End)[] BoneConnections =
    [
        ("head", "neck_0"), ("neck_0", "spine_1"), ("spine_1", "spine_2"), ("spine_2", "pelvis"),
        ("spine_1", "arm_upper_L"), ("arm_upper_L", "arm_lower_L"), ("arm_lower_L", "hand_L"),
        ("spine_1", "arm_upper_R"), ("arm_upper_R", "arm_lower_R"), ("arm_lower_R", "hand_R"),
        ("pelvis", "leg_upper_L"), ("leg_upper_L", "leg_lower_L"), ("leg_lower_L", "ankle_L"),
        ("pelvis", "leg_upper_R"), ("leg_upper_R", "leg_lower_R"), ("leg_lower_R", "ankle_R")
    ];

    public static void Draw(ImDrawListPtr drawList, GameData gameData)
    {
        var player = gameData.Player;
        if (player == null || gameData.Entities == null) return;

        foreach (var entity in gameData.Entities)
        {
            if (!IsValidEntity(entity, player)) continue;

            var color = GetTeamColor(entity.Team);
            DrawSkeleton(drawList, player, entity, color);
        }
    }

    private static bool IsValidEntity(Entity entity, Player player)
    {
        return entity.IsAlive() && entity.AddressBase != player.AddressBase;
    }

    private static uint GetTeamColor(Team team)
    {
        // Премиальные неоновые оттенки команд (вместо стандартных вырвиглазных)
        return team == Team.Terrorists
            ? OverlayRenderer.ToColor(255, 179, 0)   // Мягкий золотистый колер
            : OverlayRenderer.ToColor(0, 168, 255);  // Яркий неоновый синий
    }

    private static void DrawSkeleton(ImDrawListPtr drawList, Player player, Entity entity, uint color)
    {
        var bonePositions = entity.BonePos;
        if (bonePositions == null) return;

        var matrix = player.MatrixViewProjectionViewport;
        uint blackOutline = OverlayRenderer.ToColor(0, 0, 0, 180);

        // Список отрисованных суставов, чтобы не рисовать кружки дважды на одном месте
        HashSet<Vector2> drawnJoints = [];

        foreach (var (startBone, endBone) in BoneConnections)
        {
            if (!bonePositions.TryGetValue(startBone, out var startWorld) ||
                !bonePositions.TryGetValue(endBone, out var endWorld))
                continue;

            var startScreen = matrix.Transform(startWorld);
            var endScreen = matrix.Transform(endWorld);

            if (startScreen.Z >= 1 || endScreen.Z >= 1) continue;

            Vector2 p1 = new Vector2(startScreen.X, startScreen.Y);
            Vector2 p2 = new Vector2(endScreen.X, endScreen.Y);

            // 1. Отрисовка темного аутлайна кости для контраста
            drawList.AddLine(p1, p2, blackOutline, 3.0f);

            // 2. Основная кость
            drawList.AddLine(p1, p2, color, 1.2f);

            // Добавляем суставы (точки соединения)
            drawnJoints.Add(p1);
            drawnJoints.Add(p2);
        }

        // КРАСИВЫЙ СУСТАВНОЙ ПАК (Joint Dots)
        // На месте каждого соединения костей рисуется маленькая аккуратная точка
        foreach (var joint in drawnJoints)
        {
            drawList.AddCircleFilled(joint, 2.2f, blackOutline);
            drawList.AddCircleFilled(joint, 1.0f, OverlayRenderer.Colors.WhiteSmoke);
        }
    }
}