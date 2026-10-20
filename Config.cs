namespace RuletaPlugin;

public class LootEntry
{
    public string Name { get; set; } = "";
    public double Chance { get; set; } = 100;   // % (cajas) o peso (loot/regalos)
    public int MinProgress { get; set; } = 0;   // progreso mínimo del mundo (0-8)
    public int Min { get; set; } = 1;
    public int Max { get; set; } = 1;

    public LootEntry() { }
    public LootEntry(string name, double chance, int minProgress = 0, int min = 1, int max = 1)
    {
        Name = name; Chance = chance; MinProgress = minProgress; Min = min; Max = max;
    }
}

public class OrangeMob
{
    public string Name { get; set; } = "";
    public int Tier { get; set; } = 0; // 0 = débil, 1 = medio, 2 = fuerte

    public OrangeMob() { }
    public OrangeMob(string name, int tier) { Name = name; Tier = tier; }
}

public class EliteTier
{
    public int MinProgress { get; set; } = 0;
    public List<string> Mobs { get; set; } = new();
}

public class ShotType
{
    public string Name { get; set; } = "";   // nombre exacto en ProjectileID (ej. Stinger, DeathLaser)
    public int MinStage { get; set; } = 0;   // etapa del mundo (0-14) desde la que puede salir
    public double Speed { get; set; } = 9;
    public double DamageMult { get; set; } = 1.0;

    public ShotType() { }
    public ShotType(string name, int minStage, double speed, double damageMult)
    {
        Name = name; MinStage = minStage; Speed = speed; DamageMult = damageMult;
    }
}

public class RuletaConfig
{
    // ---- General ----
    public int IntervalMinutes { get; set; } = 20;
    public int WarnSeconds { get; set; } = 30;
    public int SpinSeconds { get; set; } = 5;

    // Pesos de cada color (suman 100 por defecto)
    public Dictionary<string, double> Weights { get; set; } = new()
    {
        ["yellow"] = 26, ["orange"] = 26, ["green"] = 26, ["red"] = 10, ["gray"] = 3,
        ["blue"] = 9,     // azul: fortuna (tesoro, frenesi de monedas, cara o cruz)
        ["purple"] = 7    // morado: caos (efectos al azar, pacto de sangre)
    };
    // Cuánto sube la probabilidad del gris cuando la dificultad amarilla está al máximo
    public double GrayBoostAtMaxYellow { get; set; } = 5;

    // ---- Amarillo ----
    public double YellowPercentPerLevel { get; set; } = 5;       // +5% vida y daño por nivel
    public double YellowBaseCapPercent { get; set; } = 20;       // tope con mundo recién empezado
    public double YellowCapPerProgressPercent { get; set; } = 5; // el tope sube con cada progreso
    public double YellowMaxPercent { get; set; } = 50;           // tope absoluto
    public double SpawnBoostPercent { get; set; } = 20;          // +20% spawns por amarillo
    public int SpawnBoostMinutes { get; set; } = 20;
    public double SpawnBoostMaxPercent { get; set; } = 100;

    // ---- Amarillo: BOSSES ----
    // Con dificultad amarilla activa, TODOS los bosses nacen con mas vida, disparan proyectiles y atacan mas rapido.
    // Nivel 1 = valor minimo, nivel maximo (el tope del momento) = valor maximo; entre medias sube poco a poco.
    public bool BossScalingEnabled { get; set; } = true;
    public double BossLifeMin { get; set; } = 3;      // vida x3 ...
    public double BossLifeMax { get; set; } = 20;     // ... hasta x20
    public double BossSpeedMin { get; set; } = 2;     // velocidad de ataque (proyectiles) x2 ...
    public double BossSpeedMax { get; set; } = 10;    // ... hasta x10
    public bool BossShootEnabled { get; set; } = true;
    // Cada jefe recibe su propio conjunto de tipos de proyectil distintos (flechas, aguijones, laseres, cristal...).
    // Sin dificultad (o nivel 1) usa el minimo; sube con la dificultad hasta el maximo (tope absoluto: 5).
    public int BossMinShotTypes { get; set; } = 1;
    public int BossMaxShotTypes { get; set; } = 5;
    public double BossBaseIntervalSeconds { get; set; } = 4;   // segundos entre ataques a velocidad x1
    public int BossMaxAttacksPerSecond { get; set; } = 3;      // tope de ataques por segundo (a velocidades muy altas)
    public int BossAttackRangeTiles { get; set; } = 90;        // solo dispara si hay un jugador a esta distancia
    public int BossMaxShooters { get; set; } = 6;              // bosses disparando a la vez (gusanos y gemelos suman)
    public double BossShotDamageMult { get; set; } = 0.8;      // dano de sus proyectiles respecto a los de un elite
    public double BossShotSpeedFalloff { get; set; } = 0.12;   // a mas velocidad de ataque, menos dano por proyectil
    // Seguridad: ningun proyectil del plugin (bosses, elites, lluvia) hace mas de este % de la vida maxima
    // del jugador mas debil conectado, asi nadie muere de un solo golpe.
    public double ShotMaxLifePercent { get; set; } = 20;

    // ---- Azul: fortuna ----
    public Dictionary<string, double> BlueWeights { get; set; } = new()
    {
        ["treasure"] = 12,   // tesoro viviente: un enemigo debil con loot enorme y objeto raro, tiempo limitado
        ["frenzy"] = 14,     // frenesi: los enemigos sueltan monedas durante unos minutos
        ["gamble"] = 10,     // cara o cruz: cada jugador gana un premio grande o sufre un castigo leve
    };
    public int TreasureMinutes { get; set; } = 5;
    public int TreasureLootMult { get; set; } = 2;
    public int FrenzyMinutes { get; set; } = 4;
    public double FrenzyChancePercent { get; set; } = 35;      // probabilidad por enemigo derrotado
    public int FrenzySilverBase { get; set; } = 3;             // monedas de plata por drop en la etapa 0
    public int FrenzySilverPerStage { get; set; } = 6;         // +6 de plata por etapa
    public double GambleWinPercent { get; set; } = 55;
    public double GambleWinMult { get; set; } = 3;             // premio asegurado x3 + 40% de objeto raro
    public double GambleRareChance { get; set; } = 40;
    public int GambleDebuffSeconds { get; set; } = 90;         // castigo leve: debilidad y lentitud

    // ---- Morado: caos ----
    public Dictionary<string, double> PurpleWeights { get; set; } = new()
    {
        ["chaos"] = 14,      // cada pocos segundos, cada jugador recibe un efecto al azar (bueno o malo, siempre leve)
        ["pact"] = 12,       // pacto de sangre: buffs de combate para todos, pero muchos mas enemigos
    };
    public int ChaosSeconds { get; set; } = 150;
    public int ChaosIntervalSeconds { get; set; } = 15;
    public double ChaosGoodPercent { get; set; } = 60;
    public int PactMinutes { get; set; } = 4;
    public double PactSpawnFactor { get; set; } = 2.5;         // +0.1 por etapa
    public int PactRewardMult { get; set; } = 2;

    // ---- Naranja ----
    public int OrangeSpawnIntervalSeconds { get; set; } = 20;
    public int OrangeMaxActive { get; set; } = 8;      // mobs distintos activos a la vez
    public int OrangeMaxPerPlayer { get; set; } = 3;   // maximo de mobs naranja cerca de cada jugador; no salen mas hasta que haya menos
    public int OrangeSpawnOffsetTiles { get; set; } = 45; // distancia horizontal minima aprox. al jugador (a izquierda o derecha)
    public int OrangeSpawnRangeX { get; set; } = 12;      // margen aleatorio alrededor de ese punto (horizontal)
    public int OrangeSpawnRangeY { get; set; } = 8;       // margen aleatorio alrededor de ese punto (vertical)
    public int OrangeNearRadiusTiles { get; set; } = 80; // que tan cerca cuenta como "cerca del jugador" (en bloques)
    public int OrangeCrateCapPerPlayerPerCycle { get; set; } = 5;
    public int[] OrangeTierUnlockProgress { get; set; } = { 0, 2, 4 };
    // Los mobs naranja nacen con menos vida y daño (1 = normal, 0.2 = 20% de lo normal)
    public double OrangeLifeMult { get; set; } = 0.35;
    public double OrangeDamageMult { get; set; } = 0.2;
    // Sin armadura: defensa 0 y debuff Ichor (-15 de defensa) durante 1 hora
    public bool OrangeRemoveDefense { get; set; } = true;

    public List<OrangeMob> OrangeMobs { get; set; } = new()
    {
        // Débiles
        new("Pixie", 0), new("Illuminant Slime", 0), new("Illuminant Bat", 0), new("Gastropod", 0),
        new("Derpling", 0), new("Floaty Gross", 0), new("Antlion Swarmer", 0), new("Clinger", 0),
        new("Slimer", 0), new("Moss Hornet", 0),
        // Medios
        new("Tomb Crawler", 1), new("Ghoul", 1), new("Dune Splicer", 1), new("Sand Elemental", 1),
        new("Ice Elemental", 1), new("Wraith", 1), new("Enchanted Sword", 1), new("Cursed Hammer", 1),
        new("Crimson Axe", 1), new("Corruptor", 1),
        // Fuertes
        new("Unicorn", 2), new("Angry Trapper", 2), new("Ice Tortoise", 2), new("Giant Tortoise", 2),
        new("Ice Golem", 2), new("Pigron", 2), new("Chaos Elemental", 2), new("Sand Poacher", 2),
        new("Armored Skeleton", 2), new("Skeleton Archer", 2),
    };

    // Probabilidad (%) por kill de cada caja
    public List<LootEntry> OrangeCrates { get; set; } = new()
    {
        new("Wooden Crate", 8, 0), new("Iron Crate", 5, 0), new("Golden Crate", 2, 1),
        new("Jungle Crate", 3, 1), new("Corrupt Crate", 3, 1), new("Crimson Crate", 3, 1),
        new("Dungeon Crate", 3, 3), new("Frozen Crate", 3, 1), new("Oasis Crate", 3, 1),
        new("Lavacrate", 3, 3), new("Sky Crate", 3, 1), new("Hallowed Crate", 2, 5),
    };

    // ---- Rojo ----
    // El rojo se mide por ETAPAS del mundo, de 0 a 14: Ojo de Cthulhu, Devorador/Cerebro, Esqueletron,
    // Reina Abeja, hardmode, los 3 jefes mecanicos, Plantera, Golem, Duke Fishron, Emperatriz de la Luz,
    // Cultista y Moon Lord. Cada etapa sube la dificultad y la recompensa poco a poco.
    public double RedDifficulty { get; set; } = 1.0;   // ajuste global: mas de 1 = mas dificil (elites y disparos)

    public int EliteWarnSeconds { get; set; } = 30;
    public int EliteTimeoutMinutes { get; set; } = 7;
    public double EliteMinMult { get; set; } = 4;   // vida y dano de los elites en la etapa 0
    public double EliteMaxMult { get; set; } = 11;  // ... y en la etapa 14 (Moon Lord)
    public int EliteSpawnRangeX { get; set; } = 25;
    public int EliteSpawnRangeY { get; set; } = 12;
    public int EliteDropMin { get; set; } = 2;
    public int EliteDropMax { get; set; } = 4;

    // Disparos de los elites: los hace el plugin, no hace falta que el mob original dispare
    public bool EliteShootEnabled { get; set; } = true;
    public int EliteAttackIntervalSeconds { get; set; } = 5;            // segundos entre ataques en la etapa 0
    public double EliteAttackIntervalDropPerStage { get; set; } = 0.3;  // cuanto baja ese tiempo por etapa
    public int EliteAttackMinIntervalSeconds { get; set; } = 2;
    public int EliteAttackRangeTiles { get; set; } = 70;                // solo dispara si hay un jugador a esta distancia
    public int ShotDamageBase { get; set; } = 18;                       // dano de cada proyectil en la etapa 0
    public int ShotDamagePerStage { get; set; } = 10;                   // +10 por etapa (unos 158 en Moon Lord)
    public int SpreadBaseCount { get; set; } = 3;                       // proyectiles del abanico (+1 cada 4 etapas)
    public int NovaBaseCount { get; set; } = 8;                         // proyectiles del circulo (+1 cada 2 etapas)
    public int MaxShotsPerSecond { get; set; } = 80;                    // tope para no saturar el servidor

    // Tipos de proyectil (Name = nombre en ProjectileID). Los que no existan se ignoran.
    public List<ShotType> EliteShots { get; set; } = new()
    {
        new("WoodenArrowHostile", 0, 9, 0.9),          // flechas
        new("Stinger", 0, 8, 0.9),                     // aguijones
        new("EyeFire", 0, 9, 0.9),                     // bolas de fuego (mas variedad para los jefes)
        new("SpikedSlimeSpike", 1, 9, 0.9),            // pinchos
        new("DemonSickle", 3, 8, 1.0),                 // hoces
        new("ShadowBeamHostile", 3, 11, 1.0),          // rayos de sombra
        new("FrostBlastHostile", 5, 9, 1.0),           // hielo
        new("BulletDeadeye", 6, 12, 1.0),              // balas
        new("InfernoHostileBolt", 8, 9, 1.2),          // infierno
        new("DeathSickle", 9, 10, 1.3),                // hoces de la muerte
        new("EyeLaser", 2, 10, 1.0),                   // laseres
        new("PinkLaser", 4, 11, 1.0),
        new("QueenSlimeMinionBlueSpike", 5, 9, 1.0),   // cristal
        new("DeathLaser", 6, 12, 1.2),
        new("CursedFlameHostile", 8, 9, 1.2),
        new("SaucerLaser", 10, 13, 1.3),
        new("CultistBossFireBall", 11, 9, 1.3),
        new("PhantasmalBolt", 13, 12, 1.5),
    };

    // Enemigos que pueden salir como elite, segun la etapa (MinProgress = etapa 0-14).
    public List<EliteTier> EliteMobTiers { get; set; } = new()
    {
        new EliteTier { MinProgress = 0,  Mobs = new() { "Zombie", "Demon Eye", "Blue Slime", "Cave Bat" } },
        new EliteTier { MinProgress = 3,  Mobs = new() { "Skeleton", "Jungle Bat", "Hornet", "Demon Eye" } },
        new EliteTier { MinProgress = 5,  Mobs = new() { "Pixie", "Gastropod", "Wraith", "Ghoul" } },
        new EliteTier { MinProgress = 8,  Mobs = new() { "Unicorn", "Pigron", "Ice Golem", "Armored Skeleton" } },
        new EliteTier { MinProgress = 9,  Mobs = new() { "Necromancer", "Diabolist", "Ragged Caster", "Skeleton Sniper", "Tactical Skeleton" } },
        new EliteTier { MinProgress = 10, Mobs = new() { "Lihzahrd", "Flying Snake", "Paladin", "Necromancer" } },
        new EliteTier { MinProgress = 12, Mobs = new() { "Reaper", "Swamp Thing", "Frankenstein", "Creature from the Deep" } },
        new EliteTier { MinProgress = 13, Mobs = new() { "Vortexian", "Solar Sroller", "Nebula Floater", "Stargazer" } },
    };

    // Loot de los elites (Chance = peso, MinProgress = etapa 0-14). Mejora poco a poco hasta Moon Lord.
    public List<LootEntry> EliteLootTable { get; set; } = new()
    {
        // Etapas 0-4: pre-hardmode
        new("Iron Bar", 30, 0, 5, 12), new("Silver Bar", 25, 0, 4, 10), new("Gold Bar", 20, 1, 3, 8),
        new("Lesser Healing Potion", 30, 0, 3, 6), new("Ironskin Potion", 20, 0, 2, 4),
        new("Regeneration Potion", 20, 0, 2, 4), new("Swiftness Potion", 20, 0, 2, 4),
        new("Spelunker Potion", 15, 0, 2, 3), new("Silver Coin", 35, 0, 20, 60),
        new("Hermes Boots", 6, 0), new("Cloud in a Bottle", 6, 0), new("Band of Regeneration", 6, 0),
        new("Magic Mirror", 5, 0), new("Shiny Red Balloon", 6, 0), new("Lucky Horseshoe", 6, 0),
        new("Demonite Bar", 18, 2, 4, 8), new("Crimtane Bar", 18, 2, 4, 8), new("Meteorite Bar", 15, 2, 4, 8),
        new("Hellstone Bar", 18, 3, 3, 6), new("Obsidian Skin Potion", 12, 3, 2, 3),
        new("Healing Potion", 25, 4, 3, 6), new("Gold Coin", 20, 4, 1, 3),
        // Etapas 5-8: hardmode y jefes mecanicos
        new("Cobalt Bar", 20, 5, 4, 8), new("Palladium Bar", 20, 5, 4, 8),
        new("Greater Healing Potion", 25, 5, 3, 6), new("Soul of Light", 20, 5, 3, 6), new("Soul of Night", 20, 5, 3, 6),
        new("Mythril Bar", 15, 6, 3, 6), new("Orichalcum Bar", 15, 6, 3, 6), new("Soul of Sight", 15, 6, 2, 4),
        new("Adamantite Bar", 12, 7, 3, 6), new("Titanium Bar", 12, 7, 3, 6), new("Soul of Might", 15, 7, 2, 4),
        new("Hallowed Bar", 15, 8, 3, 6), new("Soul of Fright", 15, 8, 2, 4),
        // Etapas 9-11: Plantera, Golem y Duke Fishron
        new("Chlorophyte Bar", 20, 9, 4, 8), new("Ectoplasm", 18, 9, 3, 6), new("Platinum Coin", 8, 9, 1, 2),
        new("Beetle Husk", 15, 10, 3, 6), new("Spectre Bar", 12, 10, 3, 5), new("Shroomite Bar", 12, 10, 3, 5),
        new("Truffle Worm", 5, 11, 1, 1),
        // Etapas 12-14: Emperatriz de la Luz, Cultista y Moon Lord
        new("Super Healing Potion", 20, 12, 3, 6), new("Solar Tablet Fragment", 15, 13, 2, 5),
        new("Luminite Bar", 25, 14, 4, 10), new("Solar Fragment", 15, 14, 3, 8), new("Vortex Fragment", 15, 14, 3, 8),
        new("Nebula Fragment", 15, 14, 3, 8), new("Stardust Fragment", 15, 14, 3, 8),
        // Extras por etapa: pociones de combate y materiales utiles que mejoran poco a poco
        new("Mana Potion", 20, 0, 3, 6), new("Archery Potion", 15, 0, 2, 3), new("Wrath Potion", 15, 2, 2, 3),
        new("Endurance Potion", 15, 2, 2, 3), new("Thorns Potion", 12, 2, 2, 3),
        new("Hellfire Arrow", 15, 3, 60, 120), new("Greater Mana Potion", 20, 5, 3, 6),
        new("Pixie Dust", 15, 5, 3, 8), new("Crystal Shard", 15, 5, 3, 8), new("Ichor", 12, 5, 2, 4),
        new("Cursed Flame", 12, 5, 2, 4), new("Light Shard", 8, 5), new("Dark Shard", 8, 5),
        new("Rage Potion", 15, 5, 2, 3), new("Inferno Potion", 15, 5, 2, 3), new("Soul of Flight", 12, 6, 2, 4),
        new("Lifeforce Potion", 12, 6, 2, 3), new("Titanium Bar", 12, 8, 3, 6),
        new("Super Mana Potion", 20, 12, 3, 6), new("Beetle Husk", 12, 12, 3, 6),
    };

    // Objetos raros: cada elite tiene una probabilidad de soltar 1 (mas con modificadores). Mejoran con la etapa.
    public double EliteRareChance { get; set; } = 10;           // % base por elite y por jugador
    public double EliteRareChancePerAffix { get; set; } = 10;   // +% por cada modificador del elite
    public int EliteRareMaxRolls { get; set; } = 3;             // tiradas maximas (el loot x2/x3 da mas tiradas)
    public double RedRewardRareChance { get; set; } = 15;       // % en eventos sin enemigo (noche de sangre, lluvia...)
    public List<LootEntry> EliteRareLootTable { get; set; } = new()
    {
        // Pre-hardmode
        new("Life Crystal", 10, 0), new("Mana Crystal", 10, 0),
        new("Hermes Boots", 8, 0), new("Cloud in a Bottle", 8, 0), new("Band of Regeneration", 8, 0),
        new("Magic Mirror", 6, 0), new("Shiny Red Balloon", 8, 0), new("Lucky Horseshoe", 8, 0),
        new("Water Walking Boots", 6, 0), new("Anklet of the Wind", 6, 0), new("Aglet", 6, 0), new("Feral Claws", 6, 1),
        // Etapa 3: tras Skeletron (dungeon e inframundo)
        new("Cobalt Shield", 6, 3), new("Muramasa", 5, 3), new("Blue Moon", 5, 3), new("Handgun", 5, 3),
        new("Aqua Scepter", 5, 3), new("Obsidian Skull", 6, 3), new("Dark Lance", 5, 3), new("Flamelash", 5, 3),
        new("Flower of Fire", 5, 3), new("Sunfury", 5, 3),
        // Hardmode y jefes mecanicos
        new("Philosopher's Stone", 6, 5), new("Cross Necklace", 6, 5), new("Titan Glove", 5, 5),
        new("Star Cloak", 6, 5), new("Dual Hook", 5, 5),
        new("Warrior Emblem", 5, 6), new("Ranger Emblem", 5, 6), new("Sorcerer Emblem", 5, 6), new("Summoner Emblem", 5, 6),
        new("Ankh Charm", 5, 7),
        // Plantera, Golem y mas alla
        new("Life Fruit", 10, 9, 1, 2), new("Paladin's Shield", 5, 9), new("Paladin's Hammer", 5, 9),
        new("Ankh Shield", 4, 10),
    };

    // Premio garantizado en cada elite: monedas + pocion de curacion de la etapa. Escala con el progreso.
    public int GuaranteedSilverBase { get; set; } = 40;
    public int GuaranteedSilverPerStage { get; set; } = 110;   // etapa 14: unas 15 monedas de oro
    // Se da la de mayor MinProgress que ya este desbloqueada (Min-Max = cantidad)
    public List<LootEntry> GuaranteedHealing { get; set; } = new()
    {
        new("Lesser Healing Potion", 1, 0, 3, 5), new("Healing Potion", 1, 4, 3, 5),
        new("Greater Healing Potion", 1, 5, 3, 5), new("Super Healing Potion", 1, 12, 3, 5),
    };

    // ---- Rojo: eventos (peso de cada uno; pon 0 para desactivar uno) ----
    public Dictionary<string, double> RedWeights { get; set; } = new()
    {
        ["elite_each"] = 16,  // un elite cerca de cada jugador
        ["shared"] = 10,      // un elite gigante por equipo (team)
        ["horde"] = 12,       // varios elites mas debiles por jugador
        ["hunter"] = 10,      // un elite persigue a un jugador al azar
        ["blood"] = 7,        // noche de sangre
        ["survival"] = 7,     // aguantar con spawns altisimos
        ["weak_boss"] = 5,    // boss ya derrotado, con vida reducida
        ["countdown"] = 6,    // llegar a un punto antes de que acabe el tiempo
        ["minions"] = 8,      // elite que invoca enemigos
        ["marked"] = 8,       // un jugador marcado, enemigos a su alrededor
        ["champion"] = 12,    // campeon: elite gigante con 3 fases, disparos y refuerzos
        ["rain"] = 9,         // lluvia de proyectiles del cielo
        ["gauntlet"] = 11,    // la prueba: oleadas de elites cada vez mas duras + jefe final
        ["apocalypse"] = 7,   // heraldo del apocalipsis: campeon gigante + lluvia de proyectiles a la vez
    };

    // ---- Rojo: modificadores de elite (afijos) ----
    // Los elites reciben modificadores al azar segun la etapa. Cada modificador hace al elite mas peligroso,
    // pero tambien sube el loot (mas premios, mas probabilidad de objeto raro).
    public bool AffixesEnabled { get; set; } = true;
    public int[] AffixUnlockStages { get; set; } = { 2, 6, 10 };   // 1er modificador en etapa 2, 2o en 6, 3o en 10
    public int AffixMaxCount { get; set; } = 3;
    public Dictionary<string, double> AffixWeights { get; set; } = new()
    {
        ["regen"] = 10,      // Regenerador: recupera vida cada segundo
        ["teleport"] = 10,   // Fantasma: se teletransporta cerca del jugador
        ["berserk"] = 10,    // Berserker: bajo cierta vida ataca el doble de rapido
        ["deathnova"] = 10,  // Explosivo: al morir lanza un circulo de proyectiles
        ["swift"] = 10,      // Veloz: dispara mucho mas seguido
        ["armored"] = 10,    // Blindado: mucha mas defensa
    };
    public double AffixRegenPercentPerSecond { get; set; } = 0.6;  // % de vida maxima por segundo
    public int AffixTeleportSeconds { get; set; } = 12;
    public double AffixBerserkLifePercent { get; set; } = 50;
    public int AffixDeathNovaCount { get; set; } = 16;
    public double AffixSwiftMult { get; set; } = 0.6;              // el tiempo entre ataques se multiplica por esto
    public int AffixArmorBase { get; set; } = 8;
    public int AffixArmorPerStage { get; set; } = 3;

    // ---- Rojo: eventos nuevos ----
    // La prueba: oleadas de elites. Cada oleada es mas dura; la ultima trae un jefe. Premio grande para quien sobreviva.
    public int GauntletWaves { get; set; } = 3;
    public int GauntletMinStage { get; set; } = 1;
    public int GauntletWaveMinutes { get; set; } = 4;        // tiempo limite = oleadas x este valor
    public int GauntletWaveDelaySeconds { get; set; } = 8;   // descanso entre oleadas
    public int GauntletRewardMult { get; set; } = 2;         // premio final para los supervivientes
    // Heraldo del apocalipsis: campeon gigante (3 fases) mientras llueven proyectiles sin parar.
    public int ApocalypseMinStage { get; set; } = 3;
    public int ApocalypseMinutes { get; set; } = 10;
    public double ApocalypseLifeMult { get; set; } = 3.0;    // vida extra respecto a un elite normal
    public int ApocalypseLootMult { get; set; } = 3;
    public int ApocalypseRainSlowdown { get; set; } = 2;     // la lluvia cae cada RainIntervalSeconds x este valor

    // Elite por equipo (team): un elite gigante por cada equipo con jugadores; los sin equipo comparten uno.
    public double SharedLifePerExtraPlayer { get; set; } = 0.75; // +75% de vida por cada jugador extra del equipo
    public int SharedLootMult { get; set; } = 3;                  // loot x3 para todo el equipo
    // Horda elite (+1 elite por jugador cada 4 etapas)
    public int HordeCountPerPlayer { get; set; } = 3;
    public int HordeMaxTotal { get; set; } = 24;
    public double HordeMultFactor { get; set; } = 0.6;   // fraccion del multiplicador del elite normal
    // Noche de sangre
    public int BloodNightMinutes { get; set; } = 5;
    public double BloodNightDamageMult { get; set; } = 2.5;
    // Supervivencia (el factor de spawns sube +0.25 por etapa)
    public int SurvivalSeconds { get; set; } = 180;
    public double SurvivalSpawnFactor { get; set; } = 4;
    // Boss sorpresa debil (solo bosses que ya hayas derrotado)
    public double WeakBossLifeMult { get; set; } = 0.6;
    public double WeakBossLifePerExtraPlayer { get; set; } = 0.5;
    public List<string> WeakBosses { get; set; } = new()
    {
        "King Slime", "Eye of Cthulhu", "Queen Bee", "Skeletron", "Deerclops", "Queen Slime"
    };
    // Cuenta atras de la muerte (-1 = usar el spawn del mundo)
    public int CountdownSeconds { get; set; } = 90;
    public int CountdownRadiusTiles { get; set; } = 30;
    public int CountdownTileX { get; set; } = -1;
    public int CountdownTileY { get; set; } = -1;
    // Elite con minions (los minions son enemigos de la etapa actual)
    public int MinionIntervalSeconds { get; set; } = 6;
    public int MinionCap { get; set; } = 8;
    public List<string> MinionMobs { get; set; } = new() { "Zombie", "Demon Eye", "Blue Slime", "Cave Bat" };
    // Marcado (+1 elite cada 3 etapas)
    public int MarkedSeconds { get; set; } = 150;
    public int MarkedCount { get; set; } = 6;
    public double MarkedMultFactor { get; set; } = 0.7;
    // Campeon: un elite gigante con 3 fases (100%, 66% y 33% de vida)
    public double ChampionLifeMult { get; set; } = 2.5;              // vida extra respecto a un elite normal
    public double ChampionLifePerExtraPlayer { get; set; } = 1.0;    // +100% de vida por cada jugador extra
    public int ChampionLootMult { get; set; } = 2;
    public int ChampionMinutes { get; set; } = 8;
    public int ChampionAddsPerPhase { get; set; } = 4;               // refuerzos que invoca al cambiar de fase
    // Lluvia de proyectiles (+1 proyectil por jugador cada 4 etapas)
    public int RainSeconds { get; set; } = 60;
    public int RainIntervalSeconds { get; set; } = 2;
    public int RainBaseShots { get; set; } = 2;

    // Premios de eventos sin enemigo (noche de sangre, supervivencia, lluvia, cuenta atras, marcado)
    public int RedRewardCount { get; set; } = 3;
    // Las recompensas del rojo crecen con la etapa del mundo (0-14):
    // +1 premio extra cada N etapas, y +X% de cantidad por etapa en items apilables.
    public int RedRewardProgressPerExtraDrop { get; set; } = 3;
    public double RedRewardStackBonusPerProgress { get; set; } = 0.15;
    public List<LootEntry> RedRewardTable { get; set; } = new()
    {
        new("Wooden Crate", 20, 0), new("Iron Crate", 14, 0), new("Golden Crate", 8, 1),
        new("Silver Coin", 30, 0, 20, 60), new("Gold Bar", 15, 1, 3, 6),
        new("Iron Bar", 25, 0, 6, 12), new("Lesser Healing Potion", 20, 0, 3, 6),
        new("Healing Potion", 20, 4, 3, 6), new("Gold Coin", 15, 4, 1, 3),
        new("Pearlwood Crate", 10, 5), new("Titanium Crate", 8, 5), new("Mythril Crate", 8, 6),
        new("Greater Healing Potion", 20, 5, 3, 6), new("Hallowed Bar", 12, 8, 3, 6),
        new("Chlorophyte Bar", 15, 9, 4, 8), new("Platinum Coin", 10, 9, 1, 3),
        new("Spectre Bar", 10, 10, 3, 5), new("Super Healing Potion", 15, 12, 3, 6),
        new("Luminite Bar", 20, 14, 4, 10),
    };

    // ---- Verde ----
    // Los buffs verdes duran hasta la proxima ruleta: se renuevan cada N segundos (tambien tras morir o al entrar).
    public int GreenBuffRefreshSeconds { get; set; } = 30;
    // Furia: dano de los jugadores x3 (+200%) hasta la proxima ruleta
    public double GreenFuryDamageMult { get; set; } = 3;
    // Probabilidad de cada opcion verde (la de alivio solo cuenta si hay dificultad amarilla)
    public Dictionary<string, double> GreenWeights { get; set; } = new()
    {
        ["buffs"] = 28, ["gifts"] = 28, ["relief"] = 16, ["info"] = 13, ["fury"] = 15
    };
    public List<LootEntry> GreenGifts { get; set; } = new()
    {
        new("Iron Bar", 30, 0, 5, 10), new("Silver Bar", 25, 0, 3, 6), new("Gold Bar", 20, 1, 2, 4),
        new("Lesser Healing Potion", 30, 0, 3, 5), new("Lesser Mana Potion", 25, 0, 3, 5),
        new("Recall Potion", 15, 0, 1, 2),
    };
}
