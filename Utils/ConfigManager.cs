using System.IO;
using System.Numerics;
using System.Text.Json;
using System.Text.Json.Serialization;
using Keys = Process.NET.Native.Types.Keys;

namespace CS2Cheat.Utils;

// УДАЛИТЬ дубли из Features/AimBot.cs - оставить только здесь
public enum AimMode { Legit = 0, Rage = 1, Silent = 2 }
public enum AimPriority { Distance = 0, Health = 1, Threat = 2, FOV = 3 }

public class WeaponAimConfig
{
    public float FOV { get; set; } = 90f;
    public float Smooth { get; set; } = 50f;
    public float MaxSpeed { get; set; } = 60f;
    public float MaxDistance { get; set; } = 5000f;
    public int PriorityBone { get; set; } = 0;
    public bool AutoShoot { get; set; } = false;
    public float Delay { get; set; } = 0f;
}

public class ConfigManager
{
    private const string ConfigFile = "config.json";
    private static ConfigManager? _cachedInstance;
    private static readonly object _cacheLock = new();
    private static FileSystemWatcher? _watcher;

    // Visuals

    public bool EspEnabled { get; set; } = true;

    // Speed Overlay Customization
    public bool SpeedOverlay { get; set; } = false;
    public bool SpeedOverlayLocked { get; set; } = false;
    public Vector2 SpeedOverlayPos { get; set; } = new(200, 200);
    public float SpeedOverlayBgAlpha { get; set; } = 0.9f;
    public float SpeedOverlayRounding { get; set; } = 8f;
    public bool SpeedOverlayShowBorder { get; set; } = true;
    public bool SpeedOverlayShowAccentStrip { get; set; } = true;
    public bool SpeedOverlayShowText { get; set; } = true; // "SPEED"
    public bool SpeedOverlayShowNumber { get; set; } = true; // само число
    public bool SpeedOverlayShowUnits { get; set; } = true; // "u/s"
    public bool SpeedOverlayShowGraph { get; set; } = true;

    // Status Panel Customization
    public bool StatusPanel { get; set; } = false;
    public bool StatusPanelLocked { get; set; } = false;
    public Vector2 StatusPanelPos { get; set; } = new(200, 400);
    public float StatusPanelBgAlpha { get; set; } = 0.9f;
    public float StatusPanelRounding { get; set; } = 8f;
    public bool StatusPanelShowBorder { get; set; } = true;
    public bool StatusPanelShowAccentStrip { get; set; } = true;
    public bool StatusPanelShowHeader { get; set; } = true; // "MODULES"
    public bool StatusPanelShowFPS { get; set; } = true;

    // Speed Overlay Sizes
    public float SpeedOverlayWidth { get; set; } = 180f;
    public float SpeedOverlayHeight { get; set; } = 100f;
    public float SpeedOverlayTextSize { get; set; } = 30f;
    public float SpeedOverlayGraphHeight { get; set; } = 28f;

    // Status Panel Sizes & Logic
    public float StatusPanelWidth { get; set; } = 170f;
    public float StatusPanelTextSize { get; set; } = 13f;
    public float StatusPanelItemSpacing { get; set; } = 5f;
    public bool StatusPanelShowOnlyActive { get; set; } = false;

    // Hitmarker Customization
    public Vector4 HitmarkerColor { get; set; } = new Vector4(1f, 1f, 1f, 1f);
    public float HitmarkerThickness { get; set; } = 2.5f;
    public float HitmarkerGap { get; set; } = 6f;
    public float HitmarkerLength { get; set; } = 18f;
    public float HitmarkerDuration { get; set; } = 0.25f;
    public float HitmarkerExpand { get; set; } = 8f;
    public bool HitmarkerOutline { get; set; } = true;
    public float HitmarkerOutlineAlpha { get; set; } = 0.5f;
    public bool HitmarkerSound { get; set; } = true;

    // Snowfall
    public bool Snowfall { get; set; } = false;
    public int SnowfallDensity { get; set; } = 100;
    public float SnowfallSpeed { get; set; } = 100f;
    public float SnowflakeSize { get; set; } = 2f;

    // Keybind Overlay
    public bool KeybindOverlay { get; set; } = false;
    public Vector2 KeybindOverlayPos { get; set; } = new(50, 200);
    public bool KeybindOverlayLocked { get; set; } = true;

    // Alignment
    public int OverlayAlignmentMode { get; set; } = 0; // 0=Free, 1=TopCenter, 2=BottomCenter
    public float OverlayVerticalOffset { get; set; } = 50f;

    // Aimbot Mode Overlay
    public bool ModeOverlay { get; set; } = false;
    public float ModeOverlaySize { get; set; } = 20f;
    public float ModeOverlayOffsetY { get; set; } = 30f;

    // Hitmarker Customization
    public int KeybindOverlayAlignMode { get; set; } = 0; // 0=Free, 1=TopCenter, 2=BottomCenter
    public float KeybindOverlayOffsetY { get; set; } = 50f;

    // Speed Overlay Alignment
    public int SpeedOverlayAlignMode { get; set; } = 0;
    public float SpeedOverlayOffsetY { get; set; } = 50f;

    // Status Panel Alignment
    public int StatusPanelAlignMode { get; set; } = 0;
    public float StatusPanelOffsetY { get; set; } = 50f;

    // Offscreen Arrows
    public bool OffscreenArrows { get; set; } = false;
    public bool AimAuraOutline { get; set; } = true;
    public float OffscreenArrowSize { get; set; } = 18f;
    public float OffscreenArrowMargin { get; set; } = 30f;
    public bool OffscreenArrowShowDistance { get; set; } = true;
    public float OffscreenArrowMaxDistance { get; set; } = 2000f;
    public int OffscreenArrowStyle { get; set; } = 0; // 0=Triangle, 1=Chevron, 2=Diamond, 3=Circle
    public int OffscreenArrowColorMode { get; set; } = 0; // 0=ByDistance, 1=Static, 2=ByVisibility
    public Vector4 OffscreenArrowColor { get; set; } = new Vector4(1f, 0.2f, 0.2f, 1f);
    public bool OffscreenArrowShowDormant { get; set; } = false;
    public bool OffscreenArrowOutline { get; set; } = true;
    public float OffscreenArrowTextSize { get; set; } = 12f;
    public bool OffscreenArrowFilled { get; set; } = true;
    public float OffscreenArrowThickness { get; set; } = 1f;

    // Aim Aura
    public bool AimAura { get; set; } = false;
    public int AimAuraStyle { get; set; } = 0; // 0=Ring, 1=Glow, 2=Crosshair, 3=Box, 4=Orbit
    public float AimAuraRadius { get; set; } = 35f;
    public float AimAuraThickness { get; set; } = 2f;
    public float AimAuraPulseSpeed { get; set; } = 3f;
    public float AimAuraPulseAmount { get; set; } = 8f;
    public Vector4 AimAuraColor { get; set; } = new Vector4(1f, 0f, 0f, 1f);
    public bool AimAuraFilled { get; set; } = false;
    public float AimAuraFillAlpha { get; set; } = 0.2f;
    public bool AimAuraShowName { get; set; } = false;
    public int AimAuraGlowRings { get; set; } = 3;
    public float AimAuraOrbitSpeed { get; set; } = 5f;
    public int AimAuraOrbitDots { get; set; } = 4;
    public float AimAuraCrosshairLen { get; set; } = 12f;
    public float AimAuraExpand { get; set; } = 5f;

    // Color
    public Vector4 OverlayAccentColor { get; set; } = new Vector4(0.0f, 0.55f, 1.0f, 1.0f);

    // Добавь эти поля в класс ConfigManager:
    public bool RcsEnabled { get; set; } = true;
    public bool TriggerAutoStop { get; set; } = false;
    public bool NoRecoil { get; set; } = false;
    public float TriggerDelay { get; set; } = 50f;
    public int TriggerAutoStopDelay { get; set; } = 10; // Задержка на нажатие контр-стрейфа (мс)
    public float RcsSmoothness { get; set; } = 1.5f;
    public bool RcsVerticalOnly { get; set; } = false;
    public float RcsStartDelay { get; set; } = 50f;
    public bool BulletTracers { get; set; } = true;
    public bool SilentAim { get; set; } = false;
    public float SilentAimFov { get; set; } = 5f;
    public int SilentAimBoneIndex { get; set; } = 0;
    public Keys SilentAimKey { get; set; } = Keys.LButton;
    public bool SoundEsp { get; set; } = true;
    public bool GrenadeTracker { get; set; } = true;
    public bool AimBacktrack { get; set; } = false;
    public bool AimMultipoint { get; set; } = true;
    public int AimHitChance { get; set; } = 100;
    public float AimReactionDelay { get; set; } = 0;
    public int AimMaxStep { get; set; } = 40;
    public bool AimHumanize { get; set; } = true;
    public bool AimPrediction { get; set; } = false;
    public bool AimDynamicSmooth { get; set; } = true;
    public bool AimEasing { get; set; } = true;
    public float AimDeadZone { get; set; } = 3f;
    public int AimStickiness { get; set; } = 200;
    public float[] EspEnemyColor { get; set; } = new[] { 1f, 0.2f, 0.2f, 1f };
    public float[] EspTeamColor { get; set; } = new[] { 0.2f, 0.6f, 1f, 1f };
    public float[] EspVisibleColor { get; set; } = new[] { 0.2f, 1f, 0.4f, 1f };
    public bool EspHealth { get; set; } = true;

    public bool AimVisibleOnly { get; set; } = true;
    public bool AimPrioritizeLowHp { get; set; } = false;
 
    public bool Hitmarker { get; set; } = false;
    public bool VisualsBulletTracers { get; set; } = true;
    public bool VisualsGrenadeHelper { get; set; } = true;
    public float EspMaxDistance { get; set; } = 3000f;
    public bool BombTracer { get; set; } = false;
    public bool BunnyHop { get; set; }
    public bool AutoStrafer { get; set; } = false;
    public bool EspVisibleCheck { get; set; } = true;
    public bool EspFilledBox { get; set; } = true;
    public bool EspBox { get; set; }
    public bool EspName { get; set; }
    public bool EspWeapon { get; set; }
    public bool EspFlags { get; set; }
    public float[] EspBoxColor { get; set; } = new float[] { 1f, 0f, 0f, 1f };
    public bool SkeletonEsp { get; set; }
    public bool EspAimCrosshair { get; set; }
    public bool BombTimer { get; set; }
    public bool VoteTeller { get; set; }
    public bool TeamCheck { get; set; } = true;

    // Legacy AimBot
    public bool AimBot { get; set; }
    public bool AimFovCircle { get; set; }
    public float AimFov { get; set; } = 15f;
    public float AimSmoothing { get; set; } = 3f;
    public int AimBoneIndex { get; set; }

    // ИСПРАВЛЕНО: AimRCS -> AimRcs для соответствия существующему коду
    public bool AimRcs { get; set; }
    public float AimRcsStrength { get; set; } = 100f;
    public bool AimLegitMode { get; set; } = true;
    public bool AimSilentFlick { get; set; } = false;

    // Extended AimBot Settings
    public bool AimBotEnabled { get; set; } = false;
    public AimMode AimMode { get; set; } = AimMode.Legit;
    public float AimSpeed { get; set; } = 100f;
    
    public int AimBonePriority { get; set; } = 0;
    public bool AimVisibilityCheck { get; set; } = true;

    public bool AimAdvancedPrediction { get; set; } = false;
    public bool AimMultipoints { get; set; } = true;
    public bool AimHumanizer { get; set; } = true;
    public float AimJitterAmount { get; set; } = 1.5f;
    public float AimRandomDelay { get; set; } = 0.3f;
    public int AimTargetSwitchDelay { get; set; } = 150;
    public int AimLockTime { get; set; } = 50;
    public AimPriority AimPriority { get; set; } = AimPriority.FOV;
    public float AimFovWeight { get; set; } = 1.0f;
    public float AimDistanceWeight { get; set; } = 0.5f;
    public float AimHealthWeight { get; set; } = 0.3f;
    public float AimFlashMaxAlpha { get; set; } = 0.4f;
    public bool AimWhileThrowing { get; set; } = false;
    public bool AimAtKnocked { get; set; } = false;
    public bool AimAutoScope { get; set; } = false;
    public bool AimAutoShoot { get; set; } = false;
    public float AimAutoShootDelay { get; set; } = 0f;
    public int AimKillDelay { get; set; } = 0;

    // RCS Extended
    public float AimRCSScaleX { get; set; } = 1.0f;
    public float AimRCSScaleY { get; set; } = 1.0f;
    public float AimRCSAmount { get; set; } = 100f;

    // Weapon Configs
    public WeaponAimConfig DefaultWeaponConfig { get; set; } = new()
    {
        FOV = 90f,
        Smooth = 50f,
        MaxSpeed = 60f,
        MaxDistance = 5000f,
        PriorityBone = 0
    };

    public Dictionary<string, WeaponAimConfig> WeaponConfigs { get; set; } = new()
    {
        ["weapon_ak47"] = new() { FOV = 90f, Smooth = 45f, MaxSpeed = 55f, MaxDistance = 5000f, PriorityBone = 0 },
        ["weapon_m4a1"] = new() { FOV = 90f, Smooth = 48f, MaxSpeed = 58f, MaxDistance = 5000f, PriorityBone = 0 },
        ["weapon_m4a1_silencer"] = new() { FOV = 90f, Smooth = 48f, MaxSpeed = 58f, MaxDistance = 5000f, PriorityBone = 0 },
        ["weapon_awp"] = new() { FOV = 30f, Smooth = 20f, MaxSpeed = 100f, MaxDistance = 10000f, PriorityBone = 0, AutoShoot = true },
        ["weapon_ssg08"] = new() { FOV = 40f, Smooth = 25f, MaxSpeed = 100f, MaxDistance = 8000f, PriorityBone = 0 },
        ["weapon_deagle"] = new() { FOV = 60f, Smooth = 30f, MaxSpeed = 80f, MaxDistance = 4000f, PriorityBone = 0 },
        ["weapon_usp_silencer"] = new() { FOV = 80f, Smooth = 60f, MaxSpeed = 50f, MaxDistance = 3000f, PriorityBone = 0 },
        ["weapon_glock"] = new() { FOV = 80f, Smooth = 60f, MaxSpeed = 50f, MaxDistance = 3000f, PriorityBone = 0 },
        ["weapon_p90"] = new() { FOV = 100f, Smooth = 70f, MaxSpeed = 40f, MaxDistance = 3500f, PriorityBone = 1 },
        ["weapon_mac10"] = new() { FOV = 110f, Smooth = 75f, MaxSpeed = 45f, MaxDistance = 3000f, PriorityBone = 1 },
    };

    // TriggerBot
    public bool TriggerBot { get; set; }
    public int TriggerFov { get; set; } = 3;
    public bool TriggerBurst { get; set; } = false;
    public int TriggerBurstShots { get; set; } = 3;

    // Keys
    [JsonConverter(typeof(KeysJsonConverter))]
    public Keys AimBotKey { get; set; } = Keys.XButton2;

    [JsonConverter(typeof(KeysJsonConverter))]
    public Keys AimLegitToggleKey { get; set; } = Keys.None;

    [JsonConverter(typeof(KeysJsonConverter))]
    public Keys TriggerBotKey { get; set; } = Keys.LMenu;

    [JsonConverter(typeof(KeysJsonConverter))]
    public Keys MenuToggleKey { get; set; } = Keys.Insert;

    // Color settings
    public float[] FovCircleColor { get; set; } = new float[] { 1f, 1f, 1f, 0.3f };
    public float[] SkeletonColor { get; set; } = new float[] { 0f, 1f, 0f, 1f };
    public float[] HeadDotColor { get; set; } = new float[] { 1f, 0f, 0f, 1f };

    [JsonIgnore]
    public static readonly string[] BoneNames = { "head", "neck_0", "spine_1", "pelvis" };

    [JsonIgnore]
    public static readonly string[] BoneDisplayNames = { "Head", "Neck", "Chest", "Pelvis" };

    [JsonIgnore]
    public static readonly string[] AimModeNames = { "Legit", "Rage", "Silent" };

    [JsonIgnore]
    public static readonly string[] PriorityNames = { "Distance", "Health", "Threat", "FOV" };

    public static ConfigManager Load()
    {
        lock (_cacheLock)
        {
            if (_cachedInstance != null) return _cachedInstance;
            _cachedInstance = LoadFromDisk();
            InitializeWatcher();
            return _cachedInstance;
        }
    }

    public static void Reload()
    {
        lock (_cacheLock)
        {
            _cachedInstance = LoadFromDisk();
            Console.WriteLine("[INFO] Config reloaded.");
        }
    }

    public static void UpdateCache(ConfigManager config)
    {
        lock (_cacheLock)
        {
            _cachedInstance = config;
        }
    }

    private static ConfigManager LoadFromDisk()
    {
        try
        {
            if (!File.Exists(ConfigFile))
            {
                var defaultOptions = Default();
                Save(defaultOptions);
                return defaultOptions;
            }

            var json = File.ReadAllText(ConfigFile);
            var options = JsonSerializer.Deserialize<ConfigManager>(json, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
                IncludeFields = true
            });
            var result = options ?? Default();
            SanitizeConfig(result);
            return result;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[ERROR] Config load failed: {ex.Message}");
            return Default();
        }
    }

    private static void SanitizeConfig(ConfigManager config)
    {
        if (config.MenuToggleKey == Keys.None) config.MenuToggleKey = Keys.Insert;
        if (config.AimBotKey == Keys.None) config.AimBotKey = Keys.XButton2;
        if (config.TriggerBotKey == Keys.None) config.TriggerBotKey = Keys.LMenu;
        if (config.AimFov <= 0) config.AimFov = 15f;
        if (config.AimSmoothing <= 0) config.AimSmoothing = 3f;

        if (config.WeaponConfigs == null || config.WeaponConfigs.Count == 0)
        {
            config.WeaponConfigs = Default().WeaponConfigs;
        }
    }

    private static void InitializeWatcher()
    {
        if (_watcher != null) return;
        try
        {
            var directory = Path.GetDirectoryName(Path.GetFullPath(ConfigFile)) ?? ".";
            var fileName = Path.GetFileName(ConfigFile);
            _watcher = new FileSystemWatcher(directory, fileName)
            {
                NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size,
                EnableRaisingEvents = true
            };
            _watcher.Changed += (_, _) => { Thread.Sleep(100); Reload(); };
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[WARN] Config watcher failed: {ex.Message}");
        }
    }

    public static void Save(ConfigManager options)
    {
        try
        {
            var json = JsonSerializer.Serialize(options, new JsonSerializerOptions
            {
                WriteIndented = true,
                IncludeFields = true
            });
            File.WriteAllText(ConfigFile, json);
            UpdateCache(options);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[ERROR] Failed to save config: {ex.Message}");
        }
    }

    public static ConfigManager Default()
    {
        return new ConfigManager
        {
            AimBot = true,
            BulletTracers = true,
            SoundEsp = false,
            GrenadeTracker = true,
            Hitmarker = false,
            HitmarkerSound = true,
            AimBotEnabled = true,
            AimMode = AimMode.Legit,
            AimFovCircle = true,
            AimFov = 15f,
            AimSmoothing = 3f,
            AimSpeed = 100f,
            AimBoneIndex = 0,
            AimBonePriority = 0,
            AimRcs = true,
            AimRcsStrength = 100f,
            AimRCSAmount = 100f,
            AimVisibilityCheck = true,
            AimPrediction = true,
            AimMultipoints = true,
            AimHumanizer = true,
            AimJitterAmount = 1.5f,
            AimTargetSwitchDelay = 150,
            AimLockTime = 50,
            BombTimer = true,
            VoteTeller = true,
            EspAimCrosshair = false,
            EspBox = true,
            EspName = true,
            EspWeapon = true,
            EspFlags = true,
            EspBoxColor = new float[] { 1f, 0f, 0f, 1f },
            SkeletonEsp = false,
            TriggerBot = true,
            TriggerDelay = 10,
            AimBotKey = Keys.XButton2,
            TriggerBotKey = Keys.LMenu,
            MenuToggleKey = Keys.Insert,
            TeamCheck = true,
        };
    }

    public static string GetKeyName(Keys key)
    {
        return key switch
        {
            Keys.LButton => "LMB",
            Keys.RButton => "RMB",
            Keys.MButton => "MMB",
            Keys.XButton1 => "Mouse4",
            Keys.XButton2 => "Mouse5",
            Keys.LMenu => "LAlt",
            Keys.RMenu => "RAlt",
            Keys.LShiftKey => "LShift",
            Keys.RShiftKey => "RShift",
            Keys.LControlKey => "LCtrl",
            Keys.RControlKey => "RCtrl",
            Keys.Insert => "Insert",
            Keys.Delete => "Delete",
            Keys.Home => "Home",
            Keys.End => "End",
            Keys.Capital => "CapsLock",
            _ => key.ToString()
        };
    }
}

public class KeysJsonConverter : JsonConverter<Keys>
{
    public override Keys Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.String)
        {
            var str = reader.GetString();
            if (Enum.TryParse<Keys>(str, true, out var result))
                return result;
        }
        else if (reader.TokenType == JsonTokenType.Number)
        {
            return (Keys)reader.GetInt32();
        }
        return Keys.None;
    }

    public override void Write(Utf8JsonWriter writer, Keys value, JsonSerializerOptions options)
    {
        writer.WriteStringValue(value.ToString());
    }
}