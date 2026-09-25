using System;
using System.Collections.Generic;
using System.Drawing;
using System.Numerics;
using System.Runtime.InteropServices;
using ClickableTransparentOverlay;
using CS2Cheat.Core;
using CS2Cheat.Core.Data;
using CS2Cheat.Data.Entity;
using CS2Cheat.Data.Game;
using CS2Cheat.Features;
using CS2Cheat.Utils;
using ImGuiNET;
using Keys = Process.NET.Native.Types.Keys;

namespace CS2Cheat.Graphics;

public static class VectorExtensions
{
    public static uint ToUint(this Vector4 color) => ImGui.ColorConvertFloat4ToU32(color);
}

public class OverlayRenderer : Overlay
{
    private readonly GameProcess _gameProcess;
    private readonly GameData _gameData;
    private ConfigManager _config;
    private bool _showMenu, _menuKeyWasDown, _styleApplied;
    private int _activeTab;
    private string? _waitingForBind;
    private bool _clearBindKeyRelease;
    private readonly BombTimer _bombTimer;
    private readonly VoteTeller _voteTeller;
    private readonly AimBot _aimBot;
    private readonly TriggerBot _triggerBot;
    private readonly SilentAim _silentAim;
    private readonly BulletTracers _bulletTracers;
    private readonly SoundEsp _soundEsp;
    private readonly GrenadeTracker _grenadeTracker;
    private readonly Bhop _bhop;
    private readonly Rcs _rcs;
    private float _menuAlpha;
    private DateTime _lastToggleTime = DateTime.MinValue;

    private float[] _viewMatrix = new float[16];
    private IntPtr _clientBase = IntPtr.Zero;
    private DateTime _lastModuleCheck = DateTime.MinValue;

    private bool _speedDragging = false;
    private bool _statusDragging = false;
    private bool _keybindDragging = false;
    private readonly List<float> _speedHistory = new(new float[80]);

    static readonly Vector4 ColBg = new(0.08f, 0.09f, 0.11f, 0.98f);
    static readonly Vector4 ColSidebar = new(0.05f, 0.06f, 0.07f, 1.00f);
    static readonly Vector4 ColAccent = new(0.00f, 0.55f, 1.00f, 1.00f);
    static readonly Vector4 ColAccentDim = new(0.00f, 0.42f, 0.80f, 1.00f);
    static readonly Vector4 ColText = new(0.94f, 0.95f, 0.97f, 1.00f);
    static readonly Vector4 ColTextDim = new(0.50f, 0.52f, 0.58f, 1.00f);
    static readonly Vector4 ColItem = new(0.12f, 0.13f, 0.17f, 1.00f);
    static readonly Vector4 ColItemHover = new(0.18f, 0.20f, 0.25f, 1.00f);
    static readonly Vector4 ColItemActive = new(0.10f, 0.11f, 0.14f, 1.00f);
    static readonly Vector4 ColBorder = new(0.20f, 0.22f, 0.27f, 0.35f);
    static readonly Vector4 ColSuccess = new(0.20f, 0.85f, 0.40f, 1.00f);

    static readonly string[] TabNames = { "Aim", "Visual", "Misc", "Config" };
    static readonly string[] AlignModes = { "Free", "Top Center", "Bottom Center" };

    public OverlayRenderer(GameProcess gp, GameData gd) : base(true)
    {
        _gameProcess = gp; _gameData = gd;
        _config = ConfigManager.Load();
        _bombTimer = new BombTimer(gp);
        _voteTeller = new VoteTeller(gp);
        _aimBot = new AimBot(gp, gd);
        _triggerBot = new TriggerBot(gp, gd);
        _silentAim = new SilentAim(gp, gd);
        _bulletTracers = new BulletTracers();
        _soundEsp = new SoundEsp();
        _grenadeTracker = new GrenadeTracker();
        _bhop = new Bhop(gp, gd);
        _rcs = new Rcs(gp, gd);
    }

    protected override Task PostInitialized()
    {
        var h = this.window.Handle;
        var ex = User32.GetWindowLong(h, User32.GWL_EXSTYLE);
        User32.SetWindowLong(h, User32.GWL_EXSTYLE, ex | User32.WS_EX_NOACTIVATE | User32.WS_EX_TOOLWINDOW);
        UpdateOverlayGeometry();
        return Task.CompletedTask;
    }

    private void UpdateOverlayGeometry()
    {
        var r = _gameProcess.WindowRectangleClient;
        if (r.Width <= 0 || r.Height <= 0) return;
        try
        {
            var ts = new System.Drawing.Size(r.Width, r.Height);
            var tp = new System.Drawing.Point(r.X, r.Y);
            if (this.Size != ts) this.Size = ts;
            if (this.Position != tp) this.Position = tp;
        }
        catch { }
    }

    protected override void Render()
    {
        UpdateOverlayGeometry();
        if (!_gameProcess.IsValid) return;

        var mk = _config.MenuToggleKey;
        var mkd = mk.IsKeyDown();
        if (mkd && !_menuKeyWasDown && (DateTime.Now - _lastToggleTime).TotalMilliseconds > 200)
        {
            _showMenu = !_showMenu;
            _lastToggleTime = DateTime.Now;
        }
        _menuKeyWasDown = mkd;

        if (_config.AimLegitToggleKey != Keys.None && _config.AimLegitToggleKey.IsKeyDown())
        {
            if (_waitingForBind == null && (DateTime.Now - _lastToggleTime).TotalMilliseconds > 300)
            {
                _config.AimLegitMode = !_config.AimLegitMode;
                ConfigManager.UpdateCache(_config);
                _lastToggleTime = DateTime.Now;
            }
        }

        _menuAlpha = _showMenu ? Math.Min(_menuAlpha + 0.12f, 1f) : Math.Max(_menuAlpha - 0.12f, 0f);

        if (_menuAlpha > 0.01f)
        {
            var io = ImGui.GetIO();
            ImGui.SetNextWindowPos(Vector2.Zero);
            ImGui.SetNextWindowSize(io.DisplaySize);
            ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, Vector2.Zero);
            ImGui.PushStyleColor(ImGuiCol.WindowBg, new Vector4(0f, 0f, 0f, _menuAlpha * 0.45f));
            ImGui.Begin("##background_dim", ImGuiWindowFlags.NoTitleBar | ImGuiWindowFlags.NoResize |
                ImGuiWindowFlags.NoMove | ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoBringToFrontOnFocus |
                ImGuiWindowFlags.NoFocusOnAppearing | ImGuiWindowFlags.NoNav);
            ImGui.End();
            ImGui.PopStyleColor();
            ImGui.PopStyleVar();
            RenderMenu();
        }

        var dl = ImGui.GetBackgroundDrawList();
        UpdateMatrix();
        RenderVisuals(dl);
        RenderOffscreenArrows(dl);
        RenderAimAura(dl);

        var fdl = ImGui.GetForegroundDrawList();
        RenderSpeedOverlay(fdl);
        RenderStatusPanel(fdl);
        RenderModeOverlay(fdl);
        RenderKeybindOverlay();
    }

    void ApplyStyle()
    {
        if (_styleApplied) return;
        var s = ImGui.GetStyle();
        s.WindowRounding = 10f; s.ChildRounding = 6f; s.FrameRounding = 5f; s.GrabRounding = 5f; s.PopupRounding = 6f;
        s.WindowBorderSize = 1f; s.ChildBorderSize = 0f; s.FrameBorderSize = 0f;
        s.WindowPadding = new Vector2(18, 18); s.FramePadding = new Vector2(10, 6); s.ItemSpacing = new Vector2(12, 11);

        s.Colors[(int)ImGuiCol.WindowBg] = ColBg;
        s.Colors[(int)ImGuiCol.Border] = ColBorder;
        s.Colors[(int)ImGuiCol.Text] = ColText;
        s.Colors[(int)ImGuiCol.CheckMark] = _config.OverlayAccentColor;
        s.Colors[(int)ImGuiCol.FrameBg] = ColItem;
        s.Colors[(int)ImGuiCol.FrameBgHovered] = ColItemHover;
        s.Colors[(int)ImGuiCol.FrameBgActive] = ColItemActive;
        s.Colors[(int)ImGuiCol.SliderGrab] = _config.OverlayAccentColor;
        s.Colors[(int)ImGuiCol.SliderGrabActive] = ColAccentDim;
        s.Colors[(int)ImGuiCol.Button] = ColItem;
        s.Colors[(int)ImGuiCol.ButtonHovered] = ColItemHover;
        s.Colors[(int)ImGuiCol.ButtonActive] = ColItemActive;
        s.Colors[(int)ImGuiCol.Header] = ColItem;
        s.Colors[(int)ImGuiCol.HeaderHovered] = ColItemHover;
        s.Colors[(int)ImGuiCol.HeaderActive] = ColItemActive;
        _styleApplied = true;
    }

    void RenderMenu()
    {
        ApplyStyle();
        ImGui.PushStyleVar(ImGuiStyleVar.Alpha, _menuAlpha);

        var menuSize = new Vector2(640, 580);
        var io = ImGui.GetIO();
        var pos = (io.DisplaySize - menuSize) * 0.5f;
        ImGui.SetNextWindowPos(pos, ImGuiCond.Always);
        ImGui.SetNextWindowSize(menuSize, ImGuiCond.Always);

        ImGui.Begin("##main", ImGuiWindowFlags.NoTitleBar | ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoMove | ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoCollapse);

        ImGui.PushStyleColor(ImGuiCol.ChildBg, ColSidebar);
        ImGui.BeginChild("##sidebar", new Vector2(150, 0), ImGuiChildFlags.None, ImGuiWindowFlags.NoScrollbar);
        ImGui.SetCursorPos(new Vector2(16, 22));
        ImGui.PushStyleColor(ImGuiCol.Text, _config.OverlayAccentColor);
        ImGui.Text("CS2 CHEAT");
        ImGui.PopStyleColor();
        ImGui.SetCursorPosY(52); ImGui.Separator(); ImGui.SetCursorPosY(68);

        for (int i = 0; i < TabNames.Length; i++)
        {
            ImGui.SetCursorPosX(10);
            bool sel = _activeTab == i;
            if (sel) ImGui.PushStyleColor(ImGuiCol.Button, ColItemHover);
            else ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(0, 0, 0, 0));
            ImGui.PushStyleVar(ImGuiStyleVar.FrameRounding, 5f);
            if (ImGui.Button(TabNames[i], new Vector2(130, 34))) _activeTab = i;
            ImGui.PopStyleVar(); ImGui.PopStyleColor();
        }

        ImGui.SetCursorPosY(ImGui.GetWindowHeight() - 45); ImGui.Separator(); ImGui.SetCursorPosX(16);
        ImGui.TextColored(ColTextDim, $"{_gameProcess.WindowRectangleClient.Width}x{_gameProcess.WindowRectangleClient.Height}");
        ImGui.EndChild(); ImGui.PopStyleColor();

        ImGui.SameLine();
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(22, 22));
        ImGui.BeginChild("##content", Vector2.Zero, ImGuiChildFlags.None, ImGuiWindowFlags.NoScrollbar);

        switch (_activeTab)
        {
            case 0: TabAimbot(); break;
            case 1: TabVisuals(); break;
            case 2: TabMisc(); break;
            case 3: TabConfig(); break;
        }

        ImGui.EndChild(); ImGui.PopStyleVar(); ImGui.End(); ImGui.PopStyleVar();
    }

    void TabAimbot()
    {
        SectionHeader("Aimbot Settings");
        var aimBot = _config.AimBot; if (Toggle("Enable System", ref aimBot)) { _config.AimBot = aimBot; ConfigManager.UpdateCache(_config); }
        var legitMode = _config.AimLegitMode; if (Toggle("Visibility Check Only (Legit)", ref legitMode)) { _config.AimLegitMode = legitMode; ConfigManager.UpdateCache(_config); }
        var fovCircle = _config.AimFovCircle; if (Toggle("Draw FOV Range Indicator", ref fovCircle)) { _config.AimFovCircle = fovCircle; ConfigManager.UpdateCache(_config); }
        var multipoint = _config.AimMultipoint; if (Toggle("Multipoint Aim", ref multipoint)) { _config.AimMultipoint = multipoint; ConfigManager.UpdateCache(_config); }
        var humanize = _config.AimHumanize; if (Toggle("Humanization", ref humanize)) { _config.AimHumanize = humanize; ConfigManager.UpdateCache(_config); }
        var backtrack = _config.AimBacktrack; if (Toggle("Backtracking", ref backtrack)) { _config.AimBacktrack = backtrack; ConfigManager.UpdateCache(_config); }

        ImGui.Spacing();
        var fov = _config.AimFov; ImGui.SetNextItemWidth(260); if (ImGui.SliderFloat("Field of View Radius", ref fov, 1f, 180f, "%.1f deg")) { _config.AimFov = fov; ConfigManager.UpdateCache(_config); }
        var smooth = _config.AimSmoothing; ImGui.SetNextItemWidth(260); if (ImGui.SliderFloat("Target Locking Smooth", ref smooth, 0f, 20f, "%.1f")) { _config.AimSmoothing = smooth; ConfigManager.UpdateCache(_config); }
        var bone = _config.AimBoneIndex; ImGui.SetNextItemWidth(260); if (ImGui.Combo("Target Hitbox Bone", ref bone, new[] { "Head", "Neck", "Chest", "Pelvis" }, 4)) { _config.AimBoneIndex = bone; ConfigManager.UpdateCache(_config); }

        ImGui.Spacing(); SectionHeader("Recoil Control");
        var rcsEnabled = GetConfigBool("RcsEnabled"); if (Toggle("Enable RCS", ref rcsEnabled)) { SetConfigBool("RcsEnabled", rcsEnabled); ConfigManager.UpdateCache(_config); }
        var rcsVerticalOnly = GetConfigBool("RcsVerticalOnly"); if (Toggle("Vertical Only", ref rcsVerticalOnly)) { SetConfigBool("RcsVerticalOnly", rcsVerticalOnly); ConfigManager.UpdateCache(_config); }
        var rcsSmooth = GetConfigFloat("RcsSmoothness", 1.5f); ImGui.SetNextItemWidth(260); if (ImGui.SliderFloat("RCS Smoothness", ref rcsSmooth, 1f, 10f, "%.1f")) { SetConfigFloat("RcsSmoothness", rcsSmooth); ConfigManager.UpdateCache(_config); }
        var rcsDelay = GetConfigFloat("RcsStartDelay", 50f); ImGui.SetNextItemWidth(260); if (ImGui.SliderFloat("RCS Start Delay (ms)", ref rcsDelay, 0f, 300f, "%.0f")) { SetConfigFloat("RcsStartDelay", rcsDelay); ConfigManager.UpdateCache(_config); }

        ImGui.Spacing(); SectionHeader("Silent Aim");
        var silentAim = GetConfigBool("SilentAim"); if (Toggle("Enable Silent Aim", ref silentAim)) { SetConfigBool("SilentAim", silentAim); ConfigManager.UpdateCache(_config); }
        var silentFov = GetConfigFloat("SilentAimFov", 5f); ImGui.SetNextItemWidth(260); if (ImGui.SliderFloat("Silent Aim FOV", ref silentFov, 0.5f, 30f, "%.1f deg")) { SetConfigFloat("SilentAimFov", silentFov); ConfigManager.UpdateCache(_config); }
        var silentBone = GetConfigInt("SilentAimBoneIndex", 0); ImGui.SetNextItemWidth(260); if (ImGui.Combo("Silent Aim Bone", ref silentBone, new[] { "Head", "Neck", "Chest", "Pelvis" }, 4)) { SetConfigInt("SilentAimBoneIndex", silentBone); ConfigManager.UpdateCache(_config); }
        DrawKeyBind("Silent Aim Key", "SilentAimKey", GetConfigKey("SilentAimKey", Keys.LButton));

        ImGui.Spacing(); SectionHeader("Keybinds");
        DrawKeyBind("Aimbot Hold Lock Key", "AimBotKey", _config.AimBotKey);
        DrawKeyBind("Toggle Legit/Rage State", "AimLegitToggleKey", _config.AimLegitToggleKey);
    }

    void TabVisuals()
    {
        SectionHeader("ESP Settings");
        var espBox = _config.EspBox; if (Toggle("Draw Box Matrix", ref espBox)) { _config.EspBox = espBox; ConfigManager.UpdateCache(_config); }
        var espName = _config.EspName; if (Toggle("Render Text Names", ref espName)) { _config.EspName = espName; ConfigManager.UpdateCache(_config); }
        var espWeapon = _config.EspWeapon; if (Toggle("Render Active Weapon", ref espWeapon)) { _config.EspWeapon = espWeapon; ConfigManager.UpdateCache(_config); }
        var espFlags = _config.EspFlags; if (Toggle("Player Flags (Scoped/Flashed)", ref espFlags)) { _config.EspFlags = espFlags; ConfigManager.UpdateCache(_config); }
        var espVisCheck = _config.EspVisibleCheck; if (Toggle("Visibility Color Check", ref espVisCheck)) { _config.EspVisibleCheck = espVisCheck; ConfigManager.UpdateCache(_config); }

        ImGui.Spacing(); SectionHeader("Visual Effects");
        var hitmarker = _config.Hitmarker; if (Toggle("Hitmarker", ref hitmarker)) { _config.Hitmarker = hitmarker; ConfigManager.UpdateCache(_config); }
        if (_config.Hitmarker)
        {
            var hmColor = _config.HitmarkerColor; if (ImGui.ColorEdit4("HM Color", ref hmColor, ImGuiColorEditFlags.NoInputs)) { _config.HitmarkerColor = hmColor; ConfigManager.UpdateCache(_config); }
            var hmThick = _config.HitmarkerThickness; if (ImGui.SliderFloat("HM Thickness", ref hmThick, 0.5f, 10f)) { _config.HitmarkerThickness = hmThick; ConfigManager.UpdateCache(_config); }
            var hmGap = _config.HitmarkerGap; if (ImGui.SliderFloat("HM Gap", ref hmGap, 0f, 20f)) { _config.HitmarkerGap = hmGap; ConfigManager.UpdateCache(_config); }
            var hmLen = _config.HitmarkerLength; if (ImGui.SliderFloat("HM Length", ref hmLen, 1f, 50f)) { _config.HitmarkerLength = hmLen; ConfigManager.UpdateCache(_config); }
            var hmDur = _config.HitmarkerDuration; if (ImGui.SliderFloat("HM Duration", ref hmDur, 0.1f, 2f)) { _config.HitmarkerDuration = hmDur; ConfigManager.UpdateCache(_config); }
            var hmExp = _config.HitmarkerExpand; if (ImGui.SliderFloat("HM Expand", ref hmExp, 0f, 30f)) { _config.HitmarkerExpand = hmExp; ConfigManager.UpdateCache(_config); }
            var hmOut = _config.HitmarkerOutline; if (Toggle("HM Outline", ref hmOut)) { _config.HitmarkerOutline = hmOut; ConfigManager.UpdateCache(_config); }
            var hmSound = _config.HitmarkerSound; if (Toggle("HM Sound", ref hmSound)) { _config.HitmarkerSound = hmSound; ConfigManager.UpdateCache(_config); }
            if (ImGui.Button("Preview Hitmarker", new Vector2(150, 25))) Hitmarker.TriggerPreview();
        }

        var skeletonEsp = _config.SkeletonEsp; if (Toggle("Draw Joint Bones (Skeleton)", ref skeletonEsp)) { _config.SkeletonEsp = skeletonEsp; ConfigManager.UpdateCache(_config); }
        var bulletTracers = _config.BulletTracers; if (Toggle("Draw Bullet Tracers", ref bulletTracers)) { _config.BulletTracers = bulletTracers; ConfigManager.UpdateCache(_config); }
        var grenadeTracker = _config.GrenadeTracker; if (Toggle("Grenade Tracker (ESP)", ref grenadeTracker)) { _config.GrenadeTracker = grenadeTracker; ConfigManager.UpdateCache(_config); }
        var soundEsp = _config.SoundEsp; if (Toggle("Sound ESP", ref soundEsp)) { _config.SoundEsp = soundEsp; ConfigManager.UpdateCache(_config); }

        ImGui.Spacing(); SectionHeader("Offscreen Arrows");
        var offArrows = _config.OffscreenArrows; if (Toggle("Offscreen Arrows", ref offArrows)) { _config.OffscreenArrows = offArrows; ConfigManager.UpdateCache(_config); }
        if (_config.OffscreenArrows)
        {
            var oaSize = _config.OffscreenArrowSize; if (ImGui.SliderFloat("Arrow Size", ref oaSize, 5f, 50f)) { _config.OffscreenArrowSize = oaSize; ConfigManager.UpdateCache(_config); }
            var oaMargin = _config.OffscreenArrowMargin; if (ImGui.SliderFloat("Edge Margin", ref oaMargin, 0f, 100f)) { _config.OffscreenArrowMargin = oaMargin; ConfigManager.UpdateCache(_config); }
            var oaMaxDist = _config.OffscreenArrowMaxDistance; if (ImGui.SliderFloat("Max Distance", ref oaMaxDist, 100f, 5000f)) { _config.OffscreenArrowMaxDistance = oaMaxDist; ConfigManager.UpdateCache(_config); }
            var oaStyle = _config.OffscreenArrowStyle; if (ImGui.Combo("Arrow Style", ref oaStyle, new[] { "Triangle", "Chevron", "Diamond", "Circle" }, 4)) { _config.OffscreenArrowStyle = oaStyle; ConfigManager.UpdateCache(_config); }
            var oaColorMode = _config.OffscreenArrowColorMode; if (ImGui.Combo("Color Mode", ref oaColorMode, new[] { "By Distance", "Static", "By Visibility" }, 3)) { _config.OffscreenArrowColorMode = oaColorMode; ConfigManager.UpdateCache(_config); }
            if (_config.OffscreenArrowColorMode == 1) { var oaColor = _config.OffscreenArrowColor; if (ImGui.ColorEdit4("Arrow Color", ref oaColor, ImGuiColorEditFlags.NoInputs)) { _config.OffscreenArrowColor = oaColor; ConfigManager.UpdateCache(_config); } }
            var oaFilled = _config.OffscreenArrowFilled; if (Toggle("Filled Arrow", ref oaFilled)) { _config.OffscreenArrowFilled = oaFilled; ConfigManager.UpdateCache(_config); }
            var oaOutline = _config.OffscreenArrowOutline; if (Toggle("Arrow Outline", ref oaOutline)) { _config.OffscreenArrowOutline = oaOutline; ConfigManager.UpdateCache(_config); }
            var oaThick = _config.OffscreenArrowThickness; if (ImGui.SliderFloat("Arrow Thickness", ref oaThick, 0.5f, 5f)) { _config.OffscreenArrowThickness = oaThick; ConfigManager.UpdateCache(_config); }
            var oaDist = _config.OffscreenArrowShowDistance; if (Toggle("Show Distance", ref oaDist)) { _config.OffscreenArrowShowDistance = oaDist; ConfigManager.UpdateCache(_config); }
            if (_config.OffscreenArrowShowDistance) { var oaTextSize = _config.OffscreenArrowTextSize; if (ImGui.SliderFloat("Distance Text Size", ref oaTextSize, 8f, 30f)) { _config.OffscreenArrowTextSize = oaTextSize; ConfigManager.UpdateCache(_config); } }
            var oaDormant = _config.OffscreenArrowShowDormant; if (Toggle("Show Dormant", ref oaDormant)) { _config.OffscreenArrowShowDormant = oaDormant; ConfigManager.UpdateCache(_config); }
        }

        ImGui.Spacing(); SectionHeader("Aim Aura");
        var aimAura = _config.AimAura; if (Toggle("Aim Aura", ref aimAura)) { _config.AimAura = aimAura; ConfigManager.UpdateCache(_config); }
        if (_config.AimAura)
        {
            var aaStyle = _config.AimAuraStyle; if (ImGui.Combo("Aura Style", ref aaStyle, new[] { "Ring", "Glow", "Crosshair", "Box", "Orbit" }, 5)) { _config.AimAuraStyle = aaStyle; ConfigManager.UpdateCache(_config); }
            var aaColor = _config.AimAuraColor; if (ImGui.ColorEdit4("Aura Color", ref aaColor, ImGuiColorEditFlags.NoInputs)) { _config.AimAuraColor = aaColor; ConfigManager.UpdateCache(_config); }
            var aaRadius = _config.AimAuraRadius; if (ImGui.SliderFloat("Aura Radius", ref aaRadius, 5f, 100f)) { _config.AimAuraRadius = aaRadius; ConfigManager.UpdateCache(_config); }
            var aaThick = _config.AimAuraThickness; if (ImGui.SliderFloat("Aura Thickness", ref aaThick, 0.5f, 10f)) { _config.AimAuraThickness = aaThick; ConfigManager.UpdateCache(_config); }
            var aaPulseSpd = _config.AimAuraPulseSpeed; if (ImGui.SliderFloat("Pulse Speed", ref aaPulseSpd, 0f, 20f)) { _config.AimAuraPulseSpeed = aaPulseSpd; ConfigManager.UpdateCache(_config); }
            var aaPulseAmt = _config.AimAuraPulseAmount; if (ImGui.SliderFloat("Pulse Amount", ref aaPulseAmt, 0f, 30f)) { _config.AimAuraPulseAmount = aaPulseAmt; ConfigManager.UpdateCache(_config); }
            var aaFilled = _config.AimAuraFilled; if (Toggle("Filled Aura", ref aaFilled)) { _config.AimAuraFilled = aaFilled; ConfigManager.UpdateCache(_config); }
            if (_config.AimAuraFilled) { var aaFillAlpha = _config.AimAuraFillAlpha; if (ImGui.SliderFloat("Fill Alpha", ref aaFillAlpha, 0f, 1f)) { _config.AimAuraFillAlpha = aaFillAlpha; ConfigManager.UpdateCache(_config); } }
            var aaShowName = _config.AimAuraShowName; if (Toggle("Show Target Name", ref aaShowName)) { _config.AimAuraShowName = aaShowName; ConfigManager.UpdateCache(_config); }

            if (_config.AimAuraStyle == 1) // Glow
            { var aaRings = _config.AimAuraGlowRings; if (ImGui.SliderInt("Glow Rings", ref aaRings, 1, 10)) { _config.AimAuraGlowRings = aaRings; ConfigManager.UpdateCache(_config); } }
            if (_config.AimAuraStyle == 2) // Crosshair
            { var aaCrossLen = _config.AimAuraCrosshairLen; if (ImGui.SliderFloat("Crosshair Length", ref aaCrossLen, 1f, 30f)) { _config.AimAuraCrosshairLen = aaCrossLen; ConfigManager.UpdateCache(_config); } }
            if (_config.AimAuraStyle == 4) // Orbit
            {
                var aaOrbSpd = _config.AimAuraOrbitSpeed; if (ImGui.SliderFloat("Orbit Speed", ref aaOrbSpd, 0f, 20f)) { _config.AimAuraOrbitSpeed = aaOrbSpd; ConfigManager.UpdateCache(_config); }
                var aaOrbDots = _config.AimAuraOrbitDots; if (ImGui.SliderInt("Orbit Dots", ref aaOrbDots, 1, 12)) { _config.AimAuraOrbitDots = aaOrbDots; ConfigManager.UpdateCache(_config); }
            }
        }

        ImGui.Spacing(); SectionHeader("Environment");
        var bombTimer = _config.BombTimer; if (Toggle("C4 Explosive Tracking", ref bombTimer)) { _config.BombTimer = bombTimer; ConfigManager.UpdateCache(_config); }
        var voteTeller = _config.VoteTeller; if (Toggle("Server Vote Telemetry", ref voteTeller)) { _config.VoteTeller = voteTeller; ConfigManager.UpdateCache(_config); }
    }

    void TabMisc()
    {
        SectionHeader("Combat Automation");
        var triggerBot = _config.TriggerBot; if (Toggle("Enable Intelligent TriggerBot", ref triggerBot)) { _config.TriggerBot = triggerBot; ConfigManager.UpdateCache(_config); }
        DrawKeyBind("Trigger Execution Key", "TriggerBotKey", _config.TriggerBotKey);
        var triggerDelay = GetConfigFloat("TriggerDelay", 50f); ImGui.SetNextItemWidth(260); if (ImGui.SliderFloat("Trigger Delay (ms)", ref triggerDelay, 0f, 1000f, "%.0f")) { SetConfigFloat("TriggerDelay", triggerDelay); ConfigManager.UpdateCache(_config); }
        var antiFlash = GetConfigBool("AntiFlash"); if (Toggle("Anti-Flash", ref antiFlash)) { SetConfigBool("AntiFlash", antiFlash); ConfigManager.UpdateCache(_config); }

        ImGui.Spacing(); SectionHeader("Movement");
        var teamCheck = _config.TeamCheck; if (Toggle("Ignore Teammates (Filter)", ref teamCheck)) { _config.TeamCheck = teamCheck; ConfigManager.UpdateCache(_config); }
        var bunnyHop = _config.BunnyHop; if (Toggle("Perfect BunnyHop", ref bunnyHop)) { _config.BunnyHop = bunnyHop; ConfigManager.UpdateCache(_config); }

        ImGui.Spacing(); SectionHeader("Interface");
        DrawKeyBind("Toggle Menu Dashboard UI", "MenuToggleKey", _config.MenuToggleKey);

        ImGui.Spacing(); SectionHeader("Global Customization");
        var overlayColor = _config.OverlayAccentColor;
        if (ImGui.ColorEdit4("Accent Color", ref overlayColor, ImGuiColorEditFlags.NoInputs | ImGuiColorEditFlags.AlphaBar | ImGuiColorEditFlags.DisplayRGB)) { _config.OverlayAccentColor = overlayColor; ConfigManager.UpdateCache(_config); }

        ImGui.Spacing(); SectionHeader("Speed Overlay Settings");
        var speedOverlay = _config.SpeedOverlay; if (Toggle("Enable Speed Overlay", ref speedOverlay)) { _config.SpeedOverlay = speedOverlay; ConfigManager.UpdateCache(_config); }
        if (_config.SpeedOverlay)
        {
            var spAlign = _config.SpeedOverlayAlignMode; if (ImGui.Combo("SP Alignment", ref spAlign, AlignModes, AlignModes.Length)) { _config.SpeedOverlayAlignMode = spAlign; ConfigManager.UpdateCache(_config); }
            if (_config.SpeedOverlayAlignMode > 0) { var spY = _config.SpeedOverlayOffsetY; if (ImGui.SliderFloat("SP Y Offset", ref spY, 0f, 1000f)) { _config.SpeedOverlayOffsetY = spY; ConfigManager.UpdateCache(_config); } }
            if (_config.SpeedOverlayAlignMode == 0) { var spLocked = _config.SpeedOverlayLocked; if (Toggle("Lock Position", ref spLocked)) { _config.SpeedOverlayLocked = spLocked; ConfigManager.UpdateCache(_config); } }
            var spW = _config.SpeedOverlayWidth; if (ImGui.SliderFloat("SP Width", ref spW, 100f, 400f)) { _config.SpeedOverlayWidth = spW; ConfigManager.UpdateCache(_config); }
            var spH = _config.SpeedOverlayHeight; if (ImGui.SliderFloat("SP Height", ref spH, 60f, 300f)) { _config.SpeedOverlayHeight = spH; ConfigManager.UpdateCache(_config); }
            var spBgAlpha = _config.SpeedOverlayBgAlpha; if (ImGui.SliderFloat("SP Background Alpha", ref spBgAlpha, 0.0f, 1.0f)) { _config.SpeedOverlayBgAlpha = spBgAlpha; ConfigManager.UpdateCache(_config); }
            var spRounding = _config.SpeedOverlayRounding; if (ImGui.SliderFloat("SP Rounding", ref spRounding, 0.0f, 20.0f)) { _config.SpeedOverlayRounding = spRounding; ConfigManager.UpdateCache(_config); }
            var spBorder = _config.SpeedOverlayShowBorder; if (Toggle("SP Show Border", ref spBorder)) { _config.SpeedOverlayShowBorder = spBorder; ConfigManager.UpdateCache(_config); }
            var spStrip = _config.SpeedOverlayShowAccentStrip; if (Toggle("SP Show Accent Strip", ref spStrip)) { _config.SpeedOverlayShowAccentStrip = spStrip; ConfigManager.UpdateCache(_config); }
            var spText = _config.SpeedOverlayShowText; if (Toggle("SP Show 'SPEED' Text", ref spText)) { _config.SpeedOverlayShowText = spText; ConfigManager.UpdateCache(_config); }
            var spNum = _config.SpeedOverlayShowNumber; if (Toggle("SP Show Number", ref spNum)) { _config.SpeedOverlayShowNumber = spNum; ConfigManager.UpdateCache(_config); }
            var spUnits = _config.SpeedOverlayShowUnits; if (Toggle("SP Show 'u/s'", ref spUnits)) { _config.SpeedOverlayShowUnits = spUnits; ConfigManager.UpdateCache(_config); }
            var spGraph = _config.SpeedOverlayShowGraph; if (Toggle("SP Show Graph", ref spGraph)) { _config.SpeedOverlayShowGraph = spGraph; ConfigManager.UpdateCache(_config); }
            if (_config.SpeedOverlayShowNumber) { var spTextSize = _config.SpeedOverlayTextSize; if (ImGui.SliderFloat("SP Text Size", ref spTextSize, 10f, 60f)) { _config.SpeedOverlayTextSize = spTextSize; ConfigManager.UpdateCache(_config); } }
            if (_config.SpeedOverlayShowGraph) { var spGraphH = _config.SpeedOverlayGraphHeight; if (ImGui.SliderFloat("SP Graph Height", ref spGraphH, 10f, 100f)) { _config.SpeedOverlayGraphHeight = spGraphH; ConfigManager.UpdateCache(_config); } }
        }

        ImGui.Spacing(); SectionHeader("Status Panel Settings");
        var statusPanel = _config.StatusPanel; if (Toggle("Enable Status Panel", ref statusPanel)) { _config.StatusPanel = statusPanel; ConfigManager.UpdateCache(_config); }
        if (_config.StatusPanel)
        {
            var stAlign = _config.StatusPanelAlignMode; if (ImGui.Combo("ST Alignment", ref stAlign, AlignModes, AlignModes.Length)) { _config.StatusPanelAlignMode = stAlign; ConfigManager.UpdateCache(_config); }
            if (_config.StatusPanelAlignMode > 0) { var stY = _config.StatusPanelOffsetY; if (ImGui.SliderFloat("ST Y Offset", ref stY, 0f, 1000f)) { _config.StatusPanelOffsetY = stY; ConfigManager.UpdateCache(_config); } }
            if (_config.StatusPanelAlignMode == 0) { var statusLocked = _config.StatusPanelLocked; if (Toggle("Lock Position", ref statusLocked)) { _config.StatusPanelLocked = statusLocked; ConfigManager.UpdateCache(_config); } }
            var stW = _config.StatusPanelWidth; if (ImGui.SliderFloat("ST Width", ref stW, 100f, 400f)) { _config.StatusPanelWidth = stW; ConfigManager.UpdateCache(_config); }
            var stBgAlpha = _config.StatusPanelBgAlpha; if (ImGui.SliderFloat("ST Background Alpha", ref stBgAlpha, 0.0f, 1.0f)) { _config.StatusPanelBgAlpha = stBgAlpha; ConfigManager.UpdateCache(_config); }
            var stRounding = _config.StatusPanelRounding; if (ImGui.SliderFloat("ST Rounding", ref stRounding, 0.0f, 20.0f)) { _config.StatusPanelRounding = stRounding; ConfigManager.UpdateCache(_config); }
            var stBorder = _config.StatusPanelShowBorder; if (Toggle("ST Show Border", ref stBorder)) { _config.StatusPanelShowBorder = stBorder; ConfigManager.UpdateCache(_config); }
            var stStrip = _config.StatusPanelShowAccentStrip; if (Toggle("ST Show Accent Strip", ref stStrip)) { _config.StatusPanelShowAccentStrip = stStrip; ConfigManager.UpdateCache(_config); }
            var stHeader = _config.StatusPanelShowHeader; if (Toggle("ST Show Header", ref stHeader)) { _config.StatusPanelShowHeader = stHeader; ConfigManager.UpdateCache(_config); }
            var stFps = _config.StatusPanelShowFPS; if (Toggle("ST Show FPS", ref stFps)) { _config.StatusPanelShowFPS = stFps; ConfigManager.UpdateCache(_config); }
            var stActive = _config.StatusPanelShowOnlyActive; if (Toggle("ST Show Only Active Modules", ref stActive)) { _config.StatusPanelShowOnlyActive = stActive; ConfigManager.UpdateCache(_config); }
            var stTextSize = _config.StatusPanelTextSize; if (ImGui.SliderFloat("ST Text Size", ref stTextSize, 10f, 30f)) { _config.StatusPanelTextSize = stTextSize; ConfigManager.UpdateCache(_config); }
            var stSpacing = _config.StatusPanelItemSpacing; if (ImGui.SliderFloat("ST Item Spacing", ref stSpacing, 0f, 20f)) { _config.StatusPanelItemSpacing = stSpacing; ConfigManager.UpdateCache(_config); }
        }

        ImGui.Spacing(); SectionHeader("Keybind Overlay Settings");
        var keybindOv = _config.KeybindOverlay; if (Toggle("Enable Keybind Overlay", ref keybindOv)) { _config.KeybindOverlay = keybindOv; ConfigManager.UpdateCache(_config); }
        if (_config.KeybindOverlay)
        {
            var kbAlign = _config.KeybindOverlayAlignMode; if (ImGui.Combo("KB Alignment", ref kbAlign, AlignModes, AlignModes.Length)) { _config.KeybindOverlayAlignMode = kbAlign; ConfigManager.UpdateCache(_config); }
            if (_config.KeybindOverlayAlignMode > 0) { var kbY = _config.KeybindOverlayOffsetY; if (ImGui.SliderFloat("KB Y Offset", ref kbY, 0f, 1000f)) { _config.KeybindOverlayOffsetY = kbY; ConfigManager.UpdateCache(_config); } }
            if (_config.KeybindOverlayAlignMode == 0) { var kbLocked = _config.KeybindOverlayLocked; if (Toggle("Lock Position", ref kbLocked)) { _config.KeybindOverlayLocked = kbLocked; ConfigManager.UpdateCache(_config); } }
        }

        ImGui.Spacing(); SectionHeader("Aimbot Mode Overlay");
        var modeOv = _config.ModeOverlay; if (Toggle("Enable Mode Overlay", ref modeOv)) { _config.ModeOverlay = modeOv; ConfigManager.UpdateCache(_config); }
        if (_config.ModeOverlay)
        {
            var modeSize = _config.ModeOverlaySize; if (ImGui.SliderFloat("Mode Text Size", ref modeSize, 10f, 50f)) { _config.ModeOverlaySize = modeSize; ConfigManager.UpdateCache(_config); }
            var modeY = _config.ModeOverlayOffsetY; if (ImGui.SliderFloat("Mode Y Offset", ref modeY, 0f, 500f)) { _config.ModeOverlayOffsetY = modeY; ConfigManager.UpdateCache(_config); }
        }
    }

    void TabConfig()
    {
        SectionHeader("Configuration");
        ImGui.PushStyleColor(ImGuiCol.Button, ColSuccess); ImGui.PushStyleColor(ImGuiCol.ButtonHovered, ColAccentDim);
        if (ImGui.Button("Save Local Configuration", new Vector2(260, 36))) { ConfigManager.Save(_config); }
        ImGui.PopStyleColor(2);
        ImGui.Spacing();
        if (ImGui.Button("Reload From Local Cache", new Vector2(260, 36))) { ConfigManager.Reload(); _config = ConfigManager.Load(); }
        ImGui.Spacing();
        if (ImGui.Button("Reset To Defaults", new Vector2(260, 36))) { _config = ConfigManager.Default(); ConfigManager.Save(_config); }
        ImGui.Spacing(); ImGui.TextWrapped("config.json");
    }

    void SectionHeader(string text) { ImGui.TextColored(_config.OverlayAccentColor, text.ToUpper()); ImGui.Spacing(); }
    bool Toggle(string label, ref bool value) { if (ImGui.Checkbox(label, ref value)) { ConfigManager.UpdateCache(_config); return true; } return false; }

    void DrawKeyBind(string label, string bindId, Keys currentKey)
    {
        ImGui.PushID(bindId);
        bool isWaiting = _waitingForBind == bindId;
        string btnText = isWaiting ? "[ ... ]" : $"[ {ConfigManager.GetKeyName(currentKey)} ]";
        ImGui.Text(label); ImGui.SameLine(260);
        if (isWaiting) { ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(0.85f, 0.20f, 0.20f, 1f)); ImGui.PushStyleColor(ImGuiCol.ButtonHovered, new Vector4(0.95f, 0.25f, 0.25f, 1f)); }
        if (ImGui.Button(btnText, new Vector2(140, 0))) { _waitingForBind = isWaiting ? null : bindId; _clearBindKeyRelease = true; }
        if (isWaiting) { ImGui.PopStyleColor(2); if (_clearBindKeyRelease) { if (ScanKey() == Keys.None) _clearBindKeyRelease = false; } else { var k = ScanKey(); if (k != Keys.None) { if (k == Keys.Escape) { _waitingForBind = null; } else { switch (bindId) { case "AimBotKey": _config.AimBotKey = k; break; case "TriggerBotKey": _config.TriggerBotKey = k; break; case "MenuToggleKey": _config.MenuToggleKey = k; break; case "AimLegitToggleKey": _config.AimLegitToggleKey = k; break; case "SilentAimKey": SetConfigKey("SilentAimKey", k); ConfigManager.UpdateCache(_config); break; } _waitingForBind = null; } } } }
        ImGui.PopID();
    }

    static Keys ScanKey()
    {
        Keys[] keys = { Keys.LButton, Keys.RButton, Keys.MButton, Keys.XButton1, Keys.XButton2, Keys.LMenu, Keys.RMenu, Keys.LShiftKey, Keys.RShiftKey, Keys.LControlKey, Keys.RControlKey, Keys.Insert, Keys.Delete, Keys.Home, Keys.End, Keys.Capital, Keys.Tab, Keys.Space, Keys.F1, Keys.F2, Keys.F3, Keys.F4, Keys.F5, Keys.F6, Keys.F7, Keys.F8, Keys.F9, Keys.F10, Keys.F11, Keys.F12, Keys.Q, Keys.W, Keys.E, Keys.R, Keys.T, Keys.Y, Keys.U, Keys.I, Keys.O, Keys.P, Keys.A, Keys.S, Keys.D, Keys.F, Keys.G, Keys.H, Keys.J, Keys.K, Keys.L, Keys.Z, Keys.X, Keys.C, Keys.V, Keys.B, Keys.N, Keys.M, Keys.D0, Keys.D1, Keys.D2, Keys.D3, Keys.D4, Keys.D5, Keys.D6, Keys.D7, Keys.D8, Keys.D9, Keys.Escape };
        foreach (var k in keys) if (k.IsKeyDown()) return k;
        return Keys.None;
    }

    void RenderVisuals(ImDrawListPtr dl)
    {
        if (_config.EspBox) EspBox.Draw(dl, _gameData, _gameProcess);
        if (_config.Hitmarker) Hitmarker.Draw(dl, _gameData, _gameProcess);
        if (_config.SkeletonEsp) SkeletonEsp.Draw(dl, _gameData);
        if (_config.BombTimer) BombTimer.Draw(dl);
        if (_config.VoteTeller) VoteTeller.Draw(dl);

        if (_config.BulletTracers)
        {
            _bulletTracers.Update(_gameProcess, _gameData);
            foreach (var tracer in _bulletTracers.ActiveTracers)
            {
                if (WorldToScreen(tracer.StartPos, out var screenStart) && WorldToScreen(tracer.EndPos, out var screenEnd))
                {
                    float timeLeft = (float)(tracer.ExpireTime - DateTime.Now).TotalMilliseconds;
                    byte alpha = 255; if (timeLeft < 150f) alpha = (byte)(255 * Math.Max(timeLeft / 150f, 0f));
                    uint color = tracer.Source switch { BulletTracers.TracerSource.LocalPlayer => ToColor(0, 255, 200, alpha), BulletTracers.TracerSource.Teammate => ToColor(50, 205, 50, alpha), BulletTracers.TracerSource.Enemy => ToColor(255, 50, 50, alpha), _ => ToColor(255, 255, 255, alpha) };
                    dl.AddLine(screenStart, screenEnd, ToColor(0, 0, 0, alpha), 3.5f); dl.AddLine(screenStart, screenEnd, color, 1.5f);
                }
            }
        }

        if (_config.SoundEsp)
        {
            _soundEsp.Update(_gameProcess, _gameData);
            foreach (var sound in _soundEsp.ActiveSounds)
            {
                if (WorldToScreen(sound.Position, out var screenPos))
                {
                    float lifespan = (float)(DateTime.Now - sound.StartTime).TotalMilliseconds; float progress = lifespan / 1000f;
                    if (progress > 1f) continue;
                    float maxRadius = sound.Type == SoundEsp.SoundType.Gunshot ? 80f : 40f; float radius = 5f + (maxRadius * progress); byte alpha = (byte)(255 * (1f - progress));
                    uint color = sound.Type == SoundEsp.SoundType.Gunshot ? ToColor(255, 50, 50, alpha) : ToColor(255, 255, 0, alpha);
                    dl.AddCircle(screenPos, radius, color, 32, 2.0f); dl.AddCircleFilled(screenPos, 2.0f, color);
                }
            }
        }

        if (_config.GrenadeTracker)
        {
            _grenadeTracker.Update(_gameProcess, _gameData);
            foreach (var grenade in _grenadeTracker.ActiveGrenades)
            {
                if (WorldToScreen(grenade.Position, out var screenPos))
                {
                    uint color = Colors.White; string text = "?";
                    switch (grenade.Type) { case GrenadeTracker.GrenadeType.Smoke: color = Colors.DeepSkyBlue; text = "SMOKE"; break; case GrenadeTracker.GrenadeType.Flash: color = Colors.Yellow; text = "FLASH"; break; case GrenadeTracker.GrenadeType.HE: color = Colors.OrangeRed; text = "HE"; break; case GrenadeTracker.GrenadeType.Molotov: color = Colors.Red; text = "FIRE"; break; case GrenadeTracker.GrenadeType.Decoy: color = Colors.LimeGreen; text = "DECOY"; break; }
                    dl.AddCircleFilled(screenPos, 5.0f, ToColor(0, 0, 0, 200)); dl.AddCircle(screenPos, 5.0f, color, 12, 2.0f); dl.AddText(new Vector2(screenPos.X + 10, screenPos.Y - 8), Colors.Black, text); dl.AddText(new Vector2(screenPos.X + 9, screenPos.Y - 9), color, text);
                }
            }
        }

        if (_config.AimFovCircle)
        {
            var io = ImGui.GetIO(); var center = new Vector2(io.DisplaySize.X / 2, io.DisplaySize.Y / 2);
            var radius = (float)(Math.Tan((_config.AimFov * Math.PI / 180.0) / 2.0) / Math.Tan((90.0 * Math.PI / 180.0) / 2.0) * (io.DisplaySize.X / 2.0));
            dl.AddCircle(center, radius, ToColor(0, 0, 0, 150), 64, 2.5f); dl.AddCircle(center, radius, Colors.White, 64, 1.0f);
        }
    }

    void RenderOffscreenArrows(ImDrawListPtr dl)
    {
        if (!_config.OffscreenArrows || _gameData?.Player == null || _gameProcess?.ModuleClient == null) return;

        var io = ImGui.GetIO();
        float screenW = io.DisplaySize.X;
        float screenH = io.DisplaySize.Y;
        Vector2 center = new Vector2(screenW / 2, screenH / 2);
        float margin = _config.OffscreenArrowMargin;

        Vector3 viewAngles = _gameProcess.ModuleClient.Read<Vector3>(Offsets.dwViewAngles);
        float radPitch = viewAngles.X * (MathF.PI / 180f);
        float radYaw = viewAngles.Y * (MathF.PI / 180f);

        float cp = MathF.Cos(radPitch);
        float sp = MathF.Sin(radPitch);
        float cy = MathF.Cos(radYaw);
        float sy = MathF.Sin(radYaw); // Здесь был первый sy

        Vector3 fwd = new(cp * cy, cp * sy, -sp);
        Vector3 right = new(-sy, cy, 0);
        Vector3 up = new(-sp * cy, -sp * sy, cp);

        Vector3 eyePos = _gameData.Player.EyePosition;
        if (eyePos == Vector3.Zero) return;

        foreach (var entity in _gameData.Entities)
        {
            if (entity?.AddressBase == IntPtr.Zero || !entity.IsAlive()) continue;
            if (_config.TeamCheck && entity.Team == _gameData.Player.Team) continue;

            try
            {
                IntPtr gsn = _gameProcess.Process.Read<IntPtr>(entity.AddressBase + Offsets.m_pGameSceneNode);
                if (gsn != IntPtr.Zero && _gameProcess.Process.Read<bool>(gsn + Offsets.m_bDormant) && !_config.OffscreenArrowShowDormant)
                    continue;
            }
            catch { continue; }

            Vector3 headPos = entity.BonePos.GetValueOrDefault("head");
            if (headPos == Vector3.Zero) continue;

            float dist3D = Vector3.Distance(eyePos, headPos);
            if (dist3D > _config.OffscreenArrowMaxDistance) continue;

            Vector3 delta = headPos - eyePos;
            float fwdDot = Vector3.Dot(delta, fwd);
            float rightDot = Vector3.Dot(delta, right);
            float upDot = Vector3.Dot(delta, up);

            float dirX = rightDot;
            float dirY = -upDot;
            if (fwdDot < 0) { dirX = -dirX; dirY = -dirY; }

            if (fwdDot > 0)
            {
                float screenX = center.X + (rightDot / fwdDot) * center.X;
                float screenY = center.Y - (upDot / fwdDot) * center.Y; // ИСПРАВЛЕНО: было sy, стало screenY
                if (screenX >= 0 && screenX <= screenW && screenY >= 0 && screenY <= screenH) continue;
            }

            float dirLen = MathF.Sqrt(dirX * dirX + dirY * dirY);
            if (dirLen < 0.001f) continue;
            dirX /= dirLen; dirY /= dirLen;

            float halfW = screenW / 2 - margin;
            float halfH = screenH / 2 - margin;
            float scale = Math.Min(halfW / Math.Max(Math.Abs(dirX), 0.001f), halfH / Math.Max(Math.Abs(dirY), 0.001f));
            Vector2 arrowPos = center + new Vector2(dirX * scale, dirY * scale);
            arrowPos.X = Math.Clamp(arrowPos.X, margin, screenW - margin);
            arrowPos.Y = Math.Clamp(arrowPos.Y, margin, screenH - margin);

            uint arrowColor;
            if (_config.OffscreenArrowColorMode == 0) // By Distance
            {
                float pct = Math.Clamp(dist3D / _config.OffscreenArrowMaxDistance, 0f, 1f);
                byte r = (byte)(255 * (1f - pct * 0.5f));
                byte g = (byte)(80 + 120 * pct);
                byte b = (byte)(60 * pct);
                byte a = (byte)(255 * (1f - pct * 0.4f));
                arrowColor = ToColor(r, g, b, a);
            }
            else if (_config.OffscreenArrowColorMode == 2) // By Visibility
            {
                bool vis = false;
                try { vis = _gameProcess.Process.Read<bool>(entity.AddressBase + Offsets.m_entitySpottedState + 0x8); } catch { }
                arrowColor = vis ? ToColor(46, 213, 115, 220) : ToColor(240, 50, 50, 220);
            }
            else
            {
                arrowColor = _config.OffscreenArrowColor.ToUint();
            }

            float angle = MathF.Atan2(dirY, dirX);
            float size = _config.OffscreenArrowSize;

            DrawArrowShape(dl, arrowPos, angle, size, arrowColor);

            if (_config.OffscreenArrowShowDistance)
            {
                string distText = $"{dist3D / 52.49f:0}m";
                Vector2 textOffset = new Vector2(-MathF.Cos(angle), -MathF.Sin(angle)) * (size + 10f);
                var textSize = ImGui.CalcTextSize(distText) * (_config.OffscreenArrowTextSize / 15f);
                Vector2 textPos = arrowPos + textOffset - textSize / 2f;
                dl.AddText(ImGui.GetFont(), _config.OffscreenArrowTextSize, textPos, arrowColor, distText);
            }
        }
    }

    void DrawArrowShape(ImDrawListPtr dl, Vector2 pos, float angle, float size, uint color)
    {
        float cos = MathF.Cos(angle);
        float sin = MathF.Sin(angle);
        uint outline = ToColor(0, 0, 0, 200);
        float thick = _config.OffscreenArrowThickness;

        switch (_config.OffscreenArrowStyle)
        {
            case 0: // Triangle
                {
                    Vector2 p1 = pos + new Vector2(cos * size, sin * size);
                    Vector2 p2 = pos + new Vector2(cos * -size * 0.5f - sin * size * 0.6f, sin * -size * 0.5f + cos * size * 0.6f);
                    Vector2 p3 = pos + new Vector2(cos * -size * 0.5f + sin * size * 0.6f, sin * -size * 0.5f - cos * size * 0.6f);
                    if (_config.OffscreenArrowFilled) dl.AddTriangleFilled(p1, p2, p3, color);
                    else dl.AddTriangle(p1, p2, p3, color, thick);
                    if (_config.OffscreenArrowOutline) { dl.AddTriangle(p1, p2, p3, outline, thick + 1f); if (_config.OffscreenArrowFilled) dl.AddTriangleFilled(p1, p2, p3, color); }
                    break;
                }
            case 1: // Chevron
                {
                    Vector2 p1 = pos + new Vector2(cos * size, sin * size);
                    Vector2 p2 = pos + new Vector2(cos * -size * 0.3f - sin * size * 0.7f, sin * -size * 0.3f + cos * size * 0.7f);
                    Vector2 p3 = pos + new Vector2(cos * -size * 0.3f + sin * size * 0.7f, sin * -size * 0.3f - cos * size * 0.7f);
                    dl.AddLine(p1, p2, color, thick);
                    dl.AddLine(p1, p3, color, thick);
                    if (_config.OffscreenArrowOutline) { dl.AddLine(p1, p2, outline, thick + 1f); dl.AddLine(p1, p3, outline, thick + 1f); }
                    break;
                }
            case 2: // Diamond
                {
                    Vector2 p1 = pos + new Vector2(cos * size, sin * size);
                    Vector2 p2 = pos + new Vector2(cos * -size * 0.3f - sin * size * 0.5f, sin * -size * 0.3f + cos * size * 0.5f);
                    Vector2 p3 = pos + new Vector2(cos * -size, sin * -size);
                    Vector2 p4 = pos + new Vector2(cos * -size * 0.3f + sin * size * 0.5f, sin * -size * 0.3f - cos * size * 0.5f);
                    if (_config.OffscreenArrowFilled) dl.AddQuadFilled(p1, p2, p3, p4, color);
                    else dl.AddQuad(p1, p2, p3, p4, color, thick);
                    if (_config.OffscreenArrowOutline) dl.AddQuad(p1, p2, p3, p4, outline, thick + 1f);
                    break;
                }
            case 3: // Circle
                {
                    if (_config.OffscreenArrowFilled) dl.AddCircleFilled(pos, size * 0.6f, color, 16);
                    else dl.AddCircle(pos, size * 0.6f, color, 16, thick);
                    if (_config.OffscreenArrowOutline) dl.AddCircle(pos, size * 0.6f, outline, 16, thick + 1f);
                    break;
                }
        }
    }

    void RenderAimAura(ImDrawListPtr dl)
    {
        if (!_config.AimAura || _gameData?.Player == null) return;

        IntPtr targetAddr = _aimBot.LockedTargetAddress;
        if (targetAddr == IntPtr.Zero) return;

        Entity? target = null;
        foreach (var e in _gameData.Entities)
        {
            if (e?.AddressBase == targetAddr) { target = e; break; }
        }
        if (target == null) return;

        Vector3 headPos = target.BonePos.GetValueOrDefault("head");
        if (headPos == Vector3.Zero) return;
        headPos.Z += 3f;

        if (!WorldToScreen(headPos, out Vector2 screenPos)) return;

        uint color = _config.AimAuraColor.ToUint();
        float time = (float)ImGui.GetTime();
        float pulse = MathF.Sin(time * _config.AimAuraPulseSpeed) * 0.5f + 0.5f;
        float radius = _config.AimAuraRadius + pulse * _config.AimAuraPulseAmount;

        switch (_config.AimAuraStyle)
        {
            case 0: // Ring
                {
                    if (_config.AimAuraFilled)
                    {
                        uint fillColor = new Vector4(_config.AimAuraColor.X, _config.AimAuraColor.Y, _config.AimAuraColor.Z, _config.AimAuraFillAlpha).ToUint();
                        dl.AddCircleFilled(screenPos, radius, fillColor, 48);
                    }
                    dl.AddCircle(screenPos, radius, color, 48, _config.AimAuraThickness);
                    if (_config.AimAuraOutline) dl.AddCircle(screenPos, radius, ToColor(0, 0, 0, 180), 48, _config.AimAuraThickness + 1.5f);
                    dl.AddCircle(screenPos, radius, color, 48, _config.AimAuraThickness);
                    break;
                }
            case 1: // Glow (multiple rings)
                {
                    for (int i = 0; i < _config.AimAuraGlowRings; i++)
                    {
                        float r = radius + i * 4f;
                        byte a = (byte)(200 * (1f - (float)i / _config.AimAuraGlowRings));
                        uint glowColor = new Vector4(_config.AimAuraColor.X, _config.AimAuraColor.Y, _config.AimAuraColor.Z, a / 255f).ToUint();
                        dl.AddCircle(screenPos, r, glowColor, 48, _config.AimAuraThickness);
                    }
                    break;
                }
            case 2: // Crosshair
                {
                    float len = _config.AimAuraCrosshairLen;
                    float gap = radius - _config.AimAuraExpand;
                    float rot = time * _config.AimAuraOrbitSpeed * 0.5f;

                    for (int i = 0; i < 4; i++)
                    {
                        float angle = i * MathF.PI / 2f + rot;
                        float dx = MathF.Cos(angle), dy = MathF.Sin(angle);
                        Vector2 p1 = screenPos + new Vector2(dx * gap, dy * gap);
                        Vector2 p2 = screenPos + new Vector2(dx * (gap + len), dy * (gap + len));
                        dl.AddLine(p1, p2, ToColor(0, 0, 0, 180), _config.AimAuraThickness + 1.5f);
                        dl.AddLine(p1, p2, color, _config.AimAuraThickness);
                    }
                    break;
                }
            case 3: // Box
                {
                    float half = radius;
                    Vector2 p1 = screenPos - new Vector2(half, half);
                    Vector2 p2 = screenPos + new Vector2(half, half);
                    if (_config.AimAuraFilled)
                    {
                        uint fillColor = new Vector4(_config.AimAuraColor.X, _config.AimAuraColor.Y, _config.AimAuraColor.Z, _config.AimAuraFillAlpha).ToUint();
                        dl.AddRectFilled(p1, p2, fillColor, 4f);
                    }
                    dl.AddRect(p1, p2, ToColor(0, 0, 0, 180), 4f, ImDrawFlags.None, _config.AimAuraThickness + 1.5f);
                    dl.AddRect(p1, p2, color, 4f, ImDrawFlags.None, _config.AimAuraThickness);
                    break;
                }
            case 4: // Orbit
                {
                    dl.AddCircle(screenPos, radius, ToColor(0, 0, 0, 120), 48, 1f);
                    for (int i = 0; i < _config.AimAuraOrbitDots; i++)
                    {
                        float angle = (i / (float)_config.AimAuraOrbitDots) * MathF.PI * 2f + time * _config.AimAuraOrbitSpeed;
                        Vector2 dotPos = screenPos + new Vector2(MathF.Cos(angle) * radius, MathF.Sin(angle) * radius);
                        dl.AddCircleFilled(dotPos, _config.AimAuraThickness * 2f, color, 12);
                        dl.AddCircleFilled(dotPos, _config.AimAuraThickness * 3f, new Vector4(_config.AimAuraColor.X, _config.AimAuraColor.Y, _config.AimAuraColor.Z, 0.3f).ToUint(), 12);
                    }
                    break;
                }
        }

        if (_config.AimAuraShowName)
        {
            string name = target.Name ?? "Player";
            var sz = ImGui.CalcTextSize(name);
            Vector2 textPos = new Vector2(screenPos.X - sz.X / 2f, screenPos.Y - radius - 20f);
            dl.AddText(textPos + new Vector2(1, 1), ToColor(0, 0, 0, 200), name);
            dl.AddText(textPos, color, name);
        }
    }

    Vector2 GetAlignedPos(Vector2 savedPos, float w, float h, int alignMode, float vOffset)
    {
        var io = ImGui.GetIO();
        if (alignMode == 1) return new Vector2(io.DisplaySize.X / 2 - w / 2, vOffset);
        if (alignMode == 2) return new Vector2(io.DisplaySize.X / 2 - w / 2, io.DisplaySize.Y - h - vOffset);
        return savedPos;
    }

    void RenderSpeedOverlay(ImDrawListPtr dl)
    {
        if (!_config.SpeedOverlay || _gameData?.Player == null || !_gameProcess.IsValid) return;
        Vector4 accent = _config.OverlayAccentColor; uint accentCol = accent.ToUint(); uint bgCol = ToColor(8, 10, 14, (byte)(_config.SpeedOverlayBgAlpha * 255)); uint borderCol = new Vector4(accent.X * 0.3f, accent.Y * 0.3f, accent.Z * 0.3f, 0.7f).ToUint();
        float w = _config.SpeedOverlayWidth; float h = _config.SpeedOverlayHeight;
        Vector2 pos = GetAlignedPos(_config.SpeedOverlayPos, w, h, _config.SpeedOverlayAlignMode, _config.SpeedOverlayOffsetY);

        if (_config.SpeedOverlayAlignMode == 0 && _showMenu && !_config.SpeedOverlayLocked)
        {
            Vector2 mouse = ImGui.GetMousePos(); bool hovered = mouse.X >= pos.X && mouse.X <= pos.X + w && mouse.Y >= pos.Y && mouse.Y <= pos.Y + h;
            if (hovered && ImGui.IsMouseClicked(ImGuiMouseButton.Left) && !_statusDragging && !_keybindDragging) _speedDragging = true;
            if (_speedDragging) { if (ImGui.IsMouseDown(ImGuiMouseButton.Left)) { _config.SpeedOverlayPos = mouse - new Vector2(w / 2, h / 2); } else { _speedDragging = false; ConfigManager.UpdateCache(_config); } }
        }

        dl.AddRectFilled(pos, pos + new Vector2(w, h), bgCol, _config.SpeedOverlayRounding);
        if (_config.SpeedOverlayShowBorder) dl.AddRect(pos, pos + new Vector2(w, h), borderCol, _config.SpeedOverlayRounding, ImDrawFlags.None, 1f);
        if (_config.SpeedOverlayShowAccentStrip) dl.AddRectFilled(pos + new Vector2(0, 0), pos + new Vector2(3, h), accentCol, _config.SpeedOverlayRounding);

        Vector3 vel = _gameProcess.Process.Read<Vector3>(_gameData.Player.AddressBase + Offsets.m_vecAbsVelocity); float speed = MathF.Sqrt(vel.X * vel.X + vel.Y * vel.Y);
        _speedHistory.Add(speed); if (_speedHistory.Count > 80) _speedHistory.RemoveAt(0);

        if (_config.SpeedOverlayShowText) dl.AddText(ImGui.GetFont(), 12f, pos + new Vector2(12, 8), ColTextDim.ToUint(), "SPEED");
        if (_config.SpeedOverlayShowNumber)
        {
            string speedText = $"{speed:0}"; dl.AddText(ImGui.GetFont(), _config.SpeedOverlayTextSize, pos + new Vector2(12, 22), accentCol, speedText);
            if (_config.SpeedOverlayShowUnits) { var textSize = ImGui.CalcTextSize(speedText) * (_config.SpeedOverlayTextSize / 15f); dl.AddText(ImGui.GetFont(), 12f, pos + new Vector2(14 + textSize.X, 42), ColTextDim.ToUint(), "u/s"); }
        }
        if (_config.SpeedOverlayShowGraph)
        {
            float graphX = 12; float graphH = _config.SpeedOverlayGraphHeight; float graphW = w - 24; float graphY = h - graphH - 10;
            dl.AddRectFilled(pos + new Vector2(graphX, graphY), pos + new Vector2(graphX + graphW, graphY + graphH), ToColor(4, 6, 10, 200), 4f);
            if (_speedHistory.Count > 1)
            {
                var points = new Vector2[_speedHistory.Count];
                for (int i = 0; i < _speedHistory.Count; i++) { float val = Math.Clamp(_speedHistory[i] / 320f, 0f, 1f); float px = pos.X + graphX + (i / (float)(_speedHistory.Count - 1)) * graphW; float py = pos.Y + graphY + graphH - (val * graphH); points[i] = new Vector2(px, py); }
                var fillPoints = new Vector2[_speedHistory.Count + 2]; for (int i = 0; i < _speedHistory.Count; i++) fillPoints[i] = points[i];
                fillPoints[_speedHistory.Count] = new Vector2(pos.X + graphX + graphW, pos.Y + graphY + graphH); fillPoints[_speedHistory.Count + 1] = new Vector2(pos.X + graphX, pos.Y + graphY + graphH);
                dl.AddConvexPolyFilled(ref fillPoints[0], fillPoints.Length, new Vector4(accent.X, accent.Y, accent.Z, 0.15f).ToUint()); dl.AddPolyline(ref points[0], points.Length, accentCol, ImDrawFlags.None, 1.5f);
            }
        }
        if (_showMenu && _config.SpeedOverlayAlignMode == 0) { string lockText = _config.SpeedOverlayLocked ? "[LOCKED]" : "[UNLOCKED]"; uint lockCol = _config.SpeedOverlayLocked ? Colors.Red : Colors.Green; dl.AddText(ImGui.GetFont(), 10f, pos + new Vector2(w - 60, 8), lockCol, lockText); }
    }

    void RenderStatusPanel(ImDrawListPtr dl)
    {
        if (!_config.StatusPanel || !_gameProcess.IsValid) return;
        Vector4 accent = _config.OverlayAccentColor; uint accentCol = accent.ToUint(); uint bgCol = ToColor(8, 10, 14, (byte)(_config.StatusPanelBgAlpha * 255)); uint borderCol = new Vector4(accent.X * 0.3f, accent.Y * 0.3f, accent.Z * 0.3f, 0.7f).ToUint();
        float w = _config.StatusPanelWidth; var items = new List<(string, bool)>();
        if (!_config.StatusPanelShowOnlyActive) { items.Add(("Aimbot", _config.AimBot)); items.Add(("TriggerBot", _config.TriggerBot)); items.Add(("Silent Aim", GetConfigBool("SilentAim"))); items.Add(("RCS", GetConfigBool("RcsEnabled"))); items.Add(("Bhop", _config.BunnyHop)); items.Add(("Anti-Flash", GetConfigBool("AntiFlash"))); items.Add(("ESP", _config.EspBox)); if (_config.StatusPanelShowFPS) items.Add(("FPS", true)); }
        else { if (_config.AimBot) items.Add(("Aimbot", true)); if (_config.TriggerBot) items.Add(("TriggerBot", true)); if (GetConfigBool("SilentAim")) items.Add(("Silent Aim", true)); if (GetConfigBool("RcsEnabled")) items.Add(("RCS", true)); if (_config.BunnyHop) items.Add(("Bhop", true)); if (GetConfigBool("AntiFlash")) items.Add(("Anti-Flash", true)); if (_config.EspBox) items.Add(("ESP", true)); if (_config.StatusPanelShowFPS) items.Add(("FPS", true)); }

        float itemH = _config.StatusPanelTextSize + _config.StatusPanelItemSpacing; float dynamicHeight = (items.Count * itemH) + (_config.StatusPanelShowHeader ? 36 : 14); float h = dynamicHeight;
        Vector2 pos = GetAlignedPos(_config.StatusPanelPos, w, h, _config.StatusPanelAlignMode, _config.StatusPanelOffsetY);

        if (_config.StatusPanelAlignMode == 0 && _showMenu && !_config.StatusPanelLocked)
        {
            Vector2 mouse = ImGui.GetMousePos(); bool hovered = mouse.X >= pos.X && mouse.X <= pos.X + w && mouse.Y >= pos.Y && mouse.Y <= pos.Y + h;
            if (hovered && ImGui.IsMouseClicked(ImGuiMouseButton.Left) && !_speedDragging && !_keybindDragging) _statusDragging = true;
            if (_statusDragging) { if (ImGui.IsMouseDown(ImGuiMouseButton.Left)) { _config.StatusPanelPos = mouse - new Vector2(w / 2, h / 2); } else { _statusDragging = false; ConfigManager.UpdateCache(_config); } }
        }

        dl.AddRectFilled(pos, pos + new Vector2(w, h), bgCol, _config.StatusPanelRounding);
        if (_config.StatusPanelShowBorder) dl.AddRect(pos, pos + new Vector2(w, h), borderCol, _config.StatusPanelRounding, ImDrawFlags.None, 1f);
        if (_config.StatusPanelShowAccentStrip) dl.AddRectFilled(pos + new Vector2(0, 0), pos + new Vector2(3, h), accentCol, _config.StatusPanelRounding);

        float y = 14;
        if (_config.StatusPanelShowHeader) { dl.AddText(ImGui.GetFont(), 14f, pos + new Vector2(14, 10), accentCol, "MODULES"); dl.AddRectFilled(pos + new Vector2(14, 28), pos + new Vector2(w - 14, 29), borderCol, 1f); y = 36; }
        float dotX = 14; float textX = 30;
        foreach (var item in items) { uint dotColor = item.Item2 ? accentCol : ToColor(30, 35, 45, 200); uint textColor = item.Item2 ? ColText.ToUint() : ColTextDim.ToUint(); dl.AddRectFilled(pos + new Vector2(dotX, y), pos + new Vector2(dotX + 10, y + 10), dotColor, 2f); dl.AddText(ImGui.GetFont(), _config.StatusPanelTextSize, pos + new Vector2(textX, y - 1), textColor, item.Item1); y += itemH; }
        if (_showMenu && _config.StatusPanelAlignMode == 0) { string lockText = _config.StatusPanelLocked ? "[LOCKED]" : "[UNLOCKED]"; uint lockCol = _config.StatusPanelLocked ? Colors.Red : Colors.Green; dl.AddText(ImGui.GetFont(), 10f, pos + new Vector2(w - 60, 10), lockCol, lockText); }
    }

    void RenderKeybindOverlay()
    {
        if (!_config.KeybindOverlay) return;
        Vector2 pos = _config.KeybindOverlayPos; bool locked = _config.KeybindOverlayLocked;
        float keySize = 28f; float gap = 4f; float w = 4 * keySize + 3 * gap + 12; float h = 3 * keySize + 2 * gap + 12;
        pos = GetAlignedPos(pos, w, h, _config.KeybindOverlayAlignMode, _config.KeybindOverlayOffsetY);

        ImGui.SetNextWindowPos(pos, ImGuiCond.Always);
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(6, 6)); ImGui.PushStyleVar(ImGuiStyleVar.WindowRounding, 4f);
        ImGui.PushStyleColor(ImGuiCol.WindowBg, new Vector4(0.04f, 0.05f, 0.07f, 0.9f));
        var flags = ImGuiWindowFlags.NoTitleBar | ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoCollapse | ImGuiWindowFlags.NoFocusOnAppearing | ImGuiWindowFlags.NoBringToFrontOnFocus;
        if (locked || _config.KeybindOverlayAlignMode > 0) flags |= ImGuiWindowFlags.NoMove | ImGuiWindowFlags.NoInputs;

        ImGui.Begin("##keybind_overlay", flags);
        var dl = ImGui.GetWindowDrawList(); Vector2 wp = ImGui.GetWindowPos(); Vector4 accent = _config.OverlayAccentColor;
        void DrawKey(float x, float y, string label, int vk) { bool down = (User32.GetAsyncKeyState(vk) & 0x8000) != 0; uint bg = down ? accent.ToUint() : ToColor(20, 22, 28, 255); uint txtCol = down ? ToColor(0, 0, 0, 255) : ColTextDim.ToUint(); dl.AddRectFilled(new Vector2(x, y), new Vector2(x + keySize, y + keySize), bg, 3f); dl.AddRect(new Vector2(x, y), new Vector2(x + keySize, y + keySize), ToColor(40, 45, 55, 255), 3f); var sz = ImGui.CalcTextSize(label); dl.AddText(new Vector2(x + (keySize - sz.X) / 2, y + (keySize - sz.Y) / 2), txtCol, label); }
        DrawKey(wp.X + keySize + gap, wp.Y, "W", 0x57); DrawKey(wp.X, wp.Y + keySize + gap, "A", 0x41); DrawKey(wp.X + keySize + gap, wp.Y + keySize + gap, "S", 0x53); DrawKey(wp.X + 2 * (keySize + gap), wp.Y + keySize + gap, "D", 0x44);
        DrawKey(wp.X, wp.Y + 2 * (keySize + gap), "SH", 0x10); DrawKey(wp.X + keySize + gap, wp.Y + 2 * (keySize + gap), "SP", 0x20); DrawKey(wp.X + 2 * (keySize + gap), wp.Y + 2 * (keySize + gap), "L", 0x01); DrawKey(wp.X + 3 * (keySize + gap), wp.Y + 2 * (keySize + gap), "R", 0x02);

        if (!locked && _config.KeybindOverlayAlignMode == 0 && _showMenu)
        {
            Vector2 mouse = ImGui.GetMousePos(); bool hovered = mouse.X >= pos.X && mouse.X <= pos.X + w && mouse.Y >= pos.Y && mouse.Y <= pos.Y + h;
            if (hovered && ImGui.IsMouseClicked(ImGuiMouseButton.Left) && !_speedDragging && !_statusDragging) _keybindDragging = true;
            if (_keybindDragging) { if (ImGui.IsMouseDown(ImGuiMouseButton.Left)) { _config.KeybindOverlayPos = mouse - new Vector2(w / 2, h / 2); } else { _keybindDragging = false; ConfigManager.UpdateCache(_config); } }
        }
        ImGui.End(); ImGui.PopStyleColor(); ImGui.PopStyleVar(2);
    }

    void RenderModeOverlay(ImDrawListPtr dl)
    {
        if (!_config.ModeOverlay) return;
        var io = ImGui.GetIO(); Vector2 center = new Vector2(io.DisplaySize.X / 2f, io.DisplaySize.Y / 2f + _config.ModeOverlayOffsetY);
        string modeText = _config.AimLegitMode ? "LEGIT" : "RAGE"; uint modeColor = _config.AimLegitMode ? Colors.Green : Colors.Red; float fontSize = _config.ModeOverlaySize;
        float approxScale = fontSize / 15f; Vector2 textSize = ImGui.CalcTextSize(modeText) * approxScale; Vector2 textPos = center - textSize / 2f;
        Vector2 bgMin = textPos - new Vector2(8, 4); Vector2 bgMax = textPos + textSize + new Vector2(8, 4);
        dl.AddRectFilled(bgMin, bgMax, ToColor(0, 0, 0, 160), 4f); dl.AddText(ImGui.GetFont(), fontSize, textPos, modeColor, modeText);
    }

    private void UpdateMatrix()
    {
        if (_clientBase == IntPtr.Zero || (DateTime.Now - _lastModuleCheck).TotalSeconds > 5)
        {
            foreach (System.Diagnostics.ProcessModule m in _gameProcess.Process.Modules) if (m.ModuleName == "client.dll") { _clientBase = m.BaseAddress; break; }
            _lastModuleCheck = DateTime.Now;
        }
        if (_clientBase == IntPtr.Zero) return;
        try { for (int i = 0; i < 16; i++) _viewMatrix[i] = _gameProcess.Process.Read<float>(_clientBase + Offsets.dwViewMatrix + (i * 4)); } catch { }
    }

    private bool WorldToScreen(Vector3 worldPos, out Vector2 screenPos)
    {
        screenPos = Vector2.Zero; if (_viewMatrix.Length < 16) return false;
        float w = _viewMatrix[12] * worldPos.X + _viewMatrix[13] * worldPos.Y + _viewMatrix[14] * worldPos.Z + _viewMatrix[15]; if (w < 0.001f) return false;
        float invW = 1f / w; float x = (_viewMatrix[0] * worldPos.X + _viewMatrix[1] * worldPos.Y + _viewMatrix[2] * worldPos.Z + _viewMatrix[3]) * invW; float y = (_viewMatrix[4] * worldPos.X + _viewMatrix[5] * worldPos.Y + _viewMatrix[6] * worldPos.Z + _viewMatrix[7]) * invW;
        var io = ImGui.GetIO(); screenPos = new Vector2(io.DisplaySize.X / 2 + x * io.DisplaySize.X / 2, io.DisplaySize.Y / 2 - y * io.DisplaySize.Y / 2); return true;
    }

    private bool GetConfigBool(string name) { try { var prop = typeof(ConfigManager).GetProperty(name); if (prop != null) return (bool)prop.GetValue(_config)!; var field = typeof(ConfigManager).GetField(name); if (field != null) return (bool)field.GetValue(_config)!; } catch { } return false; }
    private void SetConfigBool(string name, bool value) { try { var prop = typeof(ConfigManager).GetProperty(name); if (prop != null && prop.CanWrite) prop.SetValue(_config, value); else { var field = typeof(ConfigManager).GetField(name); field?.SetValue(_config, value); } } catch { } }
    private float GetConfigFloat(string name, float fallback) { try { var prop = typeof(ConfigManager).GetProperty(name); if (prop != null) return (float)prop.GetValue(_config)!; var field = typeof(ConfigManager).GetField(name); if (field != null) return (float)field.GetValue(_config)!; } catch { } return fallback; }
    private void SetConfigFloat(string name, float value) { try { var prop = typeof(ConfigManager).GetProperty(name); if (prop != null && prop.CanWrite) prop.SetValue(_config, value); else { var field = typeof(ConfigManager).GetField(name); field?.SetValue(_config, value); } } catch { } }
    private int GetConfigInt(string name, int fallback) { try { var prop = typeof(ConfigManager).GetProperty(name); if (prop != null) return (int)prop.GetValue(_config)!; var field = typeof(ConfigManager).GetField(name); if (field != null) return (int)field.GetValue(_config)!; } catch { } return fallback; }
    private void SetConfigInt(string name, int value) { try { var prop = typeof(ConfigManager).GetProperty(name); if (prop != null && prop.CanWrite) prop.SetValue(_config, value); else { var field = typeof(ConfigManager).GetField(name); field?.SetValue(_config, value); } } catch { } }
    private Keys GetConfigKey(string name, Keys fallback) { try { var prop = typeof(ConfigManager).GetProperty(name); if (prop != null) return (Keys)prop.GetValue(_config)!; var field = typeof(ConfigManager).GetField(name); if (field != null) return (Keys)field.GetValue(_config)!; } catch { } return fallback; }
    private void SetConfigKey(string name, Keys value) { try { var prop = typeof(ConfigManager).GetProperty(name); if (prop != null && prop.CanWrite) prop.SetValue(_config, value); else { var field = typeof(ConfigManager).GetField(name); field?.SetValue(_config, value); } } catch { } }

    public void StartFeatures() { _bombTimer.Start(); _voteTeller.Start(); _triggerBot.Start(); _aimBot.Start(); _silentAim.Start(); _bhop.Start(); _rcs.Start(); }
    public void StopFeatures() { _bombTimer.Dispose(); _voteTeller.Dispose(); _triggerBot.Dispose(); _aimBot.Dispose(); _silentAim.Dispose(); _bhop.Dispose(); _rcs.Dispose(); }

    public static uint ToColor(byte r, byte g, byte b, byte a = 255) => ImGui.ColorConvertFloat4ToU32(new Vector4(r / 255f, g / 255f, b / 255f, a / 255f));
    public static uint ToColor(Vector4 c) => ImGui.ColorConvertFloat4ToU32(c);

    public static class Colors
    {
        public static readonly uint Yellow = ToColor(255, 255, 0, 255); public static readonly uint Black = ToColor(0, 0, 0, 255); public static readonly uint White = ToColor(255, 255, 255);
        public static readonly uint Red = ToColor(240, 50, 50); public static readonly uint Green = ToColor(46, 213, 115); public static readonly uint Blue = ToColor(30, 144, 255);
        public static readonly uint WhiteSmoke = ToColor(245, 245, 245); public static readonly uint DeepSkyBlue = ToColor(0, 191, 255); public static readonly uint LimeGreen = ToColor(50, 205, 50); public static readonly uint OrangeRed = ToColor(255, 69, 0);
    }
}