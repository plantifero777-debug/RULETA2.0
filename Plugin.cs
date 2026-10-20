using System.Reflection;
using System.Text.Encodings.Web;
using System.Text.Json;
using Terraria;
using Terraria.DataStructures;
using Terraria.ID;
using TerrariaApi.Server;
using TShockAPI;

namespace RuletaPlugin;

public class RuletaState
{
    public int SecondsLeft { get; set; }
    public int YellowLevel { get; set; }
    public int YellowStreak { get; set; }
    public string LastColor { get; set; } = "";
    public List<string> ActiveMobs { get; set; } = new();
    public long SpawnBoostUntilUnix { get; set; }
    public int SpawnBoostStacks { get; set; }
}

public class EliteInfo
{
    public int Type;
    public string Kind = "";
    public int Drops;            // 0 = al azar entre EliteDropMin y EliteDropMax
    public bool Share;           // el loot va a todos los jugadores cercanos
    public int TargetPlayer = -1; // jugador al que persigue (cazador / marcado)
    public int Team = -1;         // equipo dueño del elite (-1 = ninguno)
    public int LootMult = 1;      // multiplicador del loot
    public int Cooldown;          // segundos hasta su proximo ataque a distancia
    public int Phase;             // fase del campeon (0, 1 o 2)
    public HashSet<string> Affixes = new(); // modificadores (regen, teleport, berserk, deathnova, swift, armored)
    public int TeleportCd;        // segundos hasta el proximo teletransporte (modificador teleport)
    public bool Enraged;          // el modificador berserk ya se activo
    public bool Boss;             // jefe final (prueba / apocalipsis): premio raro asegurado
    public double SpeedMult = 1;  // velocidad de ataque (solo bosses de la dificultad amarilla)
    public double Acc;            // ataques acumulados (bosses)
    public List<string> ShotNames = new(); // tipos de proyectil propios del jefe (vacio = todos los de la etapa)
}

[ApiVersion(2, 1)]
public class RuletaPlugin : TerrariaPlugin
{
    public override string Name => "Ruleta";
    public override string Author => "Servidor Skyblock";
    public override string Description => "Ruleta por colores que se activa sola cada 20 minutos.";
    public override Version Version => new(1, 2, 0);

    public RuletaPlugin(Main game) : base(game) { }

    // ------------------------------------------------------------------ estado
    private RuletaConfig cfg = new();
    private RuletaState st = new();
    private readonly Random rng = new();
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    private string ConfigPath => Path.Combine(TShock.SavePath, "ruleta.json");
    private string StatePath => Path.Combine(TShock.SavePath, "ruleta_estado.json");

    private int tick;
    private int saveCounter;
    private int orangeTimer;
    private int minionTimer;
    private bool spinning;
    private bool captured;
    private bool boostApplied;
    private int origSpawnRate = 600, origMaxSpawns = 5;

    // rojo
    private bool redActive;
    private string redKind = "";
    private bool bloodActive;
    private double extraSpawn = 1.0;

    // verde
    private bool furyActive;
    private bool greenBuffsActive;
    private int greenBuffTimer;
    private string markedName = "";
    private int countdownX, countdownY;
    private readonly HashSet<string> deaths = new();

    private readonly Dictionary<int, int> eventMobs = new();                 // naranja: indice -> tipo
    private readonly Dictionary<int, EliteInfo> elites = new();              // rojo: indice -> info
    private readonly Dictionary<int, (int type, int owner)> minions = new(); // minions de un elite
    private readonly Dictionary<string, int> crateCount = new();
    private readonly Dictionary<string, int> npcCache = new();
    private readonly Dictionary<string, int> itemCache = new();
    private List<(int sec, Action act)> pending = new();

    // rojo: disparos y lluvia
    private static readonly EntitySource_Misc ShotSource = new("Ruleta");
    private readonly Dictionary<string, int> projCache = new();
    private int shotsThisSecond;
    private bool rainActive;
    private int rainTimer;

    // bosses de la dificultad amarilla
    private readonly Dictionary<int, EliteInfo> bosses = new();   // bosses que disparan: indice -> info
    private bool suppressBoss;      // true mientras el plugin hace aparecer sus propios mobs
    private double shotScale = 1.0; // multiplicador temporal del dano de los proyectiles
    private int shotCap = int.MaxValue; // dano maximo de un proyectil (% de la vida del jugador mas debil)

    // azul y morado
    private string specialKind = "";
    private int specialTimer;
    private bool treasureSeen;

    // rojo: eventos nuevos
    private bool redElitesSeen;       // ya aparecieron elites en el evento rojo actual
    private int gauntletWave;          // oleadas lanzadas en la prueba
    private bool gauntletWaiting;      // descanso entre oleadas
    private bool apocStarted;

    private static readonly Dictionary<string, string> AffixLabel = new()
    {
        ["regen"] = "Regenerador", ["teleport"] = "Fantasma", ["berserk"] = "Berserker",
        ["deathnova"] = "Explosivo", ["swift"] = "Veloz", ["armored"] = "Blindado",
    };

    private static readonly Dictionary<string, string> AffixDesc = new()
    {
        ["regen"] = "recupera vida", ["teleport"] = "se teletransporta a ti", ["berserk"] = "se enfurece con poca vida",
        ["deathnova"] = "explota al morir", ["swift"] = "dispara mas rapido", ["armored"] = "mucha defensa",
    };

    // eventos rojos que terminan cuando mueren todos los elites
    private static readonly HashSet<string> EliteKillKinds = new()
    {
        "elite_each", "shared", "horde", "hunter", "weak_boss", "minions", "champion"
    };

    private static readonly Dictionary<string, string> RedTitle = new()
    {
        ["elite_each"] = "Un elite caza a cada jugador",
        ["shared"] = "Elite gigante por equipo",
        ["horde"] = "Horda elite",
        ["hunter"] = "El cazador",
        ["blood"] = "Noche de sangre",
        ["survival"] = "Supervivencia",
        ["weak_boss"] = "Boss sorpresa",
        ["countdown"] = "Cuenta atras de la muerte",
        ["minions"] = "Elite con minions",
        ["marked"] = "El marcado",
        ["champion"] = "El campeon",
        ["rain"] = "Lluvia de proyectiles",
        ["gauntlet"] = "La prueba (oleadas de elites)",
        ["apocalypse"] = "El heraldo del apocalipsis",
    };

    // bosses que solo pueden salir si ya los derrotaste (para no adelantar el progreso)
    private static readonly Dictionary<string, Func<bool>> BossDowned = new(StringComparer.OrdinalIgnoreCase)
    {
        ["King Slime"] = () => NPC.downedSlimeKing,
        ["Eye of Cthulhu"] = () => NPC.downedBoss1,
        ["Queen Bee"] = () => NPC.downedQueenBee,
        ["Skeletron"] = () => NPC.downedBoss3,
        ["Deerclops"] = () => NPC.downedDeerclops,
        ["Queen Slime"] = () => NPC.downedQueenSlime,
    };

    // ------------------------------------------------------------------ ciclo de vida
    public override void Initialize()
    {
        LoadConfig();
        LoadState();

        ServerApi.Hooks.GameUpdate.Register(this, OnUpdate);
        ServerApi.Hooks.GamePostInitialize.Register(this, OnPostInit);
        ServerApi.Hooks.NpcSpawn.Register(this, OnNpcSpawn);
        ServerApi.Hooks.NpcKilled.Register(this, OnNpcKilled);
        ServerApi.Hooks.NpcLootDrop.Register(this, OnLootDrop);
        GetDataHandlers.KillMe += OnKillMe;

        Commands.ChatCommands.Add(new Command("ruleta.use", CmdRuleta, "ruleta"));
        Commands.ChatCommands.Add(new Command("ruleta.use", CmdTiempo, "rt", "ruletatiempo"));
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            ServerApi.Hooks.GameUpdate.Deregister(this, OnUpdate);
            ServerApi.Hooks.GamePostInitialize.Deregister(this, OnPostInit);
            ServerApi.Hooks.NpcSpawn.Deregister(this, OnNpcSpawn);
            ServerApi.Hooks.NpcKilled.Deregister(this, OnNpcKilled);
            ServerApi.Hooks.NpcLootDrop.Deregister(this, OnLootDrop);
            GetDataHandlers.KillMe -= OnKillMe;
            Commands.ChatCommands.RemoveAll(c => c.CommandDelegate == CmdRuleta || c.CommandDelegate == CmdTiempo);
            SaveState();
            if (bloodActive) { Main.bloodMoon = false; }
            if (boostApplied) RestoreSpawns();
        }
        base.Dispose(disposing);
    }

    private void OnPostInit(EventArgs e) => ValidateNames();

    private void OnKillMe(object? sender, GetDataHandlers.KillMeEventArgs e)
    {
        if (e.Player != null) deaths.Add(e.Player.Name);
    }

    // ------------------------------------------------------------------ config / estado en disco
    private void LoadConfig()
    {
        try
        {
            if (File.Exists(ConfigPath))
                cfg = JsonSerializer.Deserialize<RuletaConfig>(File.ReadAllText(ConfigPath), JsonOpts) ?? new RuletaConfig();
            else
                cfg = new RuletaConfig();
            // si el .json es de una version anterior, anade los pesos nuevos que falten
            var defaults = new RuletaConfig();
            foreach (var kv in defaults.RedWeights) if (!cfg.RedWeights.ContainsKey(kv.Key)) cfg.RedWeights[kv.Key] = kv.Value;
            foreach (var kv in defaults.AffixWeights) if (!cfg.AffixWeights.ContainsKey(kv.Key)) cfg.AffixWeights[kv.Key] = kv.Value;
            foreach (var kv in defaults.BlueWeights) if (!cfg.BlueWeights.ContainsKey(kv.Key)) cfg.BlueWeights[kv.Key] = kv.Value;
            foreach (var kv in defaults.PurpleWeights) if (!cfg.PurpleWeights.ContainsKey(kv.Key)) cfg.PurpleWeights[kv.Key] = kv.Value;
            foreach (var kv in defaults.Weights) if (!cfg.Weights.ContainsKey(kv.Key)) cfg.Weights[kv.Key] = kv.Value;
            foreach (var kv in defaults.GreenWeights) if (!cfg.GreenWeights.ContainsKey(kv.Key)) cfg.GreenWeights[kv.Key] = kv.Value;
            File.WriteAllText(ConfigPath, JsonSerializer.Serialize(cfg, JsonOpts));
        }
        catch (Exception ex)
        {
            TShock.Log.ConsoleError($"[Ruleta] Error leyendo ruleta.json, uso valores por defecto: {ex.Message}");
            cfg = new RuletaConfig();
        }
        npcCache.Clear();
        itemCache.Clear();
        projCache.Clear();
    }

    private void LoadState()
    {
        try
        {
            if (File.Exists(StatePath))
                st = JsonSerializer.Deserialize<RuletaState>(File.ReadAllText(StatePath), JsonOpts) ?? new RuletaState();
        }
        catch { st = new RuletaState(); }
        if (st.SecondsLeft <= 0) st.SecondsLeft = cfg.IntervalMinutes * 60;
    }

    private void SaveState()
    {
        try { File.WriteAllText(StatePath, JsonSerializer.Serialize(st, JsonOpts)); }
        catch (Exception ex) { TShock.Log.ConsoleError($"[Ruleta] No se pudo guardar el estado: {ex.Message}"); }
    }

    private void ValidateNames()
    {
        int okMobs = cfg.OrangeMobs.Count(m => ResolveNpc(m.Name) > 0);
        foreach (var t in cfg.EliteMobTiers) foreach (var n in t.Mobs) ResolveNpc(n);
        foreach (var n in cfg.MinionMobs) ResolveNpc(n);
        foreach (var n in cfg.WeakBosses) ResolveNpc(n);
        foreach (var l in cfg.OrangeCrates.Concat(cfg.EliteLootTable).Concat(cfg.GreenGifts).Concat(cfg.RedRewardTable)
                     .Concat(cfg.EliteRareLootTable).Concat(cfg.GuaranteedHealing)) ResolveItem(l.Name);
        foreach (var s in cfg.EliteShots) ResolveProjectile(s.Name);
        TShock.Log.ConsoleInfo($"[Ruleta] Cargado. Mobs naranja validos: {okMobs}/{cfg.OrangeMobs.Count}.");
    }

    // ------------------------------------------------------------------ utilidades
    private void Say(string msg, byte r, byte g, byte b)
    {
        TSPlayer.All.SendMessage(msg, r, g, b);
        TShock.Log.ConsoleInfo(msg);
    }

    private void SayRed(string msg) => Say(msg, 255, 40, 40);

    private List<TSPlayer> Players() =>
        TShock.Players.Where(p => p != null && p.Active).ToList();

    private int Progress()
    {
        int p = 0;
        if (NPC.downedBoss1) p++;
        if (NPC.downedBoss2) p++;
        if (NPC.downedBoss3) p++;
        if (NPC.downedQueenBee) p++;
        if (Main.hardMode) p++;
        if (NPC.downedMechBossAny) p++;
        if (NPC.downedPlantBoss) p++;
        if (NPC.downedGolemBoss) p++;
        return p;
    }

    // Etapa del mundo para el rojo: de 0 a 14, cada jefe importante derrotado suma 1 hasta Moon Lord
    private const int RedStageMax = 14;

    private int RedStage()
    {
        bool[] flags =
        {
            NPC.downedBoss1, NPC.downedBoss2, NPC.downedBoss3, NPC.downedQueenBee, Main.hardMode,
            NPC.downedMechBoss1, NPC.downedMechBoss2, NPC.downedMechBoss3,
            NPC.downedPlantBoss, NPC.downedGolemBoss, NPC.downedFishron,
            NPC.downedEmpressOfLight, NPC.downedAncientCultist, NPC.downedMoonlord
        };
        return flags.Count(f => f);
    }

    private bool Busy()
    {
        if (Main.invasionType != 0 || Main.pumpkinMoon || Main.snowMoon) return true;
        for (int i = 0; i < Main.maxNPCs; i++)
            if (Main.npc[i].active && Main.npc[i].boss) return true;
        return false;
    }

    private int ResolveNpc(string name)
    {
        if (npcCache.TryGetValue(name, out var cached)) return cached;
        int id = 0;
        try
        {
            var list = TShock.Utils.GetNPCByIdOrName(name);
            var exact = list.FirstOrDefault(n => string.Equals(n.FullName, name, StringComparison.OrdinalIgnoreCase));
            if (exact != null) id = exact.netID;
            else if (list.Count == 1) id = list[0].netID;
        }
        catch { }
        npcCache[name] = id;
        if (id == 0) TShock.Log.ConsoleError($"[Ruleta] No se encontro el NPC '{name}', se ignora.");
        return id;
    }

    private int ResolveItem(string name)
    {
        if (itemCache.TryGetValue(name, out var cached)) return cached;
        int id = 0;
        try
        {
            var list = TShock.Utils.GetItemByIdOrName(name);
            var exact = list.FirstOrDefault(i => string.Equals(i.Name, name, StringComparison.OrdinalIgnoreCase));
            if (exact != null) id = exact.type;
        }
        catch { }
        itemCache[name] = id;
        if (id == 0) TShock.Log.ConsoleError($"[Ruleta] No se encontro el item '{name}', se ignora.");
        return id;
    }

    private T? PickWeighted<T>(List<T> items, Func<T, double> weight) where T : class
    {
        double total = items.Sum(weight);
        if (items.Count == 0 || total <= 0) return null;
        double roll = rng.NextDouble() * total;
        foreach (var it in items)
        {
            roll -= weight(it);
            if (roll <= 0) return it;
        }
        return items[^1];
    }

    private static void SyncNpc(NPC npc) =>
        NetMessage.SendData((int)PacketTypes.NpcUpdate, -1, -1, null, npc.whoAmI);

    private void ScaleNpc(NPC npc, double lifeMult, double dmgMult)
    {
        npc.lifeMax = (int)Math.Min(int.MaxValue / 2.0, Math.Max(1, npc.lifeMax * lifeMult));
        npc.life = npc.lifeMax;
        npc.damage = Math.Max(1, (int)(npc.damage * dmgMult));
        npc.defDamage = Math.Max(1, (int)(npc.defDamage * dmgMult));
        SyncNpc(npc);
    }

    private void RemoveNpc(int index)
    {
        var n = Main.npc[index];
        if (!n.active) return;
        n.active = false;
        n.type = 0;
        SyncNpc(n);
    }

    private List<int> SpawnTagged(int type, int tx, int ty, int rx, int ry)
    {
        var before = new HashSet<int>();
        for (int i = 0; i < Main.maxNPCs; i++)
            if (Main.npc[i].active && Main.npc[i].type == type) before.Add(i);

        suppressBoss = true; // los mobs del propio plugin no reciben el escalado de bosses de la dificultad
        try { TSPlayer.Server.SpawnNPC(type, Lang.GetNPCNameValue(type), 1, tx, ty, rx, ry); }
        finally { suppressBoss = false; }

        var result = new List<int>();
        for (int i = 0; i < Main.maxNPCs; i++)
            if (Main.npc[i].active && Main.npc[i].type == type && !before.Contains(i)) result.Add(i);
        return result;
    }

    private TSPlayer? Nearest(NPC npc, double maxTiles)
    {
        double nx = npc.position.X / 16.0, ny = npc.position.Y / 16.0;
        TSPlayer? best = null;
        double bestD = maxTiles * maxTiles;
        foreach (var p in Players())
        {
            double dx = p.TileX - nx, dy = p.TileY - ny, d = dx * dx + dy * dy;
            if (d <= bestD) { bestD = d; best = p; }
        }
        return best;
    }

    private void Safe(Action a)
    {
        try { a(); }
        catch (Exception ex) { TShock.Log.ConsoleError($"[Ruleta] Error: {ex}"); }
    }

    // ------------------------------------------------------------------ recompensas (escalan con el progreso)
    private int ExtraRewardDrops() =>
        cfg.RedRewardProgressPerExtraDrop > 0 ? RedStage() / cfg.RedRewardProgressPerExtraDrop : 0;

    private string GiveLoot(TSPlayer p, List<LootEntry> table, int count)
    {
        int prog = RedStage();
        double stackMult = 1.0 + prog * cfg.RedRewardStackBonusPerProgress;
        var pool = table.Where(l => l.MinProgress <= prog && ResolveItem(l.Name) > 0).ToList();
        var parts = new List<string>();

        for (int i = 0; i < count && pool.Count > 0; i++)
        {
            var l = PickWeighted(pool, x => x.Chance);
            if (l == null) break;
            pool.Remove(l);
            int amount = rng.Next(l.Min, l.Max + 1);
            if (l.Max > 1) amount = Math.Max(1, (int)Math.Round(amount * stackMult)); // solo items apilables
            p.GiveItem(ResolveItem(l.Name), amount);
            parts.Add($"{amount} x {l.Name}");
        }
        return string.Join(", ", parts);
    }

    private static string CoinText(long copper)
    {
        var parts = new List<string>();
        long plat = copper / 1_000_000, gold = copper / 10_000 % 100, silver = copper / 100 % 100;
        if (plat > 0) parts.Add($"{plat} platino");
        if (gold > 0) parts.Add($"{gold} oro");
        if (silver > 0) parts.Add($"{silver} plata");
        return parts.Count == 0 ? "" : string.Join(" ", parts);
    }

    private void GiveCoins(TSPlayer p, long copper)
    {
        long plat = copper / 1_000_000, gold = copper / 10_000 % 100, silver = copper / 100 % 100;
        if (plat > 0) p.GiveItem(ItemID.PlatinumCoin, (int)plat);
        if (gold > 0) p.GiveItem(ItemID.GoldCoin, (int)gold);
        if (silver > 0) p.GiveItem(ItemID.SilverCoin, (int)silver);
    }

    // Premio asegurado: monedas y pocion de curacion de la etapa actual (crece con el progreso del mundo)
    private string GiveGuaranteed(TSPlayer p, double mult)
    {
        int stage = RedStage();
        var parts = new List<string>();

        long silver = (long)((cfg.GuaranteedSilverBase + stage * cfg.GuaranteedSilverPerStage) * mult);
        if (silver > 0)
        {
            GiveCoins(p, silver * 100);
            string txt = CoinText(silver * 100);
            if (txt != "") parts.Add(txt);
        }

        var heal = cfg.GuaranteedHealing
            .Where(l => l.MinProgress <= stage && ResolveItem(l.Name) > 0)
            .OrderByDescending(l => l.MinProgress).FirstOrDefault();
        if (heal != null)
        {
            int amt = Math.Max(1, (int)Math.Round(rng.Next(heal.Min, heal.Max + 1) * Math.Max(1.0, mult)));
            p.GiveItem(ResolveItem(heal.Name), amt);
            parts.Add($"{amt} x {heal.Name}");
        }
        return string.Join(", ", parts);
    }

    // Objeto raro: varias tiradas independientes con la misma probabilidad (%)
    private string RollRare(TSPlayer p, double chancePct, int rolls)
    {
        var parts = new List<string>();
        for (int i = 0; i < Math.Max(1, rolls); i++)
        {
            if (rng.NextDouble() * 100.0 >= chancePct) continue;
            string s = GiveLoot(p, cfg.EliteRareLootTable, 1);
            if (s != "") parts.Add(s);
        }
        return string.Join(", ", parts);
    }

    private void RewardPlayer(TSPlayer p, string reason, int multiplier = 1, bool guaranteedRare = false)
    {
        int count = (cfg.RedRewardCount + ExtraRewardDrops()) * multiplier;
        var parts = new List<string>();

        string s = GiveLoot(p, cfg.RedRewardTable, count);
        if (s != "") parts.Add(s);
        string g = GiveGuaranteed(p, 0.6 * multiplier);
        if (g != "") parts.Add(g);
        string r = RollRare(p, guaranteedRare ? 100 : cfg.RedRewardRareChance * Math.Min(multiplier, 2), 1);
        if (r != "") parts.Add($"[RARO] {r}");

        if (parts.Count > 0) Say($"[RULETA] {p.Name} {reason} y obtuvo: {string.Join(", ", parts)}.", 255, 120, 120);
    }

    private IEnumerable<TSPlayer> Survivors() =>
        Players().Where(p => !p.Dead && !deaths.Contains(p.Name));

    // ------------------------------------------------------------------ amarillo helpers
    private int YellowCapLevels()
    {
        double pct = Math.Min(cfg.YellowMaxPercent, cfg.YellowBaseCapPercent + Progress() * cfg.YellowCapPerProgressPercent);
        return Math.Max(1, (int)(pct / cfg.YellowPercentPerLevel));
    }

    private double YellowMult() => 1.0 + st.YellowLevel * cfg.YellowPercentPerLevel / 100.0;

    private double SpawnFactor() =>
        (1.0 + Math.Min(st.SpawnBoostStacks * cfg.SpawnBoostPercent, cfg.SpawnBoostMaxPercent) / 100.0) * extraSpawn;

    private void UpdateSpawnBoost()
    {
        long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        if (st.SpawnBoostStacks > 0 && now >= st.SpawnBoostUntilUnix) st.SpawnBoostStacks = 0;

        if (st.SpawnBoostStacks > 0 || extraSpawn > 1.0)
        {
            double f = SpawnFactor();
            NPC.defaultSpawnRate = Math.Max(1, (int)(origSpawnRate / f));
            NPC.defaultMaxSpawns = (int)Math.Ceiling(origMaxSpawns * f);
            boostApplied = true;
        }
        else if (boostApplied)
        {
            RestoreSpawns();
        }
    }

    private void RestoreSpawns()
    {
        NPC.defaultSpawnRate = origSpawnRate;
        NPC.defaultMaxSpawns = origMaxSpawns;
        boostApplied = false;
    }

    // ------------------------------------------------------------------ bucle principal
    private void OnUpdate(EventArgs e)
    {
        if (++tick < 60) return;
        tick = 0;
        Safe(SecondTick);
    }

    private void SecondTick()
    {
        if (!captured)
        {
            origSpawnRate = NPC.defaultSpawnRate;
            origMaxSpawns = NPC.defaultMaxSpawns;
            captured = true;
        }

        // acciones programadas
        for (int i = 0; i < pending.Count; i++) pending[i] = (pending[i].sec - 1, pending[i].act);
        var due = pending.Where(p => p.sec <= 0).Select(p => p.act).ToList();
        pending.RemoveAll(p => p.sec <= 0);
        foreach (var a in due) Safe(a);

        CleanupTracked();
        UpdateSpawnBoost();
        RefreshShotCap();
        MinionsTick();
        TargetTick();
        EliteAttackTick();
        BossTick();
        RainTick();
        GauntletTick();
        ApocalypseTick();
        RedAutoFinishTick();
        SpecialTick();
        GreenBuffTick();

        if (++saveCounter >= 60) { saveCounter = 0; SaveState(); }

        // pausa: sin jugadores, jefes o invasiones
        if (Players().Count == 0 || Busy()) return;

        if (!spinning)
        {
            st.SecondsLeft--;
            if (st.SecondsLeft == cfg.WarnSeconds)
                Say($"[RULETA] La ruleta girara en {cfg.WarnSeconds} segundos...", 255, 215, 0);
            if (st.SecondsLeft <= 0) StartSpin();
        }

        OrangeTick();
    }

    private void CleanupTracked()
    {
        foreach (var k in eventMobs.Keys.ToList())
            if (!Main.npc[k].active || Main.npc[k].type != eventMobs[k]) eventMobs.Remove(k);
        foreach (var k in elites.Keys.ToList())
            if (!Main.npc[k].active || Main.npc[k].type != elites[k].Type) elites.Remove(k);
        foreach (var k in minions.Keys.ToList())
            if (!Main.npc[k].active || Main.npc[k].type != minions[k].type) minions.Remove(k);
    }

    // ------------------------------------------------------------------ ruleta
    private void StartSpin()
    {
        spinning = true;
        Say("[RULETA] La ruleta esta girando...", 255, 255, 255);
        pending.Add((Math.Max(1, cfg.SpinSeconds), ResolveSpin));
    }

    private void ResolveSpin()
    {
        if (furyActive || greenBuffsActive)
            Say("[RULETA] Los efectos verdes de la ruleta anterior terminan.", 80, 220, 80);
        furyActive = false;
        greenBuffsActive = false;

        string color = PickColor();
        RunColor(color);
        st.SecondsLeft = cfg.IntervalMinutes * 60;
        spinning = false;
        crateCount.Clear();
        SaveState();
    }

    private bool NothingActive() =>
        st.YellowLevel == 0 && st.ActiveMobs.Count == 0 && elites.Count == 0 &&
        st.SpawnBoostStacks == 0 && !redActive && specialKind == "";

    private string PickColor()
    {
        var w = new Dictionary<string, double>(cfg.Weights);
        foreach (var k in new[] { "yellow", "orange", "green", "red", "gray", "blue", "purple" })
            if (!w.ContainsKey(k)) w[k] = 0;

        if (specialKind != "") { w["blue"] = 0; w["purple"] = 0; }   // un solo evento azul/morado a la vez
        if (redActive) w["purple"] = 0;                              // el pacto/caos no se mezcla con un rojo
        if (st.LastColor == "red" || redActive) w["red"] = 0;    // nunca dos rojos seguidos
        if (st.YellowStreak >= 2) w["green"] *= 2;               // tras dos amarillos, mas verde
        if (NothingActive()) w["gray"] = 0;                      // reiniciar no tendria efecto
        else w["gray"] += cfg.GrayBoostAtMaxYellow * Math.Min(1.0, st.YellowLevel / (double)YellowCapLevels());

        var pick = PickWeighted(w.Keys.ToList(), k => w[k]);
        return pick ?? "green";
    }

    private void RunColor(string color)
    {
        switch (color)
        {
            case "yellow": DoYellow(); st.YellowStreak++; break;
            case "orange": DoOrange(); st.YellowStreak = 0; break;
            case "green": DoGreen(); st.YellowStreak = 0; break;
            case "red": DoRed(); st.YellowStreak = 0; break;
            case "gray": DoGray(); st.YellowStreak = 0; break;
            case "blue": DoBlue(null); st.YellowStreak = 0; break;
            case "purple": DoPurple(null); st.YellowStreak = 0; break;
        }
        st.LastColor = color;
    }

    // ------------------------------------------------------------------ AZUL (fortuna) y MORADO (caos)
    private void SayBlue(string msg) => Say(msg, 70, 160, 255);
    private void SayPurple(string msg) => Say(msg, 190, 90, 235);

    private static readonly string[] BlueKinds = { "treasure", "frenzy", "gamble" };
    private static readonly string[] PurpleKinds = { "chaos", "pact" };

    // eventos con duracion: se anotan en specialKind y terminan solos (o antes, si el evento lo permite)
    private void StartSpecial(string kind, int seconds, Action? onEnd)
    {
        specialKind = kind;
        specialTimer = 0;
        treasureSeen = false;
        deaths.Clear();
        pending.Add((Math.Max(1, seconds), () =>
        {
            if (specialKind != kind) return;
            onEnd?.Invoke();
            FinishSpecial();
        }));
    }

    private void FinishSpecial()
    {
        if (specialKind == "pact") { extraSpawn = 1.0; UpdateSpawnBoost(); }
        foreach (var kv in elites.Where(e => e.Value.Kind == "treasure").ToList())
        {
            RemoveNpc(kv.Key);
            elites.Remove(kv.Key);
        }
        specialKind = "";
        specialTimer = 0;
        treasureSeen = false;
    }

    // devuelve false si el evento pedido no existe (solo con /ruleta force)
    private bool DoBlue(string? forced)
    {
        string? pick = forced;
        if (pick == null)
        {
            var w = new Dictionary<string, double>(cfg.BlueWeights);
            foreach (var k in BlueKinds) if (!w.ContainsKey(k)) w[k] = 0;
            pick = PickWeighted(w.Keys.Where(k => BlueKinds.Contains(k)).ToList(), k => w[k]) ?? "gamble";
        }
        if (!BlueKinds.Contains(pick)) return false;

        switch (pick)
        {
            case "treasure": EventTreasure(); break;
            case "frenzy": EventFrenzy(); break;
            default: EventGamble(); break;
        }
        return true;
    }

    private bool DoPurple(string? forced)
    {
        string? pick = forced;
        if (pick == null)
        {
            var w = new Dictionary<string, double>(cfg.PurpleWeights);
            foreach (var k in PurpleKinds) if (!w.ContainsKey(k)) w[k] = 0;
            pick = PickWeighted(w.Keys.Where(k => PurpleKinds.Contains(k)).ToList(), k => w[k]) ?? "chaos";
        }
        if (!PurpleKinds.Contains(pick)) return false;

        if (pick == "pact") EventPact(); else EventChaos();
        return true;
    }

    // --- Azul 1: tesoro viviente
    private void EventTreasure()
    {
        var ply = RandomPlayer();
        double m = Math.Max(1.5, EliteBaseMult() * 0.5);
        int spawned = SpawnEliteNear(ply, "treasure", m, 0.25, cfg.EliteDropMax + 2, true, -1, null,
                                     "Tesoro viviente", -1, Math.Max(1, cfg.TreasureLootMult), true);
        if (spawned <= 0) { EventGamble(); return; }

        StartSpecial("treasure", cfg.TreasureMinutes * 60, () => SayBlue("[RULETA] El tesoro viviente escapo."));
        treasureSeen = true;
        SayBlue($"[RULETA] AZUL - TESORO VIVIENTE cerca de {ply.Name} (bloque {ply.TileX}, {ply.TileY}). " +
                $"Es debil y casi no hace dano, pero suelta loot enorme y un objeto raro. Tienen {cfg.TreasureMinutes} minutos.");
    }

    // --- Azul 2: frenesi de monedas
    private void EventFrenzy()
    {
        StartSpecial("frenzy", cfg.FrenzyMinutes * 60, () => SayBlue("[RULETA] El frenesi de monedas termina."));
        SayBlue($"[RULETA] AZUL - FRENESI DE MONEDAS durante {cfg.FrenzyMinutes} min: los enemigos que derroten pueden soltar monedas.");
    }

    private void FrenzyKill(NPC npc)
    {
        if (npc.friendly || npc.townNPC || npc.damage <= 0 || npc.lifeMax <= 5) return;
        if (rng.NextDouble() * 100.0 >= cfg.FrenzyChancePercent) return;
        var p = Nearest(npc, 100);
        if (p == null) return;

        long silver = cfg.FrenzySilverBase + RedStage() * cfg.FrenzySilverPerStage;
        silver = Math.Max(1, (long)(silver * (0.5 + rng.NextDouble())));
        GiveCoins(p, silver * 100);
        p.SendMessage($"[RULETA] Frenesi: +{CoinText(silver * 100)}", 70, 160, 255);
    }

    // --- Azul 3: cara o cruz
    private void EventGamble()
    {
        SayBlue("[RULETA] AZUL - CARA O CRUZ: cada jugador gana un premio grande o sufre un castigo leve.");
        int ticks = Math.Max(1, cfg.GambleDebuffSeconds) * 60;
        foreach (var p in Players().ToList())
        {
            if (p.Dead) continue;
            if (rng.NextDouble() * 100.0 < cfg.GambleWinPercent)
            {
                var parts = new List<string>();
                string g = GiveGuaranteed(p, cfg.GambleWinMult);
                if (g != "") parts.Add(g);
                string r = RollRare(p, cfg.GambleRareChance, 1);
                if (r != "") parts.Add($"[RARO] {r}");
                SayBlue($"[RULETA] {p.Name} gano la apuesta: {string.Join(", ", parts)}.");
            }
            else
            {
                p.SetBuff(BuffID.Weak, ticks);
                p.SetBuff(BuffID.Slow, ticks);
                string g = GiveGuaranteed(p, 0.3);   // premio de consuelo
                SayBlue($"[RULETA] {p.Name} perdio la apuesta: debilidad y lentitud {cfg.GambleDebuffSeconds} s (consuelo: {g}).");
            }
        }
    }

    // --- Morado 1: caos
    private static readonly int[] ChaosGood =
    {
        BuffID.Swiftness, BuffID.Regeneration, BuffID.Ironskin, BuffID.Wrath, BuffID.Rage,
        BuffID.Endurance, BuffID.Featherfall, BuffID.Shine, BuffID.Hunter, BuffID.Archery
    };
    private static readonly int[] ChaosBad = { BuffID.Slow, BuffID.Darkness, BuffID.Weak };

    private void EventChaos()
    {
        StartSpecial("chaos", cfg.ChaosSeconds, () =>
        {
            SayPurple("[RULETA] El caos termina. Los supervivientes reciben premio.");
            foreach (var p in Survivors().ToList()) RewardPlayer(p, "sobrevivio al caos");
        });
        SayPurple($"[RULETA] MORADO - CAOS durante {cfg.ChaosSeconds} s: cada {cfg.ChaosIntervalSeconds} s cada jugador recibe un efecto al azar, " +
                  "bueno o malo (siempre leve). Sobrevive para ganar premio.");
    }

    private void ChaosPulse()
    {
        int ticks = Math.Max(5, cfg.ChaosIntervalSeconds + 5) * 60;
        foreach (var p in Players().ToList())
        {
            if (p.Dead) continue;
            bool good = rng.NextDouble() * 100.0 < cfg.ChaosGoodPercent;
            var list = good ? ChaosGood : ChaosBad;
            int id = list[rng.Next(list.Length)];
            p.SetBuff(id, ticks);
            p.SendMessage($"[CAOS] {(good ? "Efecto bueno" : "Efecto malo")}: {Lang.GetBuffName(id)}", 190, 90, 235);
        }
    }

    // --- Morado 2: pacto de sangre
    private static readonly int[] PactBuffs = { BuffID.Wrath, BuffID.Rage, BuffID.Endurance, BuffID.Ironskin };

    private void EventPact()
    {
        extraSpawn = cfg.PactSpawnFactor + RedStage() * 0.1;
        UpdateSpawnBoost();
        StartSpecial("pact", cfg.PactMinutes * 60, () =>
        {
            SayPurple("[RULETA] El pacto de sangre termina. Los supervivientes reciben premio.");
            foreach (var p in Survivors().ToList()) RewardPlayer(p, "cumplio el pacto", Math.Max(1, cfg.PactRewardMult));
        });
        ApplyPactBuffs();
        SayPurple($"[RULETA] MORADO - PACTO DE SANGRE durante {cfg.PactMinutes} min: todos reciben buffs de combate " +
                  $"(Wrath, Rage, Endurance, Ironskin) pero aparecen muchos mas enemigos (x{extraSpawn:0.#}). Sobrevive para ganar premio doble.");
    }

    private void ApplyPactBuffs()
    {
        foreach (var p in Players().ToList())
        {
            if (p.Dead) continue;
            foreach (int id in PactBuffs) p.SetBuff(id, 30 * 60);
        }
    }

    private void SpecialTick()
    {
        if (specialKind == "") return;
        specialTimer++;
        switch (specialKind)
        {
            case "treasure":
                if (treasureSeen && !elites.Values.Any(e => e.Kind == "treasure"))
                {
                    SayBlue("[RULETA] El tesoro viviente fue derrotado.");
                    FinishSpecial();
                }
                break;
            case "chaos":
                if (specialTimer % Math.Max(1, cfg.ChaosIntervalSeconds) == 0) ChaosPulse();
                break;
            case "pact":
                if (specialTimer % 20 == 0) ApplyPactBuffs();
                break;
        }
    }

    // ------------------------------------------------------------------ AMARILLO
    private void DoYellow()
    {
        int cap = YellowCapLevels();
        if (st.YellowLevel < cap) st.YellowLevel++;
        st.SpawnBoostStacks++;
        st.SpawnBoostUntilUnix = DateTimeOffset.UtcNow.ToUnixTimeSeconds() + cfg.SpawnBoostMinutes * 60L;
        UpdateSpawnBoost();

        Say($"[RULETA] AMARILLO - Dificultad {st.YellowLevel}/{cap} (+{st.YellowLevel * cfg.YellowPercentPerLevel:0.#}% vida y dano). " +
            $"Mas enemigos durante {cfg.SpawnBoostMinutes} min.", 255, 230, 0);
        if (cfg.BossScalingEnabled)
            Say($"[RULETA] Bosses ahora: vida x{BossLifeMult():0.#}, velocidad de ataque x{BossSpeedMult():0.#} y proyectiles.", 255, 230, 0);
    }

    // ------------------------------------------------------------------ BOSSES (dificultad amarilla)
    private static bool IsBossLike(NPC npc) =>
        npc.boss ||
        npc.type == NPCID.EaterofWorldsHead || npc.type == NPCID.EaterofWorldsBody || npc.type == NPCID.EaterofWorldsTail ||
        npc.type == NPCID.MoonLordHead || npc.type == NPCID.MoonLordHand || npc.type == NPCID.MoonLordCore;

    // 0 en el nivel 1, 1 en el nivel maximo del momento
    private double BossT()
    {
        int cap = YellowCapLevels();
        if (cap <= 1) return 1.0;
        return Math.Clamp((st.YellowLevel - 1) / (double)(cap - 1), 0.0, 1.0);
    }

    private double BossLifeMult() => cfg.BossLifeMin + (cfg.BossLifeMax - cfg.BossLifeMin) * BossT();
    private double BossSpeedMult() => cfg.BossSpeedMin + (cfg.BossSpeedMax - cfg.BossSpeedMin) * BossT();

    private void ScaleBoss(NPC npc)
    {
        if (npc.realLife >= 0 && npc.realLife != npc.whoAmI) return; // segmentos que comparten la vida de la cabeza

        // sin dificultad amarilla: vida x1 y velocidad x1 (ya disparan proyectiles); con ella, sube segun el nivel
        bool scaled = st.YellowLevel > 0;
        double lifeM = scaled ? Math.Max(1.0, BossLifeMult()) : 1.0;
        double speedM = scaled ? Math.Max(1.0, BossSpeedMult()) : 1.0;
        if (lifeM > 1.0) ScaleNpc(npc, lifeM, 1.0); // solo vida: el dano de contacto no cambia, asi no matan de un golpe

        if (!cfg.BossShootEnabled || !npc.boss) return;
        if (bosses.Count >= cfg.BossMaxShooters) return;
        var info = new EliteInfo { Type = npc.type, Kind = "boss", SpeedMult = speedM, Cooldown = 0 };

        // tipos de proyectil distintos: de BossMinShotTypes a BossMaxShotTypes (max 5) segun el nivel de dificultad
        int minT = Math.Max(1, cfg.BossMinShotTypes), maxT = Math.Clamp(cfg.BossMaxShotTypes, minT, 5);
        int kinds = scaled ? minT + (int)Math.Round((maxT - minT) * BossT()) : minT;
        var pool = EligibleShots(RedStage()).Select(x => x.s.Name).Distinct().ToList();
        for (int i = pool.Count - 1; i > 0; i--)   // mezclar para que cada jefe tenga su propio conjunto
        {
            int j = rng.Next(i + 1);
            (pool[i], pool[j]) = (pool[j], pool[i]);
        }
        info.ShotNames = pool.Take(kinds).ToList();
        bosses[npc.whoAmI] = info;

        string types = info.ShotNames.Count > 0 ? $" {info.ShotNames.Count} tipo(s) de proyectil." : "";
        Say(scaled
            ? $"[RULETA] {Lang.GetNPCNameValue(npc.type)} se fortalece por la dificultad: vida x{lifeM:0.#}, ataque x{speedM:0.#} y dispara proyectiles.{types}"
            : $"[RULETA] {Lang.GetNPCNameValue(npc.type)} dispara proyectiles.{types} La dificultad amarilla sube su vida, su velocidad de ataque y sus tipos de proyectil.",
            255, 230, 0);
    }

    private void BossTick()
    {
        foreach (var k in bosses.Keys.ToList())
            if (!Main.npc[k].active || Main.npc[k].type != bosses[k].Type) bosses.Remove(k);
        if (bosses.Count == 0 || !cfg.BossShootEnabled) return;

        int stage = RedStage();
        foreach (var kv in bosses.ToList())
        {
            var info = kv.Value;
            var npc = Main.npc[kv.Key];

            double r = (double)npc.life / Math.Max(1, npc.lifeMax);
            info.Phase = r < 0.33 ? 2 : (r < 0.66 ? 1 : 0);

            // ataques acumulados: velocidad x10 con intervalo base de 4 s = 2.5 ataques por segundo
            info.Acc += info.SpeedMult / Math.Max(0.5, cfg.BossBaseIntervalSeconds);
            int n = Math.Min((int)info.Acc, Math.Max(1, cfg.BossMaxAttacksPerSecond));
            info.Acc -= Math.Floor(info.Acc);
            if (n <= 0) continue;

            var target = Nearest(npc, cfg.BossAttackRangeTiles);
            if (target == null || target.Dead) continue;

            // a mas velocidad de ataque, menos dano por proyectil (el total por segundo no se dispara)
            shotScale = cfg.BossShotDamageMult / (1.0 + cfg.BossShotSpeedFalloff * Math.Max(0.0, info.SpeedMult - 1.0));
            try { for (int i = 0; i < n; i++) EliteAttack(npc, info, target, stage); }
            finally { shotScale = 1.0; }
        }
    }

    private void RefreshShotCap()
    {
        var lives = Players().Where(p => !p.Dead && p.TPlayer != null).Select(p => p.TPlayer.statLifeMax2).ToList();
        int minLife = lives.Count > 0 ? lives.Min() : 100;
        shotCap = Math.Max(10, (int)(minLife * cfg.ShotMaxLifePercent / 100.0));
    }

    private void OnNpcSpawn(NpcSpawnEventArgs args)
    {
        var boss0 = Main.npc[args.NpcId];
        if (cfg.BossScalingEnabled && !suppressBoss &&
            boss0 != null && boss0.active && IsBossLike(boss0))
            Safe(() => ScaleBoss(boss0));

        double lifeM = st.YellowLevel > 0 ? YellowMult() : 1.0;
        double dmgM = lifeM * (bloodActive ? cfg.BloodNightDamageMult : 1.0);
        if (lifeM <= 1.0 && dmgM <= 1.0 && !furyActive) return;

        var npc = Main.npc[args.NpcId];
        if (npc == null || !npc.active || npc.friendly || npc.townNPC || npc.damage <= 0 || npc.lifeMax <= 5) return;

        // amarillo y noche de sangre no afectan a los bosses
        if (npc.boss) { lifeM = 1.0; dmgM = 1.0; }

        // furia verde: los jugadores hacen mas dano = los enemigos nuevos tienen menos vida
        if (furyActive) lifeM /= Math.Max(1.0, cfg.GreenFuryDamageMult);

        if (Math.Abs(lifeM - 1.0) < 0.001 && Math.Abs(dmgM - 1.0) < 0.001) return;
        ScaleNpc(npc, lifeM, dmgM);
    }

    // ------------------------------------------------------------------ NARANJA
    private bool Unlocked(OrangeMob m)
    {
        int tier = Math.Clamp(m.Tier, 0, cfg.OrangeTierUnlockProgress.Length - 1);
        return Progress() >= cfg.OrangeTierUnlockProgress[tier];
    }

    private void DoOrange()
    {
        var pool = cfg.OrangeMobs
            .Where(m => Unlocked(m) && !st.ActiveMobs.Contains(m.Name) && ResolveNpc(m.Name) > 0)
            .ToList();

        if (pool.Count == 0)
        {
            Say("[RULETA] NARANJA - No quedan mobs nuevos por desbloquear; los activos siguen en el mundo.", 255, 140, 0);
            return;
        }

        var mob = pool[rng.Next(pool.Count)];
        if (st.ActiveMobs.Count >= cfg.OrangeMaxActive)
        {
            Say($"[RULETA] {st.ActiveMobs[0]} deja de aparecer.", 255, 140, 0);
            st.ActiveMobs.RemoveAt(0);
        }
        st.ActiveMobs.Add(mob.Name);
        Say($"[RULETA] NARANJA - Nuevo mob: {mob.Name}. Activos: {st.ActiveMobs.Count}/{cfg.OrangeMaxActive}.", 255, 140, 0);
    }

    private int OrangeNear(TSPlayer p)
    {
        double r2 = (double)cfg.OrangeNearRadiusTiles * cfg.OrangeNearRadiusTiles;
        int n = 0;
        foreach (var k in eventMobs.Keys)
        {
            var npc = Main.npc[k];
            if (!npc.active) continue;
            double dx = p.TileX - npc.position.X / 16.0, dy = p.TileY - npc.position.Y / 16.0;
            if (dx * dx + dy * dy <= r2) n++;
        }
        return n;
    }

    private void OrangeTick()
    {
        if (st.ActiveMobs.Count == 0) return;
        if (++orangeTimer < cfg.OrangeSpawnIntervalSeconds) return;
        orangeTimer = 0;

        double lifeMult = cfg.OrangeLifeMult, dmgMult = cfg.OrangeDamageMult;
        foreach (var ply in Players())
        {
            if (ply.Dead) continue;
            int free = cfg.OrangeMaxPerPlayer - OrangeNear(ply);
            if (free <= 0) continue; // ya tiene el maximo cerca

            string name = st.ActiveMobs[rng.Next(st.ActiveMobs.Count)];
            int type = ResolveNpc(name);
            if (type <= 0) continue;

            // aparece a un lado del jugador, a cierta distancia (no encima de el)
            int side = rng.Next(2) == 0 ? -1 : 1;
            int tx = Math.Clamp(ply.TileX + side * cfg.OrangeSpawnOffsetTiles, 20, Main.maxTilesX - 20);
            foreach (int idx in SpawnTagged(type, tx, ply.TileY, cfg.OrangeSpawnRangeX, cfg.OrangeSpawnRangeY))
            {
                if (free-- <= 0) { RemoveNpc(idx); continue; } // nunca pasar del limite
                eventMobs[idx] = type;
                ScaleNpc(Main.npc[idx], lifeMult, dmgMult);
                if (cfg.OrangeRemoveDefense) StripArmor(Main.npc[idx]);
            }
        }
    }

    private void StripArmor(NPC npc)
    {
        npc.defense = 0;
        npc.defDefense = 0;
        npc.AddBuff(BuffID.Ichor, 60 * 3600); // -15 de defensa visible para los clientes, dura 1 hora
        NetMessage.SendData(54, -1, -1, null, npc.whoAmI); // sincroniza los buffs del NPC
    }

    private void RewardOrangeKill(NPC npc)
    {
        var ply = Nearest(npc, 150);
        if (ply == null) return;

        crateCount.TryGetValue(ply.Name, out int got);
        if (got >= cfg.OrangeCrateCapPerPlayerPerCycle) return;

        int prog = Progress();
        var eligible = cfg.OrangeCrates.Where(c => c.MinProgress <= prog).OrderBy(_ => rng.Next()).ToList();
        foreach (var c in eligible)
        {
            if (rng.NextDouble() * 100.0 >= c.Chance) continue;
            int id = ResolveItem(c.Name);
            if (id <= 0) continue;
            ply.GiveItem(id, 1);
            crateCount[ply.Name] = got + 1;
            ply.SendMessage($"[RULETA] Obtuviste: {c.Name}", 255, 140, 0);
            break;
        }
    }

    // ------------------------------------------------------------------ VERDE
    private void GreenBuffTick()
    {
        if (!greenBuffsActive) return;
        if (++greenBuffTimer < cfg.GreenBuffRefreshSeconds) return;
        greenBuffTimer = 0;
        ApplyGreenBuffs();
    }

    private void ApplyGreenBuffs()
    {
        int ticks = (cfg.GreenBuffRefreshSeconds + 90) * 60;
        foreach (var p in Players())
        {
            if (p.Dead) continue;
            p.SetBuff(BuffID.Regeneration, ticks);
            p.SetBuff(BuffID.Swiftness, ticks);
        }
    }

    private void DoGreen()
    {
        var w = new Dictionary<string, double>(cfg.GreenWeights);
        foreach (var k in new[] { "buffs", "gifts", "relief", "info", "fury" })
            if (!w.ContainsKey(k)) w[k] = 0;
        if (st.YellowLevel == 0) w["relief"] = 0;   // el alivio solo tiene sentido si hay dificultad
        if (furyActive) w["fury"] = 0;
        if (greenBuffsActive) w["buffs"] = 0;

        string pick = PickWeighted(w.Keys.ToList(), k => w[k]) ?? "gifts";

        switch (pick)
        {
            case "buffs":
                greenBuffsActive = true;
                greenBuffTimer = 0;
                ApplyGreenBuffs();
                Say("[RULETA] VERDE - Regeneracion y velocidad para todos hasta la proxima ruleta.", 80, 220, 80);
                break;

            case "gifts":
                int prog = Progress();
                var gifts = cfg.GreenGifts.Where(g => g.MinProgress <= prog && ResolveItem(g.Name) > 0).ToList();
                foreach (var p in Players())
                {
                    var g = PickWeighted(gifts, x => x.Chance);
                    if (g == null) continue;
                    int amount = rng.Next(g.Min, g.Max + 1);
                    p.GiveItem(ResolveItem(g.Name), amount);
                    p.SendMessage($"[RULETA] Regalo: {amount} x {g.Name}", 80, 220, 80);
                }
                Say("[RULETA] VERDE - Regalo de materiales para todos los jugadores.", 80, 220, 80);
                break;

            case "relief":
                st.YellowLevel--;
                Say($"[RULETA] VERDE - Alivio: la dificultad baja a {st.YellowLevel}/{YellowCapLevels()}.", 80, 220, 80);
                break;

            case "fury":
                furyActive = true;
                double mult = Math.Max(1.0, cfg.GreenFuryDamageMult);
                // enemigos que ya estan en el mundo: vida reducida (equivale a mas dano de los jugadores)
                for (int i = 0; i < Main.maxNPCs; i++)
                {
                    var npc = Main.npc[i];
                    if (!npc.active || npc.friendly || npc.townNPC || npc.damage <= 0 || npc.lifeMax <= 5) continue;
                    npc.lifeMax = Math.Max(1, (int)(npc.lifeMax / mult));
                    npc.life = Math.Max(1, (int)(npc.life / mult));
                    SyncNpc(npc);
                }
                Say($"[RULETA] VERDE - FURIA: el dano de los jugadores sube +{(mult - 1) * 100:0}% hasta la proxima ruleta.", 80, 220, 80);
                break;

            default:
                Say("[RULETA] VERDE - Aclaracion del estado actual:", 80, 220, 80);
                foreach (var line in StatusLines()) Say(line, 80, 220, 80);
                break;
        }
    }

    // ------------------------------------------------------------------ ROJO
    private double EliteBaseMult()
    {
        double t = Math.Min(1.0, RedStage() / (double)RedStageMax);
        return (cfg.EliteMinMult + (cfg.EliteMaxMult - cfg.EliteMinMult) * t) * cfg.RedDifficulty;
    }

    private string PickEliteName(int prog)
    {
        var tier = cfg.EliteMobTiers.Where(t => t.MinProgress <= prog).OrderByDescending(t => t.MinProgress).FirstOrDefault();
        var names = (tier?.Mobs ?? new List<string>()).Where(n => ResolveNpc(n) > 0).ToList();
        return names.Count == 0 ? "" : names[rng.Next(names.Count)];
    }

    private List<string> AvailableWeakBosses() =>
        cfg.WeakBosses.Where(n => BossDowned.TryGetValue(n, out var f) && f() && ResolveNpc(n) > 0).ToList();

    private bool RedAvailable(string kind) => kind switch
    {
        "weak_boss" => AvailableWeakBosses().Count > 0,
        "gauntlet" => RedStage() >= cfg.GauntletMinStage,
        "apocalypse" => RedStage() >= cfg.ApocalypseMinStage,
        _ => true
    };

    private void DoRed()
    {
        var kinds = cfg.RedWeights.Keys
            .Where(k => RedTitle.ContainsKey(k) && cfg.RedWeights[k] > 0 && RedAvailable(k))
            .ToList();
        string kind = PickWeighted(kinds, k => cfg.RedWeights[k]) ?? "elite_each";
        StartRed(kind);
    }

    private void StartRed(string kind)
    {
        redActive = true;
        redKind = kind;
        redElitesSeen = false;
        gauntletWave = 0;
        gauntletWaiting = false;
        apocStarted = false;
        deaths.Clear();

        SayRed($"[RULETA] ROJO - EVENTO DE LA MUERTE: {RedTitle[kind]}. Empieza en {cfg.EliteWarnSeconds} segundos. Preparense.");
        pending.Add((Math.Max(1, cfg.EliteWarnSeconds), () => RunRedEvent(kind)));
    }

    private void RunRedEvent(string kind)
    {
        if (!redActive || redKind != kind) return;
        if (Players().Count == 0) { FinishRed(); return; }

        switch (kind)
        {
            case "elite_each": EventEliteEach(); break;
            case "shared": EventShared(); break;
            case "horde": EventHorde(); break;
            case "hunter": EventHunter(); break;
            case "blood": EventBlood(); break;
            case "survival": EventSurvival(); break;
            case "weak_boss": EventWeakBoss(); break;
            case "countdown": EventCountdown(); break;
            case "minions": EventMinions(); break;
            case "marked": EventMarked(); break;
            case "champion": EventChampion(); break;
            case "rain": EventRain(); break;
            case "gauntlet": EventGauntlet(); break;
            case "apocalypse": EventApocalypse(); break;
            default: FinishRed(); break;
        }
    }

    private void ScheduleRedEnd(int seconds, Action? onEnd)
    {
        string kind = redKind;
        pending.Add((Math.Max(1, seconds), () =>
        {
            if (!redActive || redKind != kind) return;
            onEnd?.Invoke();
            FinishRed();
        }));
    }

    private void FinishRed()
    {
        DespawnElites();
        redActive = false;
        rainActive = false;
        redKind = "";
        markedName = "";
        redElitesSeen = false;
        gauntletWave = 0;
        gauntletWaiting = false;
        apocStarted = false;
    }

    // --- modificadores (afijos) de los elites
    private int AffixCount(string kind)
    {
        if (!cfg.AffixesEnabled || kind == "weak_boss" || kind == "treasure") return 0;
        int stage = RedStage();
        int n = cfg.AffixUnlockStages.Count(s => stage >= s);
        if (kind == "horde" || kind == "marked" || kind == "gauntlet") n = Math.Min(n, 1 + (kind == "gauntlet" ? gauntletWave / 2 : 0));
        if (kind == "champion" || kind == "apocalypse") n++;
        return Math.Min(n, cfg.AffixMaxCount);
    }

    private List<string> AssignAffixes(NPC npc, EliteInfo info, string kind)
    {
        var tags = new List<string>();
        int n = AffixCount(kind);
        var pool = cfg.AffixWeights.Where(kv => kv.Value > 0 && AffixLabel.ContainsKey(kv.Key)).Select(kv => kv.Key).ToList();

        for (int i = 0; i < n && pool.Count > 0; i++)
        {
            string? a = PickWeighted(pool, k => cfg.AffixWeights[k]);
            if (a == null) break;
            pool.Remove(a);
            info.Affixes.Add(a);
            tags.Add(AffixLabel[a]);

            if (a == "armored")
            {
                npc.defense += cfg.AffixArmorBase + RedStage() * cfg.AffixArmorPerStage;
                npc.defDefense = npc.defense;
            }
            else if (a == "teleport")
            {
                info.TeleportCd = cfg.AffixTeleportSeconds + rng.Next(0, 4);
            }
        }
        return tags;
    }

    private void AffixTick(NPC npc, EliteInfo info)
    {
        if (info.Affixes.Contains("regen") && npc.life < npc.lifeMax)
        {
            int heal = Math.Max(1, (int)(npc.lifeMax * cfg.AffixRegenPercentPerSecond / 100.0));
            npc.life = Math.Min(npc.lifeMax, npc.life + heal);
            npc.netUpdate = true;
        }

        if (info.Affixes.Contains("teleport") && --info.TeleportCd <= 0)
        {
            info.TeleportCd = cfg.AffixTeleportSeconds + rng.Next(0, 4);
            TeleportNearTarget(npc, info);
        }

        if (info.Affixes.Contains("berserk") && !info.Enraged &&
            npc.life < npc.lifeMax * cfg.AffixBerserkLifePercent / 100.0)
        {
            info.Enraged = true;
            info.Cooldown = 0; // ataca de inmediato
            SayRed($"[RULETA] {npc.GivenName} se enfurece: ataca el doble de rapido.");
        }
    }

    private void TeleportNearTarget(NPC npc, EliteInfo info)
    {
        var t = PickShotTarget(npc, info);
        if (t == null) return;

        for (int attempt = 0; attempt < 8; attempt++)
        {
            int dir = rng.Next(2) == 0 ? -1 : 1;
            int tx = t.TileX + dir * (10 + rng.Next(0, 10));
            int ty = t.TileY - rng.Next(0, 4);
            if (tx < 10 || tx > Main.maxTilesX - 10 || ty < 10 || ty > Main.maxTilesY - 10) continue;

            int w = Math.Max(1, npc.width / 16 + 1), h = Math.Max(1, npc.height / 16 + 1);
            bool clear = true;
            for (int x = tx - w / 2; x <= tx + w / 2 && clear; x++)
                for (int y = ty - h; y <= ty && clear; y++)
                    if (WorldGen.SolidTile(x, y)) clear = false;
            if (!clear) continue;

            npc.position.X = tx * 16f - npc.width / 2f;
            npc.position.Y = ty * 16f - npc.height;
            npc.velocity.X = 0f;
            npc.velocity.Y = 0f;
            npc.netUpdate = true;
            SyncNpc(npc);
            return;
        }
    }

    private int SpawnEliteNear(TSPlayer ply, string kind, double lifeMult, double dmgMult, int drops, bool share,
                               int targetPlayer = -1, string? forcedName = null, string? label = null,
                               int team = -1, int lootMult = 1, bool boss = false)
    {
        string name = forcedName ?? PickEliteName(RedStage());
        if (name == "") return 0;
        int type = ResolveNpc(name);
        if (type <= 0) return 0;

        int count = 0;
        var announced = new HashSet<string>();
        foreach (int idx in SpawnTagged(type, ply.TileX, ply.TileY, cfg.EliteSpawnRangeX, cfg.EliteSpawnRangeY))
        {
            var npc = Main.npc[idx];
            var info = new EliteInfo { Type = type, Kind = kind, Drops = drops, Share = share, TargetPlayer = targetPlayer, Team = team, LootMult = lootMult, Cooldown = rng.Next(2, 5), Boss = boss };
            var tags = AssignAffixes(npc, info, kind);   // antes de escalar: ScaleNpc sincroniza el NPC
            npc.GivenName = label ?? $"Elite {name}";
            if (tags.Count > 0) npc.GivenName += " [" + string.Join(", ", tags) + "]";
            ScaleNpc(npc, lifeMult, dmgMult);
            elites[idx] = info;
            count++;
            foreach (var a in info.Affixes) announced.Add(a);
        }

        if (count > 0)
        {
            redElitesSeen = true;
            if (announced.Count > 0 && kind != "horde" && kind != "marked")
                ply.SendMessage("[RULETA] Modificadores del elite: " +
                                string.Join(", ", announced.Select(a => $"{AffixLabel[a]} ({AffixDesc[a]})")) +
                                ". Mas peligroso, pero mejor loot.", 255, 140, 40);
        }
        return count;
    }

    private TSPlayer RandomPlayer()
    {
        var list = Players();
        return list[rng.Next(list.Count)];
    }

    // --- 1. Elite por jugador
    private void EventEliteEach()
    {
        double m = EliteBaseMult();
        int count = 0;
        foreach (var ply in Players())
        {
            int c = SpawnEliteNear(ply, "elite_each", m, m, 0, false);
            count += c;
            if (c > 0) ply.SendMessage($"[RULETA] Un elite te persigue. Vida y dano x{m:0.#}.", 255, 40, 40);
        }
        SayRed($"[RULETA] {count} elite(s) en el mundo. Duran {cfg.EliteTimeoutMinutes} min. Derrotalos para obtener loot.");
        ScheduleRedEnd(cfg.EliteTimeoutMinutes * 60, null);
    }

    // --- 2. Elite gigante por equipo
    private static readonly string[] TeamNames = { "sin equipo", "Rojo", "Verde", "Azul", "Amarillo", "Rosa" };
    private static string TeamName(int t) => t >= 0 && t < TeamNames.Length ? TeamNames[t] : $"equipo {t}";

    private void EventShared()
    {
        double m = EliteBaseMult();
        int groups = 0;

        foreach (var grp in Players().GroupBy(x => x.Team).ToList())
        {
            var members = grp.ToList();
            var ply = members[rng.Next(members.Count)];
            double lifeM = m * (1.0 + cfg.SharedLifePerExtraPlayer * (members.Count - 1));
            string tname = TeamName(grp.Key);

            int spawned = SpawnEliteNear(ply, "shared", lifeM, m, 0, true, -1, null,
                                         $"Elite equipo {tname}", grp.Key, Math.Max(1, cfg.SharedLootMult));
            if (spawned <= 0) continue;

            groups++;
            foreach (var mem in members)
                mem.SendMessage($"[RULETA] Un elite gigante aparecio para tu equipo ({tname}). Vida x{lifeM:0.#}. " +
                                $"Si lo derrotan, todo el equipo recibe el loot x{cfg.SharedLootMult}.", 255, 40, 40);
        }

        SayRed($"[RULETA] {groups} elite(s) gigante(s), uno por equipo. Duran {cfg.EliteTimeoutMinutes} min.");
        ScheduleRedEnd(cfg.EliteTimeoutMinutes * 60, null);
    }

    // --- 3. Horda elite
    private void EventHorde()
    {
        double m = Math.Max(1.5, EliteBaseMult() * cfg.HordeMultFactor);
        int total = 0;
        foreach (var ply in Players())
        {
            for (int i = 0; i < cfg.HordeCountPerPlayer + RedStage() / 4 && total < cfg.HordeMaxTotal; i++)
                total += SpawnEliteNear(ply, "horde", m, m, 1, false);
        }
        SayRed($"[RULETA] Horda de {total} elites en el mundo. Duran {cfg.EliteTimeoutMinutes} min.");
        ScheduleRedEnd(cfg.EliteTimeoutMinutes * 60, null);
    }

    // --- 4. Cazador
    private void EventHunter()
    {
        var ply = RandomPlayer();
        double m = EliteBaseMult();
        SpawnEliteNear(ply, "hunter", m, m, cfg.EliteDropMax, false, ply.Index);
        SayRed($"[RULETA] {ply.Name} es el cazado: un elite lo persigue. Los demas pueden ayudarle.");
        ScheduleRedEnd(cfg.EliteTimeoutMinutes * 60, null);
    }

    // --- 5. Noche de sangre
    private void EventBlood()
    {
        bloodActive = true;
        Main.bloodMoon = true;
        TSPlayer.Server.SetTime(false, 0.0);
        TSPlayer.All.SendData(PacketTypes.WorldInfo);
        SayRed($"[RULETA] NOCHE DE SANGRE durante {cfg.BloodNightMinutes} min: los enemigos nuevos hacen x{cfg.BloodNightDamageMult:0.#} de dano. Sobrevive para ganar premio.");
        ScheduleRedEnd(cfg.BloodNightMinutes * 60, EndBlood);
    }

    private void EndBlood()
    {
        if (!bloodActive) return;
        bloodActive = false;
        Main.bloodMoon = false;
        TSPlayer.All.SendData(PacketTypes.WorldInfo);
        SayRed("[RULETA] La noche de sangre termina.");
        foreach (var p in Survivors().ToList()) RewardPlayer(p, "sobrevivio a la noche de sangre");
    }

    // --- 6. Supervivencia
    private void EventSurvival()
    {
        extraSpawn = cfg.SurvivalSpawnFactor + RedStage() * 0.25;
        UpdateSpawnBoost();
        SayRed($"[RULETA] SUPERVIVENCIA: aguanten {cfg.SurvivalSeconds} segundos con muchos mas enemigos. Quien sobreviva gana premio.");
        ScheduleRedEnd(cfg.SurvivalSeconds, EndSurvival);
    }

    private void EndSurvival()
    {
        extraSpawn = 1.0;
        UpdateSpawnBoost();
        SayRed("[RULETA] Supervivencia terminada.");
        foreach (var p in Survivors().ToList()) RewardPlayer(p, "sobrevivio");
    }

    // --- 7. Boss sorpresa debil
    private void EventWeakBoss()
    {
        var names = AvailableWeakBosses();
        if (names.Count == 0) { EventEliteEach(); return; }

        string name = names[rng.Next(names.Count)];
        var players = Players();
        var ply = RandomPlayer();
        double lifeM = cfg.WeakBossLifeMult * (1.0 + cfg.WeakBossLifePerExtraPlayer * (players.Count - 1));
        SpawnEliteNear(ply, "weak_boss", lifeM, 1.0, cfg.EliteDropMax + 2, true, -1, name, $"{name} (debil)");
        SayRed($"[RULETA] Boss sorpresa: {name} debilitado cerca de {ply.Name}. Todos reciben loot si cooperan.");
        ScheduleRedEnd(cfg.EliteTimeoutMinutes * 60, null);
    }

    // --- 8. Cuenta atras de la muerte
    private void EventCountdown()
    {
        countdownX = cfg.CountdownTileX >= 0 ? cfg.CountdownTileX : Main.spawnTileX;
        countdownY = cfg.CountdownTileY >= 0 ? cfg.CountdownTileY : Main.spawnTileY;
        int secs = cfg.CountdownSeconds;

        SayRed($"[RULETA] CUENTA ATRAS: lleguen al punto ({countdownX}, {countdownY}) en {secs} segundos (radio {cfg.CountdownRadiusTiles}). Quien no llegue sufre debilidad.");
        foreach (int warn in new[] { 60, 30, 10 })
        {
            if (secs - warn <= 0) continue;
            int left = warn;
            pending.Add((secs - warn, () =>
            {
                if (redActive && redKind == "countdown") SayRed($"[RULETA] Quedan {left} segundos para llegar a ({countdownX}, {countdownY}).");
            }));
        }
        ScheduleRedEnd(secs, EndCountdown);
    }

    private void EndCountdown()
    {
        int r2 = cfg.CountdownRadiusTiles * cfg.CountdownRadiusTiles;
        foreach (var p in Players().ToList())
        {
            double dx = p.TileX - countdownX, dy = p.TileY - countdownY;
            bool arrived = !p.Dead && dx * dx + dy * dy <= r2;
            if (arrived)
            {
                RewardPlayer(p, "llego a tiempo");
            }
            else
            {
                p.SetBuff(BuffID.Weak, 3 * 3600);
                p.SetBuff(BuffID.Darkness, 3 * 3600);
                p.SendMessage("[RULETA] No llegaste a tiempo: debilidad y oscuridad durante 3 min.", 255, 40, 40);
            }
        }
        SayRed("[RULETA] La cuenta atras termino.");
    }

    // --- 9. Elite con minions
    private void EventMinions()
    {
        var ply = RandomPlayer();
        double m = EliteBaseMult() * 0.8;
        SpawnEliteNear(ply, "minions", m, m, cfg.EliteDropMax, false);
        SayRed($"[RULETA] Un elite invocador aparecio cerca de {ply.Name}. Mientras viva invoca enemigos; al matarlo desaparecen.");
        ScheduleRedEnd(cfg.EliteTimeoutMinutes * 60, null);
    }

    private void MinionsTick()
    {
        if (!elites.Values.Any(e => e.Kind == "minions")) { minionTimer = 0; return; }
        if (++minionTimer < cfg.MinionIntervalSeconds) return;
        minionTimer = 0;

        var names = MinionNames();
        if (names.Count == 0) return;

        foreach (var kv in elites.Where(e => e.Value.Kind == "minions").ToList())
        {
            var boss = Main.npc[kv.Key];
            if (!boss.active) continue;
            if (minions.Count(mn => mn.Value.owner == kv.Key) >= cfg.MinionCap) continue;

            int type = ResolveNpc(names[rng.Next(names.Count)]);
            int tx = (int)(boss.position.X / 16.0), ty = (int)(boss.position.Y / 16.0);
            foreach (int idx in SpawnTagged(type, tx, ty, 10, 6)) minions[idx] = (type, kv.Key);
        }
    }

    private void RemoveMinionsOf(int owner)
    {
        foreach (var k in minions.Where(m => m.Value.owner == owner).Select(m => m.Key).ToList())
        {
            RemoveNpc(k);
            minions.Remove(k);
        }
    }

    // --- 10. Marcado
    private void EventMarked()
    {
        var ply = RandomPlayer();
        markedName = ply.Name;
        double m = Math.Max(1.5, EliteBaseMult() * cfg.MarkedMultFactor);
        int total = 0;
        for (int i = 0; i < cfg.MarkedCount + RedStage() / 3; i++)
            total += SpawnEliteNear(ply, "marked", m, m, 1, false, ply.Index);

        SayRed($"[RULETA] {ply.Name} esta MARCADO: {total} elites lo rodean. Si sobrevive {cfg.MarkedSeconds} segundos gana premio doble. Los demas pueden ayudarle.");
        ScheduleRedEnd(cfg.MarkedSeconds, EndMarked);
    }

    private void EndMarked()
    {
        var p = Players().FirstOrDefault(x => x.Name == markedName);
        if (p != null && !p.Dead && !deaths.Contains(markedName))
        {
            Say($"[RULETA] {markedName} sobrevivio como marcado.", 255, 120, 120);
            RewardPlayer(p, "sobrevivio como marcado", 2);
        }
        else
        {
            SayRed($"[RULETA] {markedName} no logro sobrevivir como marcado.");
        }
    }

    // --- 11. Campeon: elite gigante con 3 fases
    private void EventChampion()
    {
        var players = Players();
        var ply = RandomPlayer();
        double m = EliteBaseMult();
        double lifeM = m * cfg.ChampionLifeMult * (1.0 + cfg.ChampionLifePerExtraPlayer * (players.Count - 1));
        int loot = Math.Max(1, cfg.ChampionLootMult);

        int spawned = SpawnEliteNear(ply, "champion", lifeM, m * 1.2, cfg.EliteDropMax + 3, true, -1, null, "Campeon", -1, loot);
        if (spawned <= 0) { EventEliteEach(); return; }

        SayRed($"[RULETA] EL CAMPEON aparecio cerca de {ply.Name}: vida x{lifeM:0.#}, 3 fases, disparos y refuerzos. " +
               $"Si lo derrotan, los jugadores cercanos reciben el loot x{loot}.");
        ScheduleRedEnd(cfg.ChampionMinutes * 60, null);
    }

    private void UpdateChampionPhase(int idx, NPC npc, EliteInfo info)
    {
        double r = (double)npc.life / Math.Max(1, npc.lifeMax);
        int phase = r < 0.33 ? 2 : (r < 0.66 ? 1 : 0);
        if (phase <= info.Phase) return;

        info.Phase = phase;
        info.Cooldown = 0; // ataca de inmediato
        SayRed(phase == 1
            ? "[RULETA] El campeon se enfurece: dispara mas rapido e invoca refuerzos."
            : "[RULETA] El campeon esta al limite: furia total.");

        int stage = RedStage();
        var shots = EligibleShots(stage);
        if (shots.Count > 0)
        {
            var shot = shots[rng.Next(shots.Count)];
            FireNova(npc, shot, stage, 14 + phase * 4);
        }
        SpawnMinionsFor(idx, npc, cfg.ChampionAddsPerPhase);
    }

    // --- 12. Lluvia de proyectiles
    private void EventRain()
    {
        rainActive = true;
        rainTimer = 0;
        SayRed($"[RULETA] LLUVIA DE PROYECTILES durante {cfg.RainSeconds} segundos: caen flechas, aguijones y laseres del cielo. Sobrevive para ganar premio.");
        ScheduleRedEnd(cfg.RainSeconds, EndRain);
    }

    private void RainTick()
    {
        if (!rainActive) return;
        bool apoc = redKind == "apocalypse";
        int interval = apoc ? cfg.RainIntervalSeconds * Math.Max(1, cfg.ApocalypseRainSlowdown) : cfg.RainIntervalSeconds;
        if (++rainTimer < interval) return;
        rainTimer = 0;

        int stage = RedStage();
        var shots = EligibleShots(stage);
        if (shots.Count == 0) return;

        int perPlayer = cfg.RainBaseShots + stage / 4;
        if (apoc) perPlayer = Math.Max(1, perPlayer * 2 / 3);
        foreach (var p in Players())
        {
            if (p.Dead) continue;
            double px = p.TPlayer.Center.X, py = p.TPlayer.Center.Y;
            for (int i = 0; i < perPlayer; i++)
            {
                var shot = shots[rng.Next(shots.Count)];
                double x = px + (rng.NextDouble() * 2 - 1) * 16 * 28;          // +-28 bloques
                double y = py - 16 * (34 + rng.Next(0, 10));                   // por encima de la pantalla
                double angle = Math.PI / 2 + (rng.NextDouble() - 0.5) * 0.5;   // hacia abajo
                FireShot(shot.id, x, y, angle, shot.s.Speed * 0.9, ShotDamage(stage, shot.s.DamageMult * 0.8));
            }
        }
    }

    private void EndRain()
    {
        rainActive = false;
        SayRed("[RULETA] La lluvia de proyectiles termina.");
        foreach (var p in Survivors().ToList()) RewardPlayer(p, "sobrevivio a la lluvia");
    }

    // --- 13. La prueba: oleadas de elites + jefe final
    private void EventGauntlet()
    {
        gauntletWave = 0;
        gauntletWaiting = false;
        int waves = Math.Max(1, cfg.GauntletWaves);
        SayRed($"[RULETA] LA PRUEBA: {waves} oleadas de elites, cada una mas dura, y un jefe al final. " +
               "Sobrevive a todas para un premio enorme (con objeto raro asegurado).");
        SpawnGauntletWave();
        if (!redActive) return;
        ScheduleRedEnd(waves * cfg.GauntletWaveMinutes * 60, () => SayRed("[RULETA] Se acabo el tiempo de la prueba."));
    }

    private void SpawnGauntletWave()
    {
        gauntletWave++;
        int waves = Math.Max(1, cfg.GauntletWaves);
        bool last = gauntletWave >= waves;
        double baseM = EliteBaseMult();
        double m = Math.Max(1.5, baseM * (0.55 + 0.25 * gauntletWave));
        int perPlayer = gauntletWave + RedStage() / 6;
        var players = Players();
        int total = 0;

        foreach (var ply in players)
            for (int i = 0; i < perPlayer && total < cfg.HordeMaxTotal; i++)
                total += SpawnEliteNear(ply, "gauntlet", m, m, 1, false);

        if (last && players.Count > 0)
        {
            var ply = RandomPlayer();
            double lifeM = baseM * cfg.ChampionLifeMult * 0.8 * (1.0 + cfg.ChampionLifePerExtraPlayer * 0.5 * (players.Count - 1));
            total += SpawnEliteNear(ply, "gauntlet", lifeM, baseM * 1.2, cfg.EliteDropMax + 2, true, -1, null,
                                    "Jefe de la prueba", -1, 2, true);
        }

        if (total <= 0)
        {
            SayRed("[RULETA] La prueba no pudo empezar (no hay enemigos validos).");
            FinishRed();
            return;
        }
        SayRed(last
            ? $"[RULETA] OLEADA FINAL ({gauntletWave}/{waves}): {total} elites y el jefe de la prueba. Elites x{m:0.#}."
            : $"[RULETA] OLEADA {gauntletWave}/{waves}: {total} elites. Vida y dano x{m:0.#}.");
    }

    private void GauntletTick()
    {
        if (!redActive || redKind != "gauntlet" || gauntletWaiting || gauntletWave == 0) return;
        if (elites.Values.Any(e => e.Kind == "gauntlet")) return;

        if (gauntletWave >= Math.Max(1, cfg.GauntletWaves)) { EndGauntlet(); return; }

        gauntletWaiting = true;
        int delay = Math.Max(1, cfg.GauntletWaveDelaySeconds);
        SayRed($"[RULETA] Oleada {gauntletWave} superada. La siguiente llega en {delay} segundos.");
        pending.Add((delay, () =>
        {
            gauntletWaiting = false;
            if (redActive && redKind == "gauntlet") SpawnGauntletWave();
        }));
    }

    private void EndGauntlet()
    {
        SayRed("[RULETA] PRUEBA SUPERADA. Los supervivientes reciben su premio.");
        foreach (var p in Survivors().ToList())
            RewardPlayer(p, "supero la prueba", Math.Max(1, cfg.GauntletRewardMult), true);
        FinishRed();
    }

    // --- 14. Heraldo del apocalipsis: campeon gigante + lluvia constante de proyectiles
    private void EventApocalypse()
    {
        var players = Players();
        var ply = RandomPlayer();
        double m = EliteBaseMult();
        double lifeM = m * cfg.ApocalypseLifeMult * (1.0 + cfg.ChampionLifePerExtraPlayer * (players.Count - 1));
        int loot = Math.Max(1, cfg.ApocalypseLootMult);

        int spawned = SpawnEliteNear(ply, "apocalypse", lifeM, m * 1.3, cfg.EliteDropMax + 4, true, -1, null,
                                     "Heraldo del Apocalipsis", -1, loot, true);
        if (spawned <= 0) { EventChampion(); return; }

        apocStarted = true;
        rainActive = true;
        rainTimer = 0;
        SayRed($"[RULETA] EL HERALDO DEL APOCALIPSIS aparecio cerca de {ply.Name}: vida x{lifeM:0.#}, 3 fases, refuerzos y " +
               $"lluvia de proyectiles constante. Loot x{loot} con objeto raro asegurado. Tienen {cfg.ApocalypseMinutes} minutos.");
        ScheduleRedEnd(cfg.ApocalypseMinutes * 60, null);
    }

    private void ApocalypseTick()
    {
        if (!redActive || redKind != "apocalypse" || !apocStarted) return;
        if (elites.Values.Any(e => e.Kind == "apocalypse")) return;

        apocStarted = false;
        rainActive = false;
        SayRed("[RULETA] El heraldo cae y la lluvia se detiene. Los supervivientes reciben un bonus.");
        foreach (var p in Survivors().ToList()) RewardPlayer(p, "sobrevivio al apocalipsis");
        FinishRed();
    }

    // Los eventos de elites terminan en cuanto mueren todos (no hace falta esperar al tiempo limite)
    private void RedAutoFinishTick()
    {
        if (!redActive || !redElitesSeen || !EliteKillKinds.Contains(redKind)) return;
        if (elites.Count > 0) return;
        SayRed("[RULETA] Todos los elites han caido. Evento rojo superado.");
        FinishRed();
    }

    // ------------------------------------------------------------------ disparos de los elites
    // Los elites disparan aunque el mob original no dispare nada: el plugin crea los proyectiles.
    private int ResolveProjectile(string name)
    {
        if (projCache.TryGetValue(name, out var cached)) return cached;
        int id = -1;
        try
        {
            var f = typeof(ProjectileID).GetField(name, BindingFlags.Public | BindingFlags.Static);
            if (f != null)
            {
                object? v = f.IsLiteral ? f.GetRawConstantValue() : f.GetValue(null);
                if (v != null) id = Convert.ToInt32(v);
            }
        }
        catch { }
        projCache[name] = id;
        if (id < 0) TShock.Log.ConsoleError($"[Ruleta] No se encontro el proyectil '{name}', se ignora.");
        return id;
    }

    private List<(int id, ShotType s)> EligibleShots(int stage)
    {
        var list = new List<(int id, ShotType s)>();
        foreach (var s in cfg.EliteShots)
        {
            if (s.MinStage > stage) continue;
            int id = ResolveProjectile(s.Name);
            if (id >= 0) list.Add((id, s));
        }
        return list;
    }

    private int ShotDamage(int stage, double dmgMult) =>
        Math.Max(1, (int)((cfg.ShotDamageBase + stage * cfg.ShotDamagePerStage) * dmgMult * cfg.RedDifficulty * shotScale));

    private void FireShot(int type, double x, double y, double angle, double speed, int damage)
    {
        if (shotsThisSecond >= cfg.MaxShotsPerSecond) return;
        shotsThisSecond++;
        damage = Math.Min(damage, shotCap);   // nunca mas que un % de la vida del jugador mas debil
        float vx = (float)(Math.Cos(angle) * speed);
        float vy = (float)(Math.Sin(angle) * speed);
        Projectile.NewProjectile(ShotSource, (float)x, (float)y, vx, vy, type, damage, 1f, Main.myPlayer);
    }

    private void FireNova(NPC npc, (int id, ShotType s) shot, int stage, int count, double speedMult = 0.8, double? startAngle = null)
    {
        double cx = npc.Center.X, cy = npc.Center.Y;
        double start = startAngle ?? rng.NextDouble() * Math.PI * 2;
        int dmg = ShotDamage(stage, shot.s.DamageMult);
        for (int i = 0; i < count; i++)
            FireShot(shot.id, cx, cy, start + i * 2 * Math.PI / count, shot.s.Speed * speedMult, dmg);
    }

    private TSPlayer? PickShotTarget(NPC npc, EliteInfo info)
    {
        if (info.TargetPlayer >= 0)
        {
            var tp = TShock.Players[info.TargetPlayer];
            if (tp != null && tp.Active && !tp.Dead) return tp;
        }
        var near = Nearest(npc, cfg.EliteAttackRangeTiles);
        return near != null && !near.Dead ? near : null;
    }

    private int AttackInterval(int stage, EliteInfo info)
    {
        double iv = cfg.EliteAttackIntervalSeconds - stage * cfg.EliteAttackIntervalDropPerStage;
        iv /= Math.Max(0.5, cfg.RedDifficulty);
        if (info.Phase >= 1) iv *= 0.7;
        if (info.Phase >= 2) iv *= 0.7;
        if (info.Affixes.Contains("swift")) iv *= cfg.AffixSwiftMult;
        if (info.Enraged) iv *= 0.5;
        return Math.Max(cfg.EliteAttackMinIntervalSeconds, (int)Math.Round(iv));
    }

    private void EliteAttackTick()
    {
        shotsThisSecond = 0;
        if (elites.Count == 0) return;
        int stage = RedStage();

        foreach (var kv in elites.ToList())
        {
            var info = kv.Value;
            var npc = Main.npc[kv.Key];
            if (!npc.active || npc.type != info.Type) continue;
            if (info.Kind == "treasure") continue; // el tesoro viviente no ataca a distancia

            if (info.Kind == "champion" || info.Kind == "apocalypse") UpdateChampionPhase(kv.Key, npc, info);
            if (info.Affixes.Count > 0) AffixTick(npc, info);

            if (!cfg.EliteShootEnabled) continue;
            if (--info.Cooldown > 0) continue;
            info.Cooldown = AttackInterval(stage, info) + rng.Next(0, 2);

            var target = PickShotTarget(npc, info);
            if (target == null) continue;
            EliteAttack(npc, info, target, stage);
        }
    }

    private void EliteAttack(NPC npc, EliteInfo info, TSPlayer target, int stage)
    {
        var shots = EligibleShots(stage);
        if (info.ShotNames.Count > 0)
        {
            // jefes: solo usan su propio conjunto de proyectiles (hasta 5 tipos distintos)
            var own = shots.Where(x => info.ShotNames.Contains(x.s.Name)).ToList();
            if (own.Count > 0) shots = own;
        }
        if (shots.Count == 0) return;

        var shot = shots[rng.Next(shots.Count)];
        double cx = npc.Center.X, cy = npc.Center.Y;
        double aim = Math.Atan2(target.TPlayer.Center.Y - cy, target.TPlayer.Center.X - cx);
        int dmg = ShotDamage(stage, shot.s.DamageMult);
        double speed = shot.s.Speed;

        // patrones: uno al jugador, abanico, circulo y rafaga (se desbloquean con las etapas)
        var patterns = new List<string> { "aimed" };
        if (stage >= 2) patterns.Add("spread");
        if (stage >= 3) patterns.Add("skyfall");    // proyectiles que caen sobre el jugador
        if (stage >= 5) patterns.Add("nova");
        if (stage >= 6) patterns.Add("spiral");     // 3 brazos en espiral
        if (stage >= 8) patterns.Add("barrage");
        if (stage >= 9) patterns.Add("ring2");      // dos circulos desfasados
        if (stage >= 11) patterns.Add("pincer");    // tenaza desde ambos lados
        if (info.Phase >= 1) patterns.Add("nova");
        string pattern = patterns[rng.Next(patterns.Count)];

        double tcx = target.TPlayer.Center.X, tcy = target.TPlayer.Center.Y;

        switch (pattern)
        {
            case "skyfall":
            {
                int n = 4 + stage / 3;
                for (int i = 0; i < n; i++)
                {
                    double x = tcx + (rng.NextDouble() * 2 - 1) * 16 * 10;
                    double y = tcy - 16 * (22 + rng.Next(0, 8));
                    FireShot(shot.id, x, y, Math.PI / 2 + (rng.NextDouble() - 0.5) * 0.15, speed * 1.1, dmg);
                }
                break;
            }
            case "spiral":
            {
                double start = rng.NextDouble() * Math.PI * 2;
                for (int arm = 0; arm < 3; arm++)
                    for (int k = 0; k < 4; k++)
                        FireShot(shot.id, cx, cy, start + arm * 2 * Math.PI / 3 + k * 0.22, speed * (0.7 + 0.12 * k), dmg);
                break;
            }
            case "ring2":
            {
                int n = cfg.NovaBaseCount + stage / 2;
                double start = rng.NextDouble() * Math.PI * 2;
                FireNova(npc, shot, stage, n, 0.8, start);
                FireNova(npc, shot, stage, n, 0.55, start + Math.PI / n);
                break;
            }
            case "pincer":
            {
                foreach (int side in new[] { -1, 1 })
                    for (int i = 0; i < 3; i++)
                    {
                        double x = tcx + side * 16 * 24;
                        double y = tcy + (i - 1) * 16 * 3;
                        FireShot(shot.id, x, y, Math.Atan2(tcy - y, tcx - x), speed * 1.15, dmg);
                    }
                break;
            }
            case "spread":
            {
                int n = cfg.SpreadBaseCount + stage / 4;
                for (int i = 0; i < n; i++)
                    FireShot(shot.id, cx, cy, aim + (i - (n - 1) / 2.0) * 0.18, speed, dmg);
                break;
            }
            case "nova":
            {
                FireNova(npc, shot, stage, cfg.NovaBaseCount + stage / 2);
                break;
            }
            case "barrage":
            {
                for (int i = 0; i < 5; i++)
                    FireShot(shot.id, cx, cy, aim, speed * (1.0 - i * 0.12), dmg);
                break;
            }
            default:
                FireShot(shot.id, cx, cy, aim, speed, dmg);
                break;
        }
    }

    // enemigos que invocan los elites: de la etapa actual
    private List<string> MinionNames()
    {
        int stage = RedStage();
        var tier = cfg.EliteMobTiers.Where(t => t.MinProgress <= stage).OrderByDescending(t => t.MinProgress).FirstOrDefault();
        var list = (tier?.Mobs ?? new List<string>()).Where(n => ResolveNpc(n) > 0).ToList();
        if (list.Count == 0) list = cfg.MinionMobs.Where(n => ResolveNpc(n) > 0).ToList();
        return list;
    }

    private void SpawnMinionsFor(int owner, NPC boss, int count)
    {
        var names = MinionNames();
        if (names.Count == 0) return;
        int tx = (int)(boss.position.X / 16.0), ty = (int)(boss.position.Y / 16.0);
        for (int i = 0; i < count; i++)
        {
            int type = ResolveNpc(names[rng.Next(names.Count)]);
            foreach (int idx in SpawnTagged(type, tx, ty, 12, 6)) minions[idx] = (type, owner);
        }
    }

    // --- comunes de elites
    private void TargetTick()
    {
        foreach (var kv in elites)
        {
            if (kv.Value.TargetPlayer < 0) continue;
            var npc = Main.npc[kv.Key];
            if (!npc.active) continue;
            var tp = TShock.Players[kv.Value.TargetPlayer];
            if (tp == null || !tp.Active) continue;
            npc.target = kv.Value.TargetPlayer;
            npc.netUpdate = true;
        }
    }

    private void DespawnElites()
    {
        int n = 0;
        foreach (var kv in elites.ToList())
        {
            if (Main.npc[kv.Key].active && Main.npc[kv.Key].type == kv.Value.Type) { RemoveNpc(kv.Key); n++; }
        }
        elites.Clear();
        foreach (var k in minions.Keys.ToList()) RemoveNpc(k);
        minions.Clear();
        if (n > 0) SayRed("[RULETA] Los elites restantes han desaparecido.");
    }

    private void RewardEliteKill(NPC npc, EliteInfo info)
    {
        var recipients = new List<TSPlayer>();
        if (info.Team >= 0)
        {
            recipients = Players().Where(x => x.Team == info.Team).ToList(); // todo el equipo, este donde este
        }
        else if (info.Share)
        {
            double nx = npc.position.X / 16.0, ny = npc.position.Y / 16.0;
            recipients = Players().Where(p => Math.Pow(p.TileX - nx, 2) + Math.Pow(p.TileY - ny, 2) <= 250.0 * 250.0).ToList();
        }
        if (recipients.Count == 0)
        {
            var near = Nearest(npc, 150);
            if (near != null) recipients.Add(near);
        }
        if (recipients.Count == 0) return;

        bool crowd = info.Kind == "horde" || info.Kind == "marked" || info.Kind == "gauntlet" && !info.Boss;
        int baseDrops = info.Drops > 0 ? info.Drops : rng.Next(cfg.EliteDropMin, cfg.EliteDropMax + 1);
        int extra = ExtraRewardDrops();
        if (crowd) extra /= 2; // muchos enemigos: bonus a la mitad
        int lootMult = Math.Max(1, info.LootMult);
        int affixes = info.Affixes.Count;
        int drops = (baseDrops + extra) * lootMult + (crowd ? affixes / 2 : affixes);   // cada modificador = +1 premio

        // premio asegurado (monedas + pocion) y objeto raro: crecen con el modificador y con el multiplicador de loot
        double guaranteed = (crowd ? 0.4 : 1.0) * lootMult * (1.0 + 0.25 * affixes);
        double rareChance = cfg.EliteRareChance + cfg.EliteRareChancePerAffix * affixes;
        if (crowd) rareChance /= 2;
        if (info.Boss || info.Kind == "champion") rareChance = 100;
        int rolls = Math.Min(Math.Max(1, cfg.EliteRareMaxRolls), lootMult);

        foreach (var r in recipients)
        {
            var parts = new List<string>();
            string s = GiveLoot(r, cfg.EliteLootTable, drops);
            if (s != "") parts.Add(s);
            string g = GiveGuaranteed(r, guaranteed);
            if (g != "") parts.Add(g);
            string rare = RollRare(r, rareChance, rolls);
            if (rare != "") parts.Add($"[RARO] {rare}");
            if (parts.Count > 0) Say($"[RULETA] {r.Name} obtuvo por el elite: {string.Join(", ", parts)}.", 255, 120, 120);
        }
    }

    private void DeathNova(NPC npc)
    {
        int stage = RedStage();
        var shots = EligibleShots(stage);
        if (shots.Count == 0) return;
        FireNova(npc, shots[rng.Next(shots.Count)], stage, cfg.AffixDeathNovaCount, 0.7);
    }

    // ------------------------------------------------------------------ GRIS
    private void EndAllRed()
    {
        redActive = false;
        rainActive = false;
        redKind = "";
        markedName = "";
        redElitesSeen = false;
        gauntletWave = 0;
        gauntletWaiting = false;
        apocStarted = false;
        extraSpawn = 1.0;
        if (bloodActive)
        {
            bloodActive = false;
            Main.bloodMoon = false;
            TSPlayer.All.SendData(PacketTypes.WorldInfo);
        }
    }

    private void DoGray()
    {
        EndAllRed();
        FinishSpecial();
        furyActive = false;
        greenBuffsActive = false;
        st.YellowLevel = 0;
        st.YellowStreak = 0;
        st.SpawnBoostStacks = 0;
        UpdateSpawnBoost();
        st.ActiveMobs.Clear();

        foreach (var k in eventMobs.Keys.ToList()) RemoveNpc(k);
        foreach (var k in elites.Keys.ToList()) RemoveNpc(k);
        foreach (var k in minions.Keys.ToList()) RemoveNpc(k);
        eventMobs.Clear();
        elites.Clear();
        minions.Clear();
        pending.Clear();

        Say("[RULETA] GRIS - Reinicio total. Dificultad, mobs y eventos vuelven a empezar desde cero.", 190, 190, 190);
    }

    // ------------------------------------------------------------------ muertes y drops
    private void OnNpcKilled(NpcKilledEventArgs args)
    {
        var npc = args.npc;
        if (npc == null) return;
        int i = npc.whoAmI;

        if (eventMobs.TryGetValue(i, out int t1) && t1 == npc.type)
        {
            Safe(() => RewardOrangeKill(npc));
        }
        else if (elites.TryGetValue(i, out var info) && info.Type == npc.type)
        {
            Safe(() => RewardEliteKill(npc, info));
            if (info.Affixes.Contains("deathnova")) Safe(() => DeathNova(npc));
            if (info.Kind == "minions" || info.Kind == "champion" || info.Kind == "apocalypse") Safe(() => RemoveMinionsOf(i));
        }
        else if (specialKind == "frenzy")
        {
            Safe(() => FrenzyKill(npc));
        }
    }

    private void OnLootDrop(NpcLootDropEventArgs args)
    {
        int i = args.NpcArrayIndex;
        if (i < 0 || i >= Main.maxNPCs) return;
        int type = Main.npc[i].type;
        bool tracked =
            (eventMobs.TryGetValue(i, out int a) && a == type) ||
            (elites.TryGetValue(i, out var info) && info.Type == type) ||
            (minions.TryGetValue(i, out var mn) && mn.type == type);
        if (tracked) args.Handled = true; // los mobs de evento solo sueltan el loot de la ruleta
    }

    // ------------------------------------------------------------------ comandos
    private string TimeLine()
    {
        int mins = Math.Max(0, st.SecondsLeft) / 60, secs = Math.Max(0, st.SecondsLeft) % 60;
        string extra = spinning ? " (girando ahora)" : (Busy() ? " (en pausa mientras haya un boss o invasion)" : "");
        return $"Proxima ruleta en {mins:00}:{secs:00}{extra}";
    }

    private IEnumerable<string> StatusLines()
    {
        yield return TimeLine();
        yield return $"Dificultad: nivel {st.YellowLevel}/{YellowCapLevels()} (+{st.YellowLevel * cfg.YellowPercentPerLevel:0.#}% vida y dano)";
        yield return $"Spawns extra: +{(SpawnFactor() - 1) * 100:0}%";
        yield return st.ActiveMobs.Count > 0
            ? $"Mobs naranja activos ({st.ActiveMobs.Count}): {string.Join(", ", st.ActiveMobs)}"
            : "Mobs naranja activos: ninguno";
        if (redActive && RedTitle.TryGetValue(redKind, out var title)) yield return $"Evento rojo activo: {title}";
        if (specialKind != "") yield return $"Evento azul/morado activo: {specialKind}";
        if (cfg.BossScalingEnabled && st.YellowLevel > 0)
            yield return $"Bosses: vida x{BossLifeMult():0.#}, velocidad de ataque x{BossSpeedMult():0.#}, con proyectiles";
        if (greenBuffsActive) yield return "Efecto verde: regeneracion y velocidad hasta la proxima ruleta";
        if (furyActive) yield return $"Efecto verde: furia (dano de jugadores x{cfg.GreenFuryDamageMult:0.#}) hasta la proxima ruleta";
        yield return $"Etapa del mundo (rojo): {RedStage()}/{RedStageMax}";
        yield return $"Elites vivos: {elites.Count}";
    }

    private static readonly Dictionary<string, string> ColorAlias = new(StringComparer.OrdinalIgnoreCase)
    {
        ["amarillo"] = "yellow", ["yellow"] = "yellow",
        ["naranja"] = "orange", ["orange"] = "orange",
        ["verde"] = "green", ["green"] = "green",
        ["rojo"] = "red", ["red"] = "red",
        ["gris"] = "gray", ["gray"] = "gray",
        ["azul"] = "blue", ["blue"] = "blue",
        ["morado"] = "purple", ["purple"] = "purple",
    };

    private void CmdTiempo(CommandArgs a) => a.Player.SendInfoMessage(TimeLine());

    private void CmdRuleta(CommandArgs a)
    {
        string sub = a.Parameters.Count > 0 ? a.Parameters[0].ToLowerInvariant() : "estado";
        bool admin = a.Player.HasPermission("ruleta.admin");

        switch (sub)
        {
            case "tiempo":
                a.Player.SendInfoMessage(TimeLine());
                break;

            case "estado":
                foreach (var l in StatusLines()) a.Player.SendInfoMessage(l);
                break;

            case "force":
                if (!admin) { a.Player.SendErrorMessage("No tienes permiso (ruleta.admin)."); break; }
                if (a.Parameters.Count < 2 || !ColorAlias.TryGetValue(a.Parameters[1], out var color))
                {
                    a.Player.SendErrorMessage("Uso: /ruleta force <amarillo|naranja|verde|rojo|gris|azul|morado> [evento]");
                    break;
                }
                if (color == "red" && a.Parameters.Count >= 3)
                {
                    string kind = a.Parameters[2].ToLowerInvariant();
                    if (!RedTitle.ContainsKey(kind))
                    {
                        a.Player.SendErrorMessage("Eventos rojos: " + string.Join(", ", RedTitle.Keys));
                        break;
                    }
                    if (redActive) { a.Player.SendErrorMessage("Ya hay un evento rojo en curso."); break; }
                    StartRed(kind);
                    st.LastColor = "red";
                }
                else if ((color == "blue" || color == "purple") && a.Parameters.Count >= 3)
                {
                    if (specialKind != "") { a.Player.SendErrorMessage("Ya hay un evento azul/morado en curso."); break; }
                    string kind = a.Parameters[2].ToLowerInvariant();
                    bool ok = color == "blue" ? DoBlue(kind) : DoPurple(kind);
                    if (!ok)
                    {
                        a.Player.SendErrorMessage("Eventos: " + string.Join(", ", color == "blue" ? BlueKinds : PurpleKinds));
                        break;
                    }
                    st.LastColor = color;
                }
                else
                {
                    RunColor(color);
                }
                SaveState();
                break;

            case "reset":
                if (!admin) { a.Player.SendErrorMessage("No tienes permiso (ruleta.admin)."); break; }
                DoGray();
                st.SecondsLeft = cfg.IntervalMinutes * 60;
                SaveState();
                break;

            case "recargar":
                if (!admin) { a.Player.SendErrorMessage("No tienes permiso (ruleta.admin)."); break; }
                LoadConfig();
                ValidateNames();
                a.Player.SendSuccessMessage("Configuracion de la ruleta recargada.");
                break;

            default:
                a.Player.SendInfoMessage("Uso: /ruleta [estado|tiempo]" + (admin ? " | force <color> [evento] | reset | recargar" : ""));
                break;
        }
    }
}
