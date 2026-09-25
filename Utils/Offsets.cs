using System.Dynamic;
using Newtonsoft.Json.Linq;

namespace CS2Cheat.Utils;

public abstract class Offsets
{
    #region offsets

    public const float WeaponRecoilScale = 2f;
    public static int dwLocalPlayerPawn;
    public static int m_vOldOrigin;
    public static int m_vecViewOffset;
    public static int m_AimPunchAngle;
    public static int m_AimPunchCache;
    public static int m_pAimPunchServices;
    public static int m_vecCsViewPunchAngle;
    public static int m_modelState;
    public static int m_pGameSceneNode;
    public static int m_fFlags;
    public static int m_iIDEntIndex;
    public static int m_lifeState;
    public static int m_iHealth;
    public static int m_iTeamNum;
    public static int m_designerName;
    public static int dwEntityList;
    public static int m_bDormant;
    public static int m_iShotsFired;
    public static int m_hPawn;
    public static int dwLocalPlayerController;
    public static int dwViewMatrix;
    public static int dwViewAngles;
    public static int m_entitySpottedState;
    public static int m_Item;
    public static int m_pClippingWeapon;
    public static int m_AttributeManager;
    public static int m_iItemDefinitionIndex;
    public static int m_bIsScoped;
    public static int m_flFlashDuration;
    public static int m_iszPlayerName;
    public static int dwPlantedC4;
    public static int dwGlobalVars;
    public static int m_nBombSite;
    public static int m_bBombDefused;
    public static int m_vecAbsVelocity;
    public static int m_flDefuseCountDown;
    public static int m_flC4Blow;
    public static int m_bBeingDefused;
    public const nint m_nCurrentTickThisFrame = 0x34;

    public static int m_vOldViewAngles;
    public static int jump;

    // КНОПКИ ДЛЯ АВТОСТОПА
    public static int forward;
    public static int back;
    public static int left;
    public static int right;

    // НОВЫЕ ОФФСЕТЫ ДЛЯ BULLET TRACER & GRENADE TRAJECTORY
    public static int m_pWeaponServices;
    public static int m_hActiveWeapon;
    public static int m_fLastShotTime;
    public static int m_pBulletServices;
    public static int m_bulletData;

    public static readonly Dictionary<string, int> Bones = new()
    {
        { "head", 7 },
        { "neck_0", 6 },
        { "spine_1", 8 },
        { "spine_2", 3 },
        { "pelvis", 1 },
        { "arm_upper_L", 9 },
        { "arm_lower_L", 10 },
        { "hand_L", 11 },
        { "arm_upper_R", 13 },
        { "arm_lower_R", 14 },
        { "hand_R", 15 },
        { "leg_upper_L", 17 },
        { "leg_lower_L", 18 },
        { "ankle_L", 19 },
        { "leg_upper_R", 20 },
        { "leg_lower_R", 21 },
        { "ankle_R", 22 }
    };

    public static async Task UpdateOffsets()
    {
        try
        {
            string baseDir = AppDomain.CurrentDomain.BaseDirectory;
            string dumperFolder = Path.Combine(baseDir, "Dumper");
            string dumperExe = Path.Combine(dumperFolder, "cs2-dumper.exe");
            string outputDir = Path.Combine(dumperFolder, "output");

            string offsetsPath = Path.Combine(outputDir, "offsets.json");
            string clientPath = Path.Combine(outputDir, "client_dll.json");
            string buttonsPath = Path.Combine(outputDir, "buttons.json");

            if (File.Exists(dumperExe))
            {
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.WriteLine("[Offsets] Запуск локального сканирования памяти CS2...");

                var startInfo = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = dumperExe,
                    WorkingDirectory = dumperFolder,
                    CreateNoWindow = true,
                    UseShellExecute = false
                };

                using (var process = System.Diagnostics.Process.Start(startInfo))
                {
                    if (process != null)
                    {
                        await process.WaitForExitAsync();
                        Console.WriteLine("[Offsets] Сканирование успешно завершено.");
                    }
                }
            }
            else
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"[Offsets] Ошибка: {dumperExe} не найден! Попытка скачать из сети...");
            }

            string offsetsRaw, clientRaw, buttonsRaw;

            if (File.Exists(offsetsPath) && File.Exists(clientPath))
            {
                offsetsRaw = await File.ReadAllTextAsync(offsetsPath);
                clientRaw = await File.ReadAllTextAsync(clientPath);
                buttonsRaw = File.Exists(buttonsPath) ? await File.ReadAllTextAsync(buttonsPath) : "{}";
                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine("[Offsets] Данные успешно загружены из локального дампа.");
            }
            else
            {
                Console.ForegroundColor = ConsoleColor.DarkYellow;
                Console.WriteLine("[Offsets] Локальные файлы отсутствуют. Загрузка из GitHub...");
                HttpClientInstance.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0");
                offsetsRaw = await FetchJson("https://raw.githubusercontent.com/a2x/cs2-dumper/main/output/offsets.json");
                clientRaw = await FetchJson("https://raw.githubusercontent.com/a2x/cs2-dumper/main/output/client_dll.json");
                buttonsRaw = await FetchJson("https://raw.githubusercontent.com/a2x/cs2-dumper/main/output/buttons.json");
            }

            var jsonOffsets = JObject.Parse(offsetsRaw);
            var jsonClient = JObject.Parse(clientRaw);
            var jsonButtons = JObject.Parse(buttonsRaw);

            dynamic destData = new ExpandoObject();

            var clientDllToken = jsonOffsets["client.dll"] ?? jsonOffsets["clientdll"];
            var engine2DllToken = jsonOffsets["engine2.dll"] ?? jsonOffsets["engine2dll"];

            destData.dwBuildNumber = engine2DllToken?["dwBuildNumber"]?.Value<int>() ?? 0;
            destData.dwLocalPlayerController = clientDllToken?["dwLocalPlayerController"]?.Value<int>() ?? 0;
            destData.dwEntityList = clientDllToken?["dwEntityList"]?.Value<int>() ?? 0;
            destData.dwViewMatrix = clientDllToken?["dwViewMatrix"]?.Value<int>() ?? 0;
            destData.dwPlantedC4 = clientDllToken?["dwPlantedC4"]?.Value<int>() ?? 0;
            destData.dwLocalPlayerPawn = clientDllToken?["dwLocalPlayerPawn"]?.Value<int>() ?? 0;
            destData.dwViewAngles = clientDllToken?["dwViewAngles"]?.Value<int>() ?? 0;
            destData.dwGlobalVars = clientDllToken?["dwGlobalVars"]?.Value<int>() ?? 0;

            int GetField(string className, string fieldName)
            {
                var fieldsNode = jsonClient["client.dll"]?["classes"]?[className]?["fields"]
                                 ?? jsonClient["clientdll"]?["classes"]?[className]?["fields"];
                return fieldsNode?[fieldName]?.Value<int>() ?? 0;
            }

            destData.m_fFlags = GetField("C_BaseEntity", "m_fFlags");
            destData.m_vOldOrigin = GetField("C_BaseEntity", "m_vOldOrigin");
            destData.m_vecViewOffset = GetField("C_BaseModelEntity", "m_vecViewOffset");
            destData.m_aimPunchAngle = GetField("C_CSPlayerPawn", "m_aimPunchAngle");
            destData.m_aimPunchCache = GetField("C_CSPlayerPawn", "m_aimPunchCache");
            destData.m_pAimPunchServices = GetField("C_CSPlayerPawn", "m_pAimPunchServices");
            destData.m_vecCsViewPunchAngle = GetField("CPlayer_CameraServices", "m_vecCsViewPunchAngle");
            destData.m_modelState = GetField("CSkeletonInstance", "m_modelState");
            destData.m_pGameSceneNode = GetField("C_BaseEntity", "m_pGameSceneNode");
            destData.m_iIDEntIndex = GetField("C_CSPlayerPawn", "m_iIDEntIndex");
            destData.m_lifeState = GetField("C_BaseEntity", "m_lifeState");
            destData.m_iHealth = GetField("C_BaseEntity", "m_iHealth");
            destData.m_iTeamNum = GetField("C_BaseEntity", "m_iTeamNum");
            destData.m_designerName = GetField("CEntityInstance", "m_designerName");
            destData.m_bDormant = GetField("CGameSceneNode", "m_bDormant");
            destData.m_iShotsFired = GetField("C_CSPlayerPawn", "m_iShotsFired");
            destData.m_hPawn = GetField("CBasePlayerController", "m_hPawn");
            destData.m_entitySpottedState = GetField("C_CSPlayerPawn", "m_entitySpottedState");
            destData.m_Item = GetField("C_AttributeContainer", "m_Item");
            destData.m_pClippingWeapon = GetField("C_CSPlayerPawnBase", "m_pClippingWeapon");
            destData.m_AttributeManager = GetField("C_EconEntity", "m_AttributeManager");
            destData.m_iItemDefinitionIndex = GetField("C_EconItemView", "m_iItemDefinitionIndex");
            destData.m_bIsScoped = GetField("C_CSPlayerPawnBase", "m_bIsScoped");
            destData.m_flFlashDuration = GetField("C_CSPlayerPawnBase", "m_flFlashDuration");
            destData.m_iszPlayerName = GetField("CBasePlayerController", "m_iszPlayerName");
            destData.m_nBombSite = GetField("C_PlantedC4", "m_nBombSite");
            destData.m_bBombDefused = GetField("C_PlantedC4", "m_bBombDefused");
            destData.m_vecAbsVelocity = GetField("C_BaseEntity", "m_vecAbsVelocity");
            destData.m_flDefuseCountDown = GetField("C_PlantedC4", "m_flDefuseCountDown");
            destData.m_flC4Blow = GetField("C_PlantedC4", "m_flC4Blow");
            destData.m_bBeingDefused = GetField("C_PlantedC4", "m_bBeingDefused");
            destData.m_vOldViewAngles = GetField("C_CSPlayerPawn", "m_angEyeAngles");

            // ПАРСИНГ ПЕРЕМЕННЫХ ДЛЯ ТРЕЙСЕРОВ И СЕРВИСОВ ОРУЖИЯ
            destData.m_pWeaponServices = GetField("C_BasePlayerPawn", "m_pWeaponServices");
            destData.m_hActiveWeapon = GetField("CPlayer_WeaponServices", "m_hActiveWeapon");
            destData.m_fLastShotTime = GetField("C_CSWeaponBase", "m_fLastShotTime");
            destData.m_pBulletServices = GetField("C_CSPlayerPawn", "m_pBulletServices");

            // ВАЖНО: Имя класса должно быть точно как в дампере - CCSPlayer_BulletServices (с больших букв)
            // m_BulletChannels - это указатель на массив bullet_data (лежит по смещению 0x0 в CCSPlayer_BulletServices)
            int bulletDataOffset = GetField("CCSPlayer_BulletServices", "m_BulletChannels");
            if (bulletDataOffset == 0)
            {
                // Если дампер не нашел, пробуем альтернативное имя
                bulletDataOffset = GetField("CCSPlayer_BulletServices", "m_BulletChannels");
            }
            destData.m_bulletData = bulletDataOffset; // Обычно равно 0x0

            var jumpToken = jsonButtons["client.dll"]?["jump"] ?? jsonButtons["clientdll"]?["jump"];
            destData.jump = jumpToken?.Value<int>() ?? 0;

            // ПАРСИНГ КНОПОК ДЛЯ АВТОСТОПА
            var forwardToken = jsonButtons["client.dll"]?["forward"] ?? jsonButtons["clientdll"]?["forward"];
            destData.forward = forwardToken?.Value<int>() ?? 0;

            var backToken = jsonButtons["client.dll"]?["back"] ?? jsonButtons["clientdll"]?["back"];
            destData.back = backToken?.Value<int>() ?? 0;

            var leftToken = jsonButtons["client.dll"]?["left"] ?? jsonButtons["clientdll"]?["left"];
            destData.left = leftToken?.Value<int>() ?? 0;

            var rightToken = jsonButtons["client.dll"]?["right"] ?? jsonButtons["clientdll"]?["right"];
            destData.right = rightToken?.Value<int>() ?? 0;

            Console.WriteLine($"[Offsets] m_pBulletServices: 0x{destData.m_pBulletServices:X}");
            Console.WriteLine($"[Offsets] m_bulletData: 0x{destData.m_bulletData:X}");
            UpdateStaticFields(destData);
        }
        catch (Exception ex)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"[Offsets Updater Critical Error]: {ex.Message}");
            throw;
        }

    }

    private static readonly HttpClient HttpClientInstance = new();

    private static async Task<string> FetchJson(string url)
    {
        return await HttpClientInstance.GetStringAsync(url);
    }

    private static void UpdateStaticFields(dynamic data)
    {
        m_designerName = data.m_designerName;
        dwLocalPlayerPawn = data.dwLocalPlayerPawn;
        m_vOldOrigin = data.m_vOldOrigin;
        m_vecViewOffset = data.m_vecViewOffset;
        m_AimPunchAngle = data.m_aimPunchAngle;
        m_AimPunchCache = data.m_aimPunchCache;
        m_pAimPunchServices = data.m_pAimPunchServices;
        m_vecCsViewPunchAngle = data.m_vecCsViewPunchAngle;
        m_modelState = data.m_modelState;
        m_pGameSceneNode = data.m_pGameSceneNode;
        m_iIDEntIndex = data.m_iIDEntIndex;
        m_lifeState = data.m_lifeState;
        m_iHealth = data.m_iHealth;
        m_iTeamNum = data.m_iTeamNum;
        m_bDormant = data.m_bDormant;
        m_iShotsFired = data.m_iShotsFired;
        m_hPawn = data.m_hPawn;
        m_fFlags = data.m_fFlags;
        dwLocalPlayerController = data.dwLocalPlayerController;
        dwViewMatrix = data.dwViewMatrix;
        dwViewAngles = data.dwViewAngles;
        dwEntityList = data.dwEntityList;
        m_entitySpottedState = data.m_entitySpottedState;
        m_Item = data.m_Item;
        m_pClippingWeapon = data.m_pClippingWeapon;
        m_AttributeManager = data.m_AttributeManager;
        m_iItemDefinitionIndex = data.m_iItemDefinitionIndex;
        m_bIsScoped = data.m_bIsScoped;
        m_flFlashDuration = data.m_flFlashDuration;
        m_iszPlayerName = data.m_iszPlayerName;
        dwPlantedC4 = data.dwPlantedC4;
        dwGlobalVars = data.dwGlobalVars;
        m_nBombSite = data.m_nBombSite;
        m_bBombDefused = data.m_bBombDefused;
        m_vecAbsVelocity = data.m_vecAbsVelocity;
        m_flDefuseCountDown = data.m_flDefuseCountDown;
        m_flC4Blow = data.m_flC4Blow;
        m_bBeingDefused = data.m_bBeingDefused;
        jump = data.jump;

        // ИНИЦИАЛИЗАЦИЯ КНОПОК
        forward = data.forward;
        back = data.back;
        left = data.left;
        right = data.right;

        m_vOldViewAngles = data.m_vOldViewAngles;

        // Инициализация новых статических полей
        m_pWeaponServices = data.m_pWeaponServices;
        m_hActiveWeapon = data.m_hActiveWeapon;
        m_fLastShotTime = data.m_fLastShotTime;
        m_pBulletServices = data.m_pBulletServices;
        m_bulletData = data.m_bulletData;

        // Bullet Tracer
        m_pWeaponServices = data.m_pWeaponServices;
        m_hActiveWeapon = data.m_hActiveWeapon;
        m_fLastShotTime = data.m_fLastShotTime;
        m_pBulletServices = data.m_pBulletServices;
        m_bulletData = data.m_bulletData; // <-- ЭТО ОБЯЗАТЕЛЬНО ДОЛЖНО БЫТЬ

    }

    #endregion
}