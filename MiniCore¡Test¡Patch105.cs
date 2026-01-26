using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Data;
using Terraria;
using TerrariaApi.Server;
using TShockAPI;
using TShockAPI.Hooks;
using TShockAPI.DB;
using Newtonsoft.Json;
using Microsoft.Xna.Framework;
using Microsoft.Data.Sqlite;
using MySql.Data.MySqlClient;
using Terraria.Social;
using Terraria.GameContent.Events;

namespace MiniCore
{
    [ApiVersion(2, 1)]
    public class MiniCore : TerrariaPlugin
    {
        public override string Name => "MiniCore";
        public override Version Version => new Version(1, 3, 0);
        public override string Author => "Archiepescop";
        public override string Description => "Ядро для создания мини-игр в Terraria";

        private static readonly string ConfigPath = Path.Combine(TShock.SavePath, "MiniCore.json");
        private Config config = new Config();
        private Dictionary<int, PlayerData> playerData = new Dictionary<int, PlayerData>();
        private IDbConnection database;
        private Random random = new Random();

        public MiniCore(Main game) : base(game)
        {
        }

        public override void Initialize()
        {
            LoadConfig();
            InitializeDatabase();
            
            // ОШИБКА 1: Используем правильное имя события
            GetDataHandlers.ItemDrop += OnItemDrop;
            
            Commands.ChatCommands.Add(new Command("minigame.admin", CreateSpace, "createspace", "cs"));
            Commands.ChatCommands.Add(new Command("minigame.admin", DeleteSpace, "deletespace", "ds"));
            Commands.ChatCommands.Add(new Command("minigame.admin", SetSpaceLimit, "setspacelimit", "ssl"));
            Commands.ChatCommands.Add(new Command("minigame.admin", AddInventoryToSpace, "addinvspace", "ais"));
            Commands.ChatCommands.Add(new Command("minigame.admin", RemoveInventoryFromSpace, "removeinvspace", "ris"));
            Commands.ChatCommands.Add(new Command("minigame.admin", ListSpaceInventories, "listspaceinv", "lsi"));
            Commands.ChatCommands.Add(new Command("minigame.admin", SetLimitedInventory, "invranlim", "irl"));
            Commands.ChatCommands.Add(new Command("minigame.admin", SetDeadInventory, "setinvdead", "ssd"));
            Commands.ChatCommands.Add(new Command("minigame.admin", SetDeadRespawn, "setrespawndead", "srd"));
            Commands.ChatCommands.Add(new Command("minigame.admin", SetBuffSpace, "setbuffspace", "sbs"));
            Commands.ChatCommands.Add(new Command("minigame.admin", SetTimerStart, "settimerstart", "sts"));
            Commands.ChatCommands.Add(new Command("minigame.use", PickInventory, "pickinv", "pi"));
            Commands.ChatCommands.Add(new Command("minigame.admin", SaveInventoryTemplate, "saveinv", "si"));
            Commands.ChatCommands.Add(new Command("minigame.admin", DeleteInventoryTemplate, "delinv", "di"));
            Commands.ChatCommands.Add(new Command("minigame.admin", ListInventoryTemplates, "listinv", "li"));
            Commands.ChatCommands.Add(new Command("minigame.admin", CreateEmptyTemplate, "createinv", "ci"));
            Commands.ChatCommands.Add(new Command("minigame.admin", SetTemplateStats, "setinvstats", "sis"));
            Commands.ChatCommands.Add(new Command("minigame.admin", AddTemplateItem, "addinvitem", "aii"));
            Commands.ChatCommands.Add(new Command("minigame.admin", LinkSign, "linksign", "lsign"));
            Commands.ChatCommands.Add(new Command("minigame.admin", UnlinkSign, "unlinksign", "ulsign"));
            Commands.ChatCommands.Add(new Command("minigame.admin", SetSpaceSpawn, "setspacespawn", "sss"));
            Commands.ChatCommands.Add(new Command("minigame.use", JoinSpace, "joinspace", "js"));
            Commands.ChatCommands.Add(new Command("minigame.use", LeaveSpace, "leavespace", "ls"));
            Commands.ChatCommands.Add(new Command("minigame.use", ListSpaces, "listspaces", "spaces"));
            Commands.ChatCommands.Add(new Command("minigame.use", ShowHelp, "mhelp", "minihelp"));
            Commands.ChatCommands.Add(new Command("minigame.admin", SetItemParameter, "setitemparam", "sip"));
            Commands.ChatCommands.Add(new Command("minigame.admin", GetItemParameter, "getitemparam", "gip"));
            Commands.ChatCommands.Add(new Command("minigame.admin", ListItemParameters, "listitemparams", "lip"));
            Commands.ChatCommands.Add(new Command("minigame.admin", RemoveItemParameter, "removeitemparam", "rip"));
            Commands.ChatCommands.Add(new Command("minigame.admin", AddRewardArea, "ara", "addreward"));
            
            ServerApi.Hooks.ServerChat.Register(this, OnChat);
            ServerApi.Hooks.GameUpdate.Register(this, OnGameUpdate);
            ServerApi.Hooks.ServerLeave.Register(this, OnLeave);
            
            // ОШИБКА 2: Используем правильное имя события для PlayerSpawn
            GetDataHandlers.PlayerSpawn += OnPlayerSpawn;
            
            // ОШИБКА 3: Используем правильное имя события для Sign
            GetDataHandlers.Sign += OnSignInteract;
            
            Console.WriteLine("[MiniCore] Плагин успешно загружен! Версия 1.3.0");
        }

        private void OnItemDrop(object sender, GetDataHandlers.ItemDropEventArgs e)
        {
            if (e.Handled) return;

            var player = TShock.Players[e.Player.Index];
            if (player == null || !playerData.ContainsKey(player.Index)) return;

            var pData = playerData[player.Index];
            if (string.IsNullOrEmpty(pData.CurrentSpace)) return;

            var space = config.Spaces.FirstOrDefault(s => s.Name == pData.CurrentSpace);
            if (space == null) return;

            // Находим предмет в мире
            var item = Main.item[e.ID];
            if (item == null || !item.active) return;

            // Ищем кастомные параметры для этого предмета в текущем пространстве
            var parameters = space.ItemParameters.Where(p => p.ItemNetID == item.netID).ToList();
            if (parameters.Count > 0)
            {
                foreach (var param in parameters)
                {
                    ApplyItemParameter(item, param.ParameterName, param.ParameterValue);
                }

                // Синхронизируем изменения через 88 пакет (SyncItem)
                NetMessage.SendData((int)PacketTypes.ItemDrop, -1, -1, null, e.ID);
            }
        }

        private void InitializeDatabase()
        {
            string dbPath = Path.Combine(TShock.SavePath, "MiniCore.sqlite");
            
            try
            {
                if (TShock.DB.GetSqlType() == SqlType.Mysql)
                {
                    string[] host = TShock.Config.Settings.MySqlHost.Split(':');
                    database = new MySqlConnection()
                    {
                        ConnectionString = string.Format("Server={0}; Port={1}; Database={2}; Uid={3}; Pwd={4};",
                            host[0],
                            host.Length == 1 ? "3306" : host[1],
                            TShock.Config.Settings.MySqlDbName,
                            TShock.Config.Settings.MySqlUsername,
                            TShock.Config.Settings.MySqlPassword)
                    };
                }
                else
                {
                    database = new SqliteConnection($"Data Source={dbPath}");
                }
                
                database.Open();
                
                var creator = new SqlTableCreator(database, new SqliteQueryCreator());
                if (TShock.DB.GetSqlType() == SqlType.Mysql)
                {
                    creator = new SqlTableCreator(database, new MysqlQueryCreator());
                }
                
                creator.EnsureTableStructure(new SqlTable("MiniCore_Inventories",
                    new SqlColumn("Id", MySqlDbType.Int32) { Primary = true, AutoIncrement = true },
                    new SqlColumn("TemplateName", MySqlDbType.VarChar, 64) { Unique = true },
                    new SqlColumn("InventoryData", MySqlDbType.Text),
                    new SqlColumn("StatLife", MySqlDbType.Int32),
                    new SqlColumn("StatLifeMax", MySqlDbType.Int32),
                    new SqlColumn("StatMana", MySqlDbType.Int32),
                    new SqlColumn("StatManaMax", MySqlDbType.Int32),
                    new SqlColumn("CreatedAt", MySqlDbType.DateTime)
                ));
                
                creator.EnsureTableStructure(new SqlTable("MiniCore_Signs",
                    new SqlColumn("Id", MySqlDbType.Int32) { Primary = true, AutoIncrement = true },
                    new SqlColumn("SpaceName", MySqlDbType.VarChar, 64),
                    new SqlColumn("X", MySqlDbType.Int32),
                    new SqlColumn("Y", MySqlDbType.Int32)
                ));
                
                creator.EnsureTableStructure(new SqlTable("MiniCore_CustomItems",
                    new SqlColumn("Id", MySqlDbType.Int32) { Primary = true, AutoIncrement = true },
                    new SqlColumn("SpaceName", MySqlDbType.VarChar, 64),
                    new SqlColumn("ItemNetID", MySqlDbType.Int32),
                    new SqlColumn("ParameterName", MySqlDbType.VarChar, 64),
                    new SqlColumn("ParameterValue", MySqlDbType.Text),
                    new SqlColumn("CreatedAt", MySqlDbType.DateTime)
                ));

                creator.EnsureTableStructure(new SqlTable("MiniCore_RewardAreas",
                    new SqlColumn("Id", MySqlDbType.Int32) { Primary = true, AutoIncrement = true },
                    new SqlColumn("Name", MySqlDbType.VarChar, 64) { Unique = true },
                    new SqlColumn("X", MySqlDbType.Int32),
                    new SqlColumn("Y", MySqlDbType.Int32),
                    new SqlColumn("Radius", MySqlDbType.Int32),
                    new SqlColumn("MoneyReward", MySqlDbType.Int32),
                    new SqlColumn("CooldownSeconds", MySqlDbType.Int32),
                    new SqlColumn("LastRewardTimes", MySqlDbType.Text)
                ));
                
                LoadSignsFromDatabase();
                LoadInventoriesFromDatabase();
                LoadCustomItemsFromDatabase();
                LoadRewardAreasFromDatabase();
                
                Console.WriteLine("[MiniCore] База данных инициализирована.");
            }
            catch (Exception ex)
            {
                TShock.Log.ConsoleError($"[MiniCore] Ошибка инициализации БД: {ex.Message}");
            }
        }

        private void LoadSignsFromDatabase()
        {
            if (database == null) return;
            
            try
            {
                using (var reader = database.QueryReader("SELECT SpaceName, X, Y FROM MiniCore_Signs"))
                {
                    while (reader.Read())
                    {
                        string spaceName = reader.Get<string>("SpaceName");
                        int x = reader.Get<int>("X");
                        int y = reader.Get<int>("Y");
                        
                        var space = config.Spaces.FirstOrDefault(s => s.Name.Equals(spaceName, StringComparison.OrdinalIgnoreCase));
                        if (space != null)
                        {
                            if (!space.LinkedSigns.Any(s => s.X == x && s.Y == y))
                            {
                                space.LinkedSigns.Add(new SignLocation { X = x, Y = y });
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                TShock.Log.ConsoleError($"[MiniCore] Ошибка загрузки табличек из БД: {ex.Message}");
            }
        }

        private void LoadInventoriesFromDatabase()
        {
            if (database == null) return;
            
            try
            {
                using (var reader = database.QueryReader("SELECT TemplateName, InventoryData, StatLife, StatLifeMax, StatMana, StatManaMax FROM MiniCore_Inventories"))
                {
                    while (reader.Read())
                    {
                        string name = reader.Get<string>("TemplateName");
                        string invJson = reader.Get<string>("InventoryData");
                        
                        var inv = JsonConvert.DeserializeObject<InventoryData>(invJson) ?? new InventoryData();
                        inv.StatLife = reader.Get<int>("StatLife");
                        inv.StatLifeMax = reader.Get<int>("StatLifeMax");
                        inv.StatMana = reader.Get<int>("StatMana");
                        inv.StatManaMax = reader.Get<int>("StatManaMax");
                        
                        config.SpaceInventories[name] = inv;
                    }
                }
                Console.WriteLine($"[MiniCore] Загружено {config.SpaceInventories.Count} шаблонов инвентаря из БД.");
            }
            catch (Exception ex)
            {
                TShock.Log.ConsoleError($"[MiniCore] Ошибка загрузки инвентарей из БД: {ex.Message}");
            }
        }

        private void LoadCustomItemsFromDatabase()
        {
            if (database == null) return;
            
            try
            {
                using (var reader = database.QueryReader("SELECT SpaceName, ItemNetID, ParameterName, ParameterValue FROM MiniCore_CustomItems"))
                {
                    while (reader.Read())
                    {
                        string spaceName = reader.Get<string>("SpaceName");
                        int itemNetID = reader.Get<int>("ItemNetID");
                        string paramName = reader.Get<string>("ParameterName");
                        string paramValue = reader.Get<string>("ParameterValue");
                        
                        var space = config.Spaces.FirstOrDefault(s => s.Name.Equals(spaceName, StringComparison.OrdinalIgnoreCase));
                        if (space != null)
                        {
                            if (!space.ItemParameters.Any(p => p.ItemNetID == itemNetID && p.ParameterName.Equals(paramName, StringComparison.OrdinalIgnoreCase)))
                            {
                                space.ItemParameters.Add(new ItemParameter
                                {
                                    ItemNetID = itemNetID,
                                    ParameterName = paramName,
                                    ParameterValue = paramValue
                                });
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                TShock.Log.ConsoleError($"[MiniCore] Ошибка загрузки кастомных предметов: {ex.Message}");
            }
        }

        private void LoadRewardAreasFromDatabase()
        {
            if (database == null) return;
            
            try
            {
                using (var reader = database.QueryReader("SELECT * FROM MiniCore_RewardAreas"))
                {
                    while (reader.Read())
                    {
                        var area = new RewardArea
                        {
                            Name = reader.Get<string>("Name"),
                            X = reader.Get<int>("X"),
                            Y = reader.Get<int>("Y"),
                            Radius = reader.Get<int>("Radius"),
                            MoneyReward = reader.Get<int>("MoneyReward"),
                            CooldownSeconds = reader.Get<int>("CooldownSeconds")
                        };
                        
                        string lastRewardJson = reader.Get<string>("LastRewardTimes");
                        if (!string.IsNullOrEmpty(lastRewardJson))
                        {
                            try
                            {
                                area.LastRewardTimes = JsonConvert.DeserializeObject<Dictionary<int, DateTime>>(lastRewardJson) 
                                    ?? new Dictionary<int, DateTime>();
                            }
                            catch
                            {
                                area.LastRewardTimes = new Dictionary<int, DateTime>();
                            }
                        }
                        
                        config.RewardAreas.Add(area);
                    }
                }
                Console.WriteLine($"[MiniCore] Загружено {config.RewardAreas.Count} зон наград из БД.");
            }
            catch (Exception ex)
            {
                TShock.Log.ConsoleError($"[MiniCore] Ошибка загрузки зон наград: {ex.Message}");
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                // ОШИБКА 4: Используем правильный метод для отмены регистрации событий
                GetDataHandlers.ItemDrop -= OnItemDrop;
                ServerApi.Hooks.ServerChat.Deregister(this, OnChat);
                ServerApi.Hooks.GameUpdate.Deregister(this, OnGameUpdate);
                ServerApi.Hooks.ServerLeave.Deregister(this, OnLeave);
                GetDataHandlers.PlayerSpawn -= OnPlayerSpawn;
                GetDataHandlers.Sign -= OnSignInteract;
                SaveConfig();
                
                if (database != null && database.State == System.Data.ConnectionState.Open)
                {
                    database.Close();
                }
            }
            base.Dispose(disposing);
        }

        private void CreateSpace(CommandArgs args)
        {
            if (args.Parameters.Count < 1)
            {
                args.Player.SendErrorMessage("Использование: /createspace <название>");
                return;
            }

            string spaceName = string.Join(" ", args.Parameters);
            
            if (config.Spaces.Any(r => r.Name.Equals(spaceName, StringComparison.OrdinalIgnoreCase)))
            {
                args.Player.SendErrorMessage($"Простор '{spaceName}' уже существует!");
                return;
            }

            var space = new Space
            {
                Name = spaceName,
                MaxPlayers = 10,
                SpawnX = args.Player.TileX,
                SpawnY = args.Player.TileY,
                InventoryNames = new List<string>(),
                LinkedSigns = new List<SignLocation>(),
                ItemParameters = new List<ItemParameter>()
            };

            config.Spaces.Add(space);
            SaveConfig();
            
            args.Player.SendSuccessMessage($"Простор '{spaceName}' создан! Установите точку спавна с помощью /setspacespawn {spaceName}");
        }

        private void DeleteSpace(CommandArgs args)
        {
            if (args.Parameters.Count < 1)
            {
                args.Player.SendErrorMessage("Использование: /deletespace <название>");
                return;
            }

            string spaceName = string.Join(" ", args.Parameters);
            var space = config.Spaces.FirstOrDefault(r => r.Name.Equals(spaceName, StringComparison.OrdinalIgnoreCase));
            
            if (space == null)
            {
                args.Player.SendErrorMessage($"Простор '{spaceName}' не найден!");
                return;
            }

            foreach (var player in TShock.Players.Where(p => p != null && playerData.ContainsKey(p.Index)))
            {
                if (playerData[player.Index].CurrentSpace == spaceName)
                {
                    ForceLeaveSpace(player);
                }
            }

            DeleteSignsForSpace(spaceName);
            
            config.Spaces.Remove(space);
            SaveConfig();
            
            args.Player.SendSuccessMessage($"Простор '{spaceName}' удалён!");
        }

        private void DeleteSignsForSpace(string spaceName)
        {
            try
            {
                database.Query("DELETE FROM MiniCore_Signs WHERE SpaceName = @0", spaceName);
            }
            catch (Exception ex)
            {
                TShock.Log.ConsoleError($"[MiniCore] Ошибка удаления табличек: {ex.Message}");
            }
        }

        private void SetSpaceLimit(CommandArgs args)
        {
            if (args.Parameters.Count < 2)
            {
                args.Player.SendErrorMessage("Использование: /setspacelimit <название> <лимит>");
                return;
            }

            if (!int.TryParse(args.Parameters[args.Parameters.Count - 1], out int limit) || limit < 1)
            {
                args.Player.SendErrorMessage("Укажите корректный лимит игроков (число больше 0)!");
                return;
            }

            string spaceName = string.Join(" ", args.Parameters.Take(args.Parameters.Count - 1));
            var space = config.Spaces.FirstOrDefault(r => r.Name.Equals(spaceName, StringComparison.OrdinalIgnoreCase));
            
            if (space == null)
            {
                args.Player.SendErrorMessage($"Простор '{spaceName}' не найден!");
                return;
            }

            space.MaxPlayers = limit;
            SaveConfig();
            
            args.Player.SendSuccessMessage($"Лимит игроков для Простора '{spaceName}' установлен на {limit}");
        }

        private void AddInventoryToSpace(CommandArgs args)
        {
            if (args.Parameters.Count < 2)
            {
                args.Player.SendErrorMessage("Использование: /addinvspace <простор> <инвентарь>");
                return;
            }

            string invName = args.Parameters[args.Parameters.Count - 1];
            string spaceName = string.Join(" ", args.Parameters.Take(args.Parameters.Count - 1));
            
            var space = config.Spaces.FirstOrDefault(r => r.Name.Equals(spaceName, StringComparison.OrdinalIgnoreCase));
            
            if (space == null)
            {
                args.Player.SendErrorMessage($"Простор '{spaceName}' не найден!");
                return;
            }

            if (!config.SpaceInventories.ContainsKey(invName))
            {
                args.Player.SendErrorMessage($"Инвентарь '{invName}' не существует!");
                return;
            }

            if (space.InventoryNames.Any(i => i.Equals(invName, StringComparison.OrdinalIgnoreCase)))
            {
                args.Player.SendErrorMessage($"Инвентарь '{invName}' уже привязан к этому простору!");
                return;
            }

            space.InventoryNames.Add(invName);
            SaveConfig();
            
            args.Player.SendSuccessMessage($"Инвентарь '{invName}' добавлен к Простору '{spaceName}'");
        }

        private void RemoveInventoryFromSpace(CommandArgs args)
        {
            if (args.Parameters.Count < 2)
            {
                args.Player.SendErrorMessage("Использование: /removeinvspace <простор> <инвентарь>");
                return;
            }

            string invName = args.Parameters[args.Parameters.Count - 1];
            string spaceName = string.Join(" ", args.Parameters.Take(args.Parameters.Count - 1));
            
            var space = config.Spaces.FirstOrDefault(r => r.Name.Equals(spaceName, StringComparison.OrdinalIgnoreCase));
            
            if (space == null)
            {
                args.Player.SendErrorMessage($"Простор '{spaceName}' не найден!");
                return;
            }

            if (space.InventoryNames.RemoveAll(i => i.Equals(invName, StringComparison.OrdinalIgnoreCase)) == 0)
            {
                args.Player.SendErrorMessage($"Инвентарь '{invName}' не найден в этом простора!");
                return;
            }

            SaveConfig();
            args.Player.SendSuccessMessage($"Инвентарь '{invName}' удален из Простора '{spaceName}'");
        }

        private void ListSpaceInventories(CommandArgs args)
        {
            if (args.Parameters.Count < 1)
            {
                args.Player.SendErrorMessage("Использование: /listspaceinv <простор>");
                return;
            }

            string spaceName = string.Join(" ", args.Parameters);
            var space = config.Spaces.FirstOrDefault(r => r.Name.Equals(spaceName, StringComparison.OrdinalIgnoreCase));
            
            if (space == null)
            {
                args.Player.SendErrorMessage($"Простор '{spaceName}' не найден!");
                return;
            }

            if (space.InventoryNames.Count == 0)
            {
                args.Player.SendInfoMessage($"К Простору '{spaceName}' не привязано ни одного инвентаря!");
                return;
            }

            args.Player.SendInfoMessage($"=== Инвентари Простора '{spaceName}' ===");
            foreach (var inv in space.InventoryNames)
            {
                args.Player.SendInfoMessage($"• {inv}");
            }
        }

        private void SetLimitedInventory(CommandArgs args)
        {
            if (args.Parameters.Count < 2)
            {
                args.Player.SendErrorMessage("Использование: /invranlim <простор> <кол-во> [инвентарь]");
                args.Player.SendErrorMessage("Кол-во: 0 - отключить, >0 - кол-во игроков с инвентарём");
                return;
            }

            int count = 0;
            if (!int.TryParse(args.Parameters[args.Parameters.Count - 2], out count) || count < 0)
            {
                args.Player.SendErrorMessage("Кол-во должно быть числом >= 0!");
                return;
            }

            string spaceName = string.Join(" ", args.Parameters.Take(args.Parameters.Count - 2));
            var space = config.Spaces.FirstOrDefault(r => r.Name.Equals(spaceName, StringComparison.OrdinalIgnoreCase));
            
            if (space == null)
            {
                args.Player.SendErrorMessage($"Простор '{spaceName}' не найден!");
                return;
            }

            if (count == 0)
            {
                space.LimitedInvEnabled = false;
                space.LimitedInvCount = 0;
                space.LimitedInvName = "";
                SaveConfig();
                args.Player.SendSuccessMessage($"Лимитированный инвентарь отключен для простора '{spaceName}'");
                return;
            }

            string invName = args.Parameters[args.Parameters.Count - 1];
            if (!config.SpaceInventories.ContainsKey(invName))
            {
                args.Player.SendErrorMessage($"Инвентарь '{invName}' не существует!");
                return;
            }

            space.LimitedInvEnabled = true;
            space.LimitedInvCount = count;
            space.LimitedInvName = invName;
            SaveConfig();
            
            args.Player.SendSuccessMessage($"Лимитированный инвентарь установлен: {count} игроков получат '{invName}'");
        }

        private void SetDeadInventory(CommandArgs args)
        {
            if (args.Parameters.Count < 2)
            {
                args.Player.SendErrorMessage("Использование: /setinvdead <простор> <инвентарь>");
                return;
            }

            string invName = args.Parameters[args.Parameters.Count - 1];
            string spaceName = string.Join(" ", args.Parameters.Take(args.Parameters.Count - 1));
            
            var space = config.Spaces.FirstOrDefault(r => r.Name.Equals(spaceName, StringComparison.OrdinalIgnoreCase));
            
            if (space == null)
            {
                args.Player.SendErrorMessage($"Простор '{spaceName}' не найден!");
                return;
            }

            if (!config.SpaceInventories.ContainsKey(invName))
            {
                args.Player.SendErrorMessage($"Инвентарь '{invName}' не существует!");
                return;
            }

            space.DeadInventoryName = invName;
            SaveConfig();
            
            args.Player.SendSuccessMessage($"Инвентарь после смерти установлен на '{invName}' для простора '{spaceName}'");
        }

        private void SetDeadRespawn(CommandArgs args)
        {
            if (args.Parameters.Count < 1)
            {
                args.Player.SendErrorMessage("Использование: /setrespawndead <простор> (затем стань на месте)");
                return;
            }

            string spaceName = string.Join(" ", args.Parameters);
            var space = config.Spaces.FirstOrDefault(r => r.Name.Equals(spaceName, StringComparison.OrdinalIgnoreCase));
            
            if (space == null)
            {
                args.Player.SendErrorMessage($"Простор '{spaceName}' не найден!");
                return;
            }

            if (!playerData.ContainsKey(args.Player.Index))
            {
                playerData[args.Player.Index] = new PlayerData();
            }

            playerData[args.Player.Index].AwaitingDeadRespawnSet = true;
            args.Player.SendSuccessMessage($"Стань на место респавна после смерти для простора '{spaceName}'");
        }

        private void SetBuffSpace(CommandArgs args)
        {
            if (args.Parameters.Count < 2)
            {
                args.Player.SendErrorMessage("Использование: /setbuffspace <простор> <buffid>");
                args.Player.SendErrorMessage("buffid = 0 чтобы отключить");
                return;
            }

            int buffId = 0;
            if (!int.TryParse(args.Parameters[args.Parameters.Count - 1], out buffId))
            {
                args.Player.SendErrorMessage("BuffID должен быть числом!");
                return;
            }

            string spaceName = string.Join(" ", args.Parameters.Take(args.Parameters.Count - 1));
            var space = config.Spaces.FirstOrDefault(r => r.Name.Equals(spaceName, StringComparison.OrdinalIgnoreCase));
            
            if (space == null)
            {
                args.Player.SendErrorMessage($"Простор '{spaceName}' не найден!");
                return;
            }

            space.BuffID = buffId;
            SaveConfig();
            
            if (buffId == 0)
                args.Player.SendSuccessMessage($"Дебафф отключен для простора '{spaceName}'");
            else
                args.Player.SendSuccessMessage($"Дебафф ID {buffId} установлен для простора '{spaceName}'");
        }

        private void SetTimerStart(CommandArgs args)
        {
            if (args.Parameters.Count < 2)
            {
                args.Player.SendErrorMessage("Использование: /settimerstart <простор> <секунды>");
                return;
            }

            int seconds = 0;
            if (!int.TryParse(args.Parameters[args.Parameters.Count - 1], out seconds) || seconds < 0)
            {
                args.Player.SendErrorMessage("Секунды должны быть числом >= 0!");
                return;
            }

            string spaceName = string.Join(" ", args.Parameters.Take(args.Parameters.Count - 1));
            var space = config.Spaces.FirstOrDefault(r => r.Name.Equals(spaceName, StringComparison.OrdinalIgnoreCase));
            
            if (space == null)
            {
                args.Player.SendErrorMessage($"Простор '{spaceName}' не найден!");
                return;
            }

            space.TimerStartSeconds = seconds;
            SaveConfig();
            
            if (seconds == 0)
                args.Player.SendSuccessMessage($"Таймер отключен для простора '{spaceName}'");
            else
                args.Player.SendSuccessMessage($"Таймер на {seconds} сек установлен для простора '{spaceName}'");
        }

        private void PickInventory(CommandArgs args)
        {
            if (args.Parameters.Count < 1)
            {
                args.Player.SendErrorMessage("Использование: /pickinv <инвентарь>");
                return;
            }

            if (!playerData.ContainsKey(args.Player.Index) || string.IsNullOrEmpty(playerData[args.Player.Index].CurrentSpace))
            {
                args.Player.SendErrorMessage("Вы не находитесь в простое!");
                return;
            }

            var space = config.Spaces.FirstOrDefault(s => s.Name.Equals(playerData[args.Player.Index].CurrentSpace, StringComparison.OrdinalIgnoreCase));
            if (space == null)
            {
                args.Player.SendErrorMessage("Простор не найден!");
                return;
            }

            string invName = string.Join(" ", args.Parameters);
            if (!space.InventoryNames.Any(i => i.Equals(invName, StringComparison.OrdinalIgnoreCase)))
            {
                args.Player.SendErrorMessage($"Инвентарь '{invName}' не привязан к этому простору!");
                return;
            }

            if (!config.SpaceInventories.ContainsKey(invName))
            {
                args.Player.SendErrorMessage($"Инвентарь '{invName}' не существует!");
                return;
            }

            playerData[args.Player.Index].SelectedInventory = invName;
            RestorePlayerInventoryWithSync(args.Player, config.SpaceInventories[invName], space);
            args.Player.SendSuccessMessage($"Вы выбрали инвентарь '{invName}'");
        }

        private void SaveInventoryTemplate(CommandArgs args)
        {
            if (args.Parameters.Count < 1)
            {
                args.Player.SendErrorMessage("Использование: /saveinv <название_шаблона>");
                return;
            }

            if (database == null)
            {
                args.Player.SendErrorMessage("База данных не инициализирована!");
                return;
            }

            string templateName = string.Join(" ", args.Parameters);
            
            try
            {
                var inventory = SavePlayerInventory(args.Player);
                
                if (inventory == null)
                {
                    args.Player.SendErrorMessage("Не удалось сохранить инвентарь!");
                    return;
                }
                
                string invJson = JsonConvert.SerializeObject(inventory);
                
                if (config.SpaceInventories.ContainsKey(templateName))
                {
                    database.Query(@"UPDATE MiniCore_Inventories 
                        SET InventoryData = @0, StatLife = @1, StatLifeMax = @2, StatMana = @3, StatManaMax = @4 
                        WHERE TemplateName = @5",
                        invJson, inventory.StatLife, inventory.StatLifeMax, 
                        inventory.StatMana, inventory.StatManaMax, templateName);
                }
                else
                {
                    database.Query(@"INSERT INTO MiniCore_Inventories 
                        (TemplateName, InventoryData, StatLife, StatLifeMax, StatMana, StatManaMax, CreatedAt) 
                        VALUES (@0, @1, @2, @3, @4, @5, @6)",
                        templateName, invJson, inventory.StatLife, inventory.StatLifeMax,
                        inventory.StatMana, inventory.StatManaMax, DateTime.Now);
                }
                
                config.SpaceInventories[templateName] = inventory;
                
                args.Player.SendSuccessMessage($"Шаблон '{templateName}' сохранён в базу данных!");
                args.Player.SendInfoMessage($"HP: {inventory.StatLife}/{inventory.StatLifeMax}, Мана: {inventory.StatMana}/{inventory.StatManaMax}");
                args.Player.SendInfoMessage($"Предметов: {inventory.Items.Count(i => i.NetID != 0)}, Броня: {inventory.Armor.Count(i => i.NetID != 0)}");
            }
            catch (Exception ex)
            {
                args.Player.SendErrorMessage($"Ошибка сохранения: {ex.Message}");
                TShock.Log.ConsoleError($"[MiniCore] Ошибка сохранения инвентаря: {ex.Message}\n{ex.StackTrace}");
            }
        }

        private void DeleteInventoryTemplate(CommandArgs args)
        {
            if (args.Parameters.Count < 1)
            {
                args.Player.SendErrorMessage("Использование: /delinv <название_шаблона>");
                return;
            }

            string templateName = string.Join(" ", args.Parameters);
            
            if (!config.SpaceInventories.ContainsKey(templateName))
            {
                args.Player.SendErrorMessage($"Шаблон '{templateName}' не найден!");
                return;
            }
            
            try
            {
                database.Query("DELETE FROM MiniCore_Inventories WHERE TemplateName = @0", templateName);
                config.SpaceInventories.Remove(templateName);
                args.Player.SendSuccessMessage($"Шаблон '{templateName}' удалён из базы данных!");
            }
            catch (Exception ex)
            {
                args.Player.SendErrorMessage($"Ошибка удаления: {ex.Message}");
            }
        }

        private void ListInventoryTemplates(CommandArgs args)
        {
            if (config.SpaceInventories.Count == 0)
            {
                args.Player.SendInfoMessage("Нет сохранённых шаблонов инвентаря.");
                return;
            }
            
            args.Player.SendInfoMessage("=== Шаблоны инвентаря ===");
            foreach (var kvp in config.SpaceInventories)
            {
                var inv = kvp.Value;
                int itemCount = inv.Items.Count(i => i.NetID != 0);
                args.Player.SendInfoMessage($"• {kvp.Key} (HP: {inv.StatLife}/{inv.StatLifeMax}, Мана: {inv.StatMana}/{inv.StatManaMax}, Предметов: {itemCount})");
            }
        }

        private void CreateEmptyTemplate(CommandArgs args)
        {
            if (args.Parameters.Count < 1)
            {
                args.Player.SendErrorMessage("Использование: /createinv <название_шаблона>");
                return;
            }

            string templateName = string.Join(" ", args.Parameters);
            
            if (config.SpaceInventories.ContainsKey(templateName))
            {
                args.Player.SendErrorMessage($"Шаблон '{templateName}' уже существует!");
                return;
            }

            var inventory = new InventoryData
            {
                Items = new List<ItemData>(),
                Armor = new List<ItemData>(),
                Dyes = new List<ItemData>(),
                MiscEquips = new List<ItemData>(),
                MiscDyes = new List<ItemData>(),
                StatLife = 100,
                StatLifeMax = 100,
                StatMana = 20,
                StatManaMax = 20
            };
            
            for (int i = 0; i < 59; i++) inventory.Items.Add(new ItemData());
            for (int i = 0; i < 20; i++) inventory.Armor.Add(new ItemData());
            for (int i = 0; i < 10; i++) inventory.Dyes.Add(new ItemData());
            for (int i = 0; i < 5; i++) inventory.MiscEquips.Add(new ItemData());
            for (int i = 0; i < 5; i++) inventory.MiscDyes.Add(new ItemData());

            try
            {
                if (database != null)
                {
                    string invJson = JsonConvert.SerializeObject(inventory);
                    
                    database.Query(@"INSERT INTO MiniCore_Inventories 
                        (TemplateName, InventoryData, StatLife, StatLifeMax, StatMana, StatManaMax, CreatedAt) 
                        VALUES (@0, @1, @2, @3, @4, @5, @6)",
                        templateName, invJson, inventory.StatLife, inventory.StatLifeMax,
                        inventory.StatMana, inventory.StatManaMax, DateTime.Now);
                }
                
                config.SpaceInventories[templateName] = inventory;
                args.Player.SendSuccessMessage($"Пустой шаблон '{templateName}' создан!");
                args.Player.SendInfoMessage("Используйте /setinvstats и /addinvitem для настройки");
            }
            catch (Exception ex)
            {
                args.Player.SendErrorMessage($"Ошибка создания: {ex.Message}");
            }
        }

        private void SetTemplateStats(CommandArgs args)
        {
            if (args.Parameters.Count < 5)
            {
                args.Player.SendErrorMessage("Использование: /setinvstats <шаблон> <hp> <hpmax> <мана> <манаmax>");
                args.Player.SendInfoMessage("Пример: /setinvstats pvp_kit 100 200 50 100");
                return;
            }

            if (!int.TryParse(args.Parameters[args.Parameters.Count - 4], out int hp) ||
                !int.TryParse(args.Parameters[args.Parameters.Count - 3], out int hpMax) ||
                !int.TryParse(args.Parameters[args.Parameters.Count - 2], out int mana) ||
                !int.TryParse(args.Parameters[args.Parameters.Count - 1], out int manaMax))
            {
                args.Player.SendErrorMessage("Все значения должны быть числами!");
                return;
            }

            string templateName = string.Join(" ", args.Parameters.Take(args.Parameters.Count - 4));
            
            if (!config.SpaceInventories.ContainsKey(templateName))
            {
                args.Player.SendErrorMessage($"Шаблон '{templateName}' не найден!");
                return;
            }

            var inv = config.SpaceInventories[templateName];
            inv.StatLife = hp;
            inv.StatLifeMax = hpMax;
            inv.StatMana = mana;
            inv.StatManaMax = manaMax;

            try
            {
                string invJson = JsonConvert.SerializeObject(inv);
                
                database.Query(@"UPDATE MiniCore_Inventories 
                    SET InventoryData = @0, StatLife = @1, StatLifeMax = @2, StatMana = @3, StatManaMax = @4 
                    WHERE TemplateName = @5",
                    invJson, hp, hpMax, mana, manaMax, templateName);
                
                args.Player.SendSuccessMessage($"Параметры шаблона '{templateName}' обновлены!");
            }
            catch (Exception ex)
            {
                args.Player.SendErrorMessage($"Ошибка обновления: {ex.Message}");
            }
        }

        private void AddTemplateItem(CommandArgs args)
        {
            if (args.Parameters.Count < 4)
            {
                args.Player.SendErrorMessage("Использование: /addinvitem <шаблон> <слот> <предмет> [кол-во] [префикс]");
                args.Player.SendInfoMessage("Слоты: items (0-58), armor (0-19), dyes (0-9), miscequips (0-4), miscdyes (0-4)");
                return;
            }

            string templateName = args.Parameters[0];
            string slotType = args.Parameters[1].ToLower();
            
            if (!int.TryParse(args.Parameters[2], out int itemId))
            {
                args.Player.SendErrorMessage("Укажите корректный ID предмета!");
                return;
            }

            int stack = 1;
            if (args.Parameters.Count > 3 && int.TryParse(args.Parameters[3], out int s))
                stack = s;

            byte prefix = 0;
            if (args.Parameters.Count > 4 && byte.TryParse(args.Parameters[4], out byte p))
                prefix = p;

            if (!config.SpaceInventories.ContainsKey(templateName))
            {
                args.Player.SendErrorMessage($"Шаблон '{templateName}' не найден!");
                return;
            }

            var inv = config.SpaceInventories[templateName];

            try
            {
                if (slotType == "items")
                {
                    if (!int.TryParse(args.Parameters[1], out int slot) || slot < 0 || slot >= inv.Items.Count)
                    {
                        args.Player.SendErrorMessage("Некорректный номер слота!");
                        return;
                    }
                    inv.Items[slot] = new ItemData { NetID = itemId, Stack = stack, Prefix = prefix };
                }
                else if (slotType == "armor")
                {
                    if (!int.TryParse(args.Parameters[1], out int slot) || slot < 0 || slot >= inv.Armor.Count)
                    {
                        args.Player.SendErrorMessage("Некорректный номер слота!");
                        return;
                    }
                    inv.Armor[slot] = new ItemData { NetID = itemId, Stack = stack, Prefix = prefix };
                }

                SavePlayerInventoryToDatabase(templateName, inv);
                args.Player.SendSuccessMessage($"Предмет добавлен в шаблон '{templateName}'!");
            }
            catch (Exception ex)
            {
                args.Player.SendErrorMessage($"Ошибка добавления: {ex.Message}");
            }
        }

        private void SavePlayerInventoryToDatabase(string templateName, InventoryData inv)
        {
            string invJson = JsonConvert.SerializeObject(inv);
            
            database.Query(@"UPDATE MiniCore_Inventories 
                SET InventoryData = @0, StatLife = @1, StatLifeMax = @2, StatMana = @3, StatManaMax = @4 
                WHERE TemplateName = @5",
                invJson, inv.StatLife, inv.StatLifeMax, inv.StatMana, inv.StatManaMax, templateName);
        }

        private void LinkSign(CommandArgs args)
        {
            if (args.Parameters.Count < 1)
            {
                args.Player.SendErrorMessage("Использование: /linksign <название_простора>");
                args.Player.SendErrorMessage("Затем кликните на табличку, которую хотите привязать.");
                return;
            }

            string spaceName = string.Join(" ", args.Parameters);
            
            if (!config.Spaces.Any(s => s.Name.Equals(spaceName, StringComparison.OrdinalIgnoreCase)))
            {
                args.Player.SendErrorMessage($"Простор '{spaceName}' не найден!");
                return;
            }

            if (!playerData.ContainsKey(args.Player.Index))
            {
                playerData[args.Player.Index] = new PlayerData();
            }

            playerData[args.Player.Index].AwaitingSignLink = spaceName;
            args.Player.SendSuccessMessage($"Кликните на табличку, чтобы привязать её к Простору '{spaceName}'.");
        }

        private void UnlinkSign(CommandArgs args)
        {
            if (args.Parameters.Count < 1)
            {
                args.Player.SendErrorMessage("Использование: /unlinksign <название_простора>");
                args.Player.SendErrorMessage("Затем кликните на табличку, которую хотите отвязать.");
                return;
            }

            string spaceName = string.Join(" ", args.Parameters);
            
            if (!playerData.ContainsKey(args.Player.Index))
            {
                playerData[args.Player.Index] = new PlayerData();
            }

            playerData[args.Player.Index].AwaitingSignUnlink = true;
            args.Player.SendSuccessMessage($"Кликните на табличку, чтобы отвязать её от Простора '{spaceName}'.");
        }

        private void SetSpaceSpawn(CommandArgs args)
        {
            if (args.Parameters.Count < 1)
            {
                args.Player.SendErrorMessage("Использование: /setspacespawn <название>");
                return;
            }

            string spaceName = string.Join(" ", args.Parameters);
            var space = config.Spaces.FirstOrDefault(s => s.Name.Equals(spaceName, StringComparison.OrdinalIgnoreCase));
            
            if (space == null)
            {
                args.Player.SendErrorMessage($"Простор '{spaceName}' не найден!");
                return;
            }

            space.SpawnX = args.Player.TileX;
            space.SpawnY = args.Player.TileY;
            SaveConfig();
            
            args.Player.SendSuccessMessage($"Точка спавна для Простора '{spaceName}' установлена на [{space.SpawnX}, {space.SpawnY}]!");
        }

        private void JoinSpace(CommandArgs args)
        {
            if (args.Parameters.Count < 1)
            {
                args.Player.SendErrorMessage("Использование: /joinspace <название>");
                return;
            }

            string spaceName = string.Join(" ", args.Parameters);
            JoinSpaceInternal(args.Player, spaceName);
        }

        private void JoinSpaceInternal(TSPlayer player, string spaceName)
        {
            var space = config.Spaces.FirstOrDefault(s => s.Name.Equals(spaceName, StringComparison.OrdinalIgnoreCase));
            
            if (space == null)
            {
                player.SendErrorMessage($"Простор '{spaceName}' не найден!");
                return;
            }

            int currentPlayers = TShock.Players.Count(p => p != null && playerData.ContainsKey(p.Index) && playerData[p.Index].CurrentSpace == spaceName);
            
            if (currentPlayers >= space.MaxPlayers)
            {
                player.SendErrorMessage($"Простор '{spaceName}' переполнен!");
                return;
            }

            if (!playerData.ContainsKey(player.Index))
            {
                playerData[player.Index] = new PlayerData();
            }

            var pData = playerData[player.Index];

            if (pData.CurrentSpace != null && !pData.CurrentSpace.Equals(spaceName, StringComparison.OrdinalIgnoreCase))
            {
                ForceLeaveSpace(player);
            }

            pData.LobbyInventory = SavePlayerInventory(player);
            pData.CurrentSpace = spaceName;
            pData.OriginalPvPState = player.TPlayer.hostile;
            player.TPlayer.hostile = true;

            NetMessage.SendData((int)PacketTypes.TogglePvp, -1, -1, null, player.Index);

            LoadSpaceInventoriesWithSync(player, space.InventoryNames, space);
            
            // Random spawn logic (regenerate every 10 minutes)
            long currentTime = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            if (currentTime - space.LastRandomSpawnTime >= 600) // 600 seconds = 10 minutes
            {
                space.RandomSpawnX = random.Next(100, Main.maxTilesX - 100);
                space.RandomSpawnY = random.Next(100, Main.maxTilesY - 100);
                space.LastRandomSpawnTime = currentTime;
                SaveConfig();
            }
            
            // Teleport to random spawn if available, else use default spawn
            int teleportX = space.RandomSpawnX > 0 ? space.RandomSpawnX : space.SpawnX;
            int teleportY = space.RandomSpawnY > 0 ? space.RandomSpawnY : space.SpawnY;
            player.Teleport(teleportX * 16, teleportY * 16);

            // Apply buff if set
            if (space.BuffID > 0)
            {
                player.SetBuff(space.BuffID, 99999);
            }

            player.SendSuccessMessage($"Добро пожаловать в Простор '{spaceName}'!");
            
            BroadcastToSpace(spaceName, $"{player.Name} присоединился к Простору!", Color.Yellow, player.Index);
        }

        private void LeaveSpace(CommandArgs args)
        {
            if (!playerData.ContainsKey(args.Player.Index))
            {
                args.Player.SendErrorMessage("Вы не находитесь в простое!");
                return;
            }

            ForceLeaveSpace(args.Player);
        }

        private void ForceLeaveSpace(TSPlayer player)
        {
            if (!playerData.ContainsKey(player.Index))
                return;

            var pData = playerData[player.Index];
            string spaceName = pData.CurrentSpace;

            if (pData.LobbyInventory != null)
            {
                RestorePlayerInventoryWithSync(player, pData.LobbyInventory);
            }

            player.TPlayer.hostile = pData.OriginalPvPState;
            NetMessage.SendData((int)PacketTypes.TogglePvp, -1, -1, null, player.Index);

            pData.CurrentSpace = null;
            pData.LobbyInventory = null;
            pData.SelectedInventory = null;
            pData.HasLimitedInventory = false;

            if (!string.IsNullOrEmpty(spaceName))
            {
                BroadcastToSpace(spaceName, $"{player.Name} покинул Простор!", Color.Yellow, player.Index);
                player.SendSuccessMessage($"Вы покинули Простор '{spaceName}'!");
            }
        }

        private void ShowHelp(CommandArgs args)
        {
            args.Player.SendMessage("╔══════════════════════════════════════════════════╗", Color.Cyan);
            args.Player.SendMessage("║          СПРАВКА ПО КОМАНДАМ MINICORE             ║", Color.Cyan);
            args.Player.SendMessage("╚══════════════════════════════════════════════════╝", Color.Cyan);
            
            args.Player.SendMessage("─────────────────── КОМАНДЫ АДМИНИСТРАТОРА ───────────────────", Color.Yellow);
            args.Player.SendMessage("/createspace <название>  (/cs) - Создать новый простор", Color.White);
            args.Player.SendMessage("/deletespace <название>  (/ds) - Удалить простор", Color.White);
            args.Player.SendMessage("/setspacelimit <название> <лимит> (/ssl) - Установить макс. игроков", Color.White);
            args.Player.SendMessage("/setspacespawn <название> (/sss) - Установить точку спавна (стоишь на месте)", Color.White);
            args.Player.SendMessage("/addinvspace <простор> <инвентарь> (/ais) - Добавить инвентарь к простору", Color.White);
            args.Player.SendMessage("/removeinvspace <простор> <инвентарь> (/ris) - Удалить инвентарь из простора", Color.White);
            args.Player.SendMessage("/listspaceinv <простор> (/lsi) - Показать инвентари простора", Color.White);
            args.Player.SendMessage("/invranlim <простор> <кол-во> <инвентарь> (/irl) - Лимит инвентаря (Murder режим)", Color.White);
            args.Player.SendMessage("/setinvdead <простор> <инвентарь> (/ssd) - Инвентарь при смерти", Color.White);
            args.Player.SendMessage("/setrespawndead <простор> (/srd) - Точка спавна при смерти (стань на месте)", Color.White);
            args.Player.SendMessage("/setbuffspace <простор> <buffid> (/sbs) - Установить дебафф при входе (0 чтобы отключить)", Color.White);
            args.Player.SendMessage("/settimerstart <простор> <секунды> (/sts) - Обратный отсчёт до старта", Color.White);
            args.Player.SendMessage("", Color.White);
            
            args.Player.SendMessage("──────────────────── КОМАНДЫ ИГРОКОВ ──────────────────────", Color.Yellow);
            args.Player.SendMessage("/pickinv <инвентарь> (/pi) - Выбрать инвентарь в простое", Color.White);
            
            args.Player.SendMessage("─────────────────── УПРАВЛЕНИЕ ИНВЕНТАРЁМ ───────────────────", Color.Yellow);
            args.Player.SendMessage("/saveinv <название> (/si) - Сохранить текущий инвентарь как шаблон", Color.White);
            args.Player.SendMessage("/delinv <название> (/di) - Удалить шаблон инвентаря", Color.White);
            args.Player.SendMessage("/listinv (/li) - Список всех шаблонов инвентаря", Color.White);
            args.Player.SendMessage("/createinv <название> (/ci) - Создать пустой шаблон", Color.White);
            args.Player.SendMessage("/setinvstats <шаблон> <hp> <hpmax> <мана> <манаmax> (/sis) - Установить статы", Color.White);
            args.Player.SendMessage("/addinvitem <шаблон> <слот> <предмет> [кол-во] [префикс] (/aii) - Добавить предмет", Color.White);
            args.Player.SendMessage("", Color.White);
            
            args.Player.SendMessage("──────────────────── УПРАВЛЕНИЕ ТАБЛИЧКИ ─────────────────────", Color.Yellow);
            args.Player.SendMessage("/linksign <простор> (/lsign) - Привязать табличку к простору (кликни после)", Color.White);
            args.Player.SendMessage("/unlinksign <простор> (/ulsign) - Отвязать табличку от простора (кликни после)", Color.White);
            args.Player.SendMessage("", Color.White);
            
            args.Player.SendMessage("──────────────────── КОМАНДЫ ИГРОКОВ ──────────────────────", Color.Yellow);
            args.Player.SendMessage("/joinspace <название> (/js) - Войти в простор", Color.White);
            args.Player.SendMessage("/leavespace (/ls) - Выйти из простора", Color.White);
            args.Player.SendMessage("/listspaces (/spaces) - Список доступных просторов", Color.White);
            args.Player.SendMessage("/mhelp (/minihelp) - Показать эту справку", Color.White);
            args.Player.SendMessage("", Color.White);
            
            args.Player.SendMessage("╔══════════════════════════════════════════════════╗", Color.Cyan);
            args.Player.SendMessage("║ Сокращённые команды показаны в скобках ()        ║", Color.Cyan);
            args.Player.SendMessage("╚══════════════════════════════════════════════════╝", Color.Cyan);
        }

        private void SetItemParameter(CommandArgs args)
        {
            if (args.Parameters.Count < 3)
            {
                args.Player.SendErrorMessage("Использование: /setitemparam <пространство> <netid> <параметр> <значение>");
                args.Player.SendInfoMessage("Доступные параметры: damage, cooldown, mana, knockback, crit, color");
                return;
            }

            if (!int.TryParse(args.Parameters[1], out int netID))
            {
                args.Player.SendErrorMessage("NetID должен быть числом!");
                return;
            }

            string spaceName = args.Parameters[0];
            string paramName = args.Parameters[2].ToLower();
            string paramValue = string.Join(" ", args.Parameters.Skip(3));

            var space = config.Spaces.FirstOrDefault(s => s.Name.Equals(spaceName, StringComparison.OrdinalIgnoreCase));
            if (space == null)
            {
                args.Player.SendErrorMessage($"Пространство '{spaceName}' не найдено!");
                return;
            }

            // Валидация параметров
            if (!ValidateItemParameter(paramName, paramValue, args))
                return;

            var existingParam = space.ItemParameters.FirstOrDefault(p => p.ItemNetID == netID && p.ParameterName.Equals(paramName, StringComparison.OrdinalIgnoreCase));
            if (existingParam != null)
            {
                existingParam.ParameterValue = paramValue;
                args.Player.SendSuccessMessage($"Параметр '{paramName}' обновлён: {paramValue}");
            }
            else
            {
                space.ItemParameters.Add(new ItemParameter
                {
                    ItemNetID = netID,
                    ParameterName = paramName,
                    ParameterValue = paramValue
                });
                args.Player.SendSuccessMessage($"Параметр '{paramName}' установлен для предмета {netID}");
            }

            // Сохранение в БД и конфиг
            SaveConfig();
            SaveItemParameterToDatabase(space.Name, netID, paramName, paramValue);
            BroadcastToSpace(space.Name, $"[Система] Параметр предмета {netID} ({paramName}) изменён", Color.Yellow);
        }

        private bool ValidateItemParameter(string paramName, string paramValue, CommandArgs args)
        {
            switch (paramName)
            {
                case "damage":
                case "crit":
                case "cooldown":
                case "mana":
                case "knockback":
                    if (!float.TryParse(paramValue, out _))
                    {
                        args.Player.SendErrorMessage($"Параметр '{paramName}' должен быть числом!");
                        return false;
                    }
                    break;
                case "color":
                    if (paramValue.Length != 6 || !System.Text.RegularExpressions.Regex.IsMatch(paramValue, @"^[0-9A-Fa-f]{6}$"))
                    {
                        args.Player.SendErrorMessage("Цвет должен быть в формате HEX (RRGGBB)!");
                        return false;
                    }
                    break;
                default:
                    break;
            }
            return true;
        }

        private void SaveItemParameterToDatabase(string spaceName, int itemNetID, string paramName, string paramValue)
        {
            if (database == null) return;
            
            try
            {
                database.Query(@"DELETE FROM MiniCore_CustomItems 
                    WHERE SpaceName = @0 AND ItemNetID = @1 AND ParameterName = @2",
                    spaceName, itemNetID, paramName);
                
                database.Query(@"INSERT INTO MiniCore_CustomItems 
                    (SpaceName, ItemNetID, ParameterName, ParameterValue, CreatedAt) 
                    VALUES (@0, @1, @2, @3, @4)",
                    spaceName, itemNetID, paramName, paramValue, DateTime.UtcNow);
            }
            catch (Exception ex)
            {
                TShock.Log.ConsoleError($"[MiniCore] Ошибка сохранения параметра: {ex.Message}");
            }
        }

        private void GetItemParameter(CommandArgs args)
        {
            if (args.Parameters.Count < 3)
            {
                args.Player.SendErrorMessage("Использование: /getitemparam <пространство> <netid> <параметр>");
                return;
            }

            if (!int.TryParse(args.Parameters[1], out int netID))
            {
                args.Player.SendErrorMessage("NetID должен быть числом!");
                return;
            }

            string spaceName = args.Parameters[0];
            string paramName = args.Parameters[2];

            var space = config.Spaces.FirstOrDefault(s => s.Name.Equals(spaceName, StringComparison.OrdinalIgnoreCase));
            if (space == null)
            {
                args.Player.SendErrorMessage($"Пространство '{spaceName}' не найдено!");
                return;
            }

            var param = space.ItemParameters.FirstOrDefault(p => p.ItemNetID == netID && p.ParameterName.Equals(paramName, StringComparison.OrdinalIgnoreCase));
            if (param != null)
            {
                args.Player.SendInfoMessage($"Параметр '{paramName}' для предмета {netID}: '{param.ParameterValue}'");
            }
            else
            {
                args.Player.SendErrorMessage($"Параметр '{paramName}' не найден для предмета {netID}");
            }
        }

        private void ListItemParameters(CommandArgs args)
        {
            if (args.Parameters.Count < 1)
            {
                args.Player.SendErrorMessage("Использование: /listitemparams <пространство> [netid]");
                return;
            }

            string spaceName = args.Parameters[0];
            var space = config.Spaces.FirstOrDefault(s => s.Name.Equals(spaceName, StringComparison.OrdinalIgnoreCase));

            if (space == null)
            {
                args.Player.SendErrorMessage($"Пространство '{spaceName}' не найдено!");
                return;
            }

            int filterNetID = null;
            if (args.Parameters.Count > 1 && int.TryParse(args.Parameters[1], out int nid))
            {
                filterNetID = nid;
            }

            var paramsToShow = filterNetID.HasValue 
                ? space.ItemParameters.Where(p => p.ItemNetID == filterNetID).ToList()
                : space.ItemParameters;

            if (paramsToShow.Count == 0)
            {
                args.Player.SendInfoMessage("Параметры не найдены.");
                return;
            }

            args.Player.SendInfoMessage($"=== Параметры в пространстве '{spaceName}' ===");
            foreach (var param in paramsToShow)
            {
                args.Player.SendInfoMessage($"• NetID {param.ItemNetID}: {param.ParameterName} = '{param.ParameterValue}'");
            }
        }

        private void RemoveItemParameter(CommandArgs args)
        {
            if (args.Parameters.Count < 3)
            {
                args.Player.SendErrorMessage("Использование: /removeitemparam <пространство> <netid> <параметр>");
                return;
            }

            if (!int.TryParse(args.Parameters[1], out int netID))
            {
                args.Player.SendErrorMessage("NetID должен быть числом!");
                return;
            }

            string spaceName = args.Parameters[0];
            string paramName = args.Parameters[2];

            var space = config.Spaces.FirstOrDefault(s => s.Name.Equals(spaceName, StringComparison.OrdinalIgnoreCase));
            if (space == null)
            {
                args.Player.SendErrorMessage($"Пространство '{spaceName}' не найдено!");
                return;
            }

            int removed = space.ItemParameters.RemoveAll(p => p.ItemNetID == netID && p.ParameterName.Equals(paramName, StringComparison.OrdinalIgnoreCase));
            if (removed > 0)
            {
                args.Player.SendSuccessMessage($"Параметр '{paramName}' удалён для предмета {netID}");
                SaveConfig();
                BroadcastToSpace(space.Name, $"[Система] Параметр предмета {netID} удалён", Color.Yellow);
            }
            else
            {
                args.Player.SendErrorMessage($"Параметр не найден");
            }
        }

        private void ListSpaces(CommandArgs args)
        {
            if (config.Spaces.Count == 0)
            {
                args.Player.SendInfoMessage("Нет доступных Просторов.");
                return;
            }

            args.Player.SendInfoMessage("=== Доступные Просторы ===");
            foreach (var space in config.Spaces)
            {
                int currentPlayers = TShock.Players.Count(p => p != null && playerData.ContainsKey(p.Index) && playerData[p.Index].CurrentSpace == space.Name);
                args.Player.SendInfoMessage($"• {space.Name} ({currentPlayers}/{space.MaxPlayers})");
            }
        }

        private void OnGameUpdate(EventArgs args)
        {
            foreach (var player in TShock.Players.Where(p => p != null && p.Active && p.IsLoggedIn))
            {
                if (!playerData.ContainsKey(player.Index)) continue;
                
                foreach (var area in config.RewardAreas)
                {
                    float dist = Vector2.Distance(new Vector2(player.TileX, player.TileY), new Vector2(area.X, area.Y));
                    if (dist <= area.Radius)
                    {
                        if (!area.LastRewardTimes.ContainsKey(player.Index) || 
                            (DateTime.Now - area.LastRewardTimes[player.Index]).TotalSeconds >= area.CooldownSeconds)
                        {
                            area.LastRewardTimes[player.Index] = DateTime.Now;
                            
                            // Даем золотые монеты напрямую
                            player.GiveItem(73, area.MoneyReward); // Gold Coin
                            player.SendSuccessMessage($"[MiniCore] Вы получили награду {area.MoneyReward} золота в зоне {area.Name}!");
                        }
                    }
                }
            }
        }

        private void AddRewardArea(CommandArgs args)
        {
            if (args.Parameters.Count < 4)
            {
                args.Player.SendErrorMessage("Использование: /ara <название> <радиус> <награда> <кулдаун>");
                return;
            }

            try
            {
                var area = new RewardArea
                {
                    Name = args.Parameters[0],
                    X = args.Player.TileX,
                    Y = args.Player.TileY,
                    Radius = int.Parse(args.Parameters[1]),
                    MoneyReward = int.Parse(args.Parameters[2]),
                    CooldownSeconds = int.Parse(args.Parameters[3]),
                    LastRewardTimes = new Dictionary<int, DateTime>()
                };

                config.RewardAreas.Add(area);
                
                if (database != null)
                {
                    string lastRewardJson = JsonConvert.SerializeObject(area.LastRewardTimes);
                    
                    database.Query(@"INSERT INTO MiniCore_RewardAreas 
                        (Name, X, Y, Radius, MoneyReward, CooldownSeconds, LastRewardTimes) 
                        VALUES (@0, @1, @2, @3, @4, @5, @6)",
                        area.Name, area.X, area.Y, area.Radius, area.MoneyReward, area.CooldownSeconds, lastRewardJson);
                }
                
                args.Player.SendSuccessMessage($"Зона наград {area.Name} создана!");
            }
            catch (Exception ex)
            {
                args.Player.SendErrorMessage($"Ошибка: {ex.Message}");
            }
        }

        private void OnChat(ServerChatEventArgs args)
        {
            if (args.Handled) return;
            
            var player = TShock.Players[args.Who];
            if (player == null || !playerData.ContainsKey(player.Index))
                return;

            var pData = playerData[player.Index];
            if (!string.IsNullOrEmpty(pData.CurrentSpace))
            {
                args.Handled = true;
                BroadcastToSpace(pData.CurrentSpace, $"[{pData.CurrentSpace}] {player.Name}: {args.Text}", Color.White);
            }
        }

        private void OnLeave(LeaveEventArgs args)
        {
            var player = TShock.Players[args.Who];
            if (player != null && playerData.ContainsKey(args.Who))
            {
                ForceLeaveSpace(player);
                playerData.Remove(args.Who);
            }
        }

        private void OnPlayerSpawn(GetDataHandlers.SpawnEventArgs args)
        {
            if (args.Player == null)
                return;

            var player = TShock.Players[args.Player.Index];
            if (player == null)
                return;

            if (playerData.ContainsKey(player.Index))
            {
                var pData = playerData[player.Index];
                
                if (pData.AwaitingDeadRespawnSet)
                {
                    var space = config.Spaces.FirstOrDefault(r => r.Name.Equals(playerData[player.Index].CurrentSpace, StringComparison.OrdinalIgnoreCase));
                    if (space != null)
                    {
                        space.DeadSpawnX = player.TileX;
                        space.DeadSpawnY = player.TileY;
                        SaveConfig();
                        player.SendSuccessMessage($"Точка респавна после смерти установлена для простора '{space.Name}'!");
                    }
                    pData.AwaitingDeadRespawnSet = false;
                }
                
                if (!string.IsNullOrEmpty(pData.CurrentSpace))
                {
                    var space = config.Spaces.FirstOrDefault(s => s.Name.Equals(pData.CurrentSpace, StringComparison.OrdinalIgnoreCase));
                    if (space != null)
                    {
                        player.Teleport(space.SpawnX * 16, space.SpawnY * 16);
                    }
                }
            }
        }

        private void OnSignInteract(GetDataHandlers.SignEventArgs args)
        {
            var player = TShock.Players[args.Player.Index];
            if (player == null)
                return;

            if (!playerData.ContainsKey(player.Index))
            {
                playerData[player.Index] = new PlayerData();
            }

            var pData = playerData[player.Index];
            
            if (pData.AwaitingSignUnlink)
            {
                var space = config.Spaces.FirstOrDefault(s => s.LinkedSigns.Any(sl => sl.X == args.X && sl.Y == args.Y));
                if (space != null)
                {
                    space.LinkedSigns.RemoveAll(s => s.X == args.X && s.Y == args.Y);
                    SaveConfig();
                    
                    try
                    {
                        database.Query("DELETE FROM MiniCore_Signs WHERE X = @0 AND Y = @1", args.X, args.Y);
                    }
                    catch (Exception ex)
                    {
                        TShock.Log.ConsoleError($"[MiniCore] Ошибка удаления таблички из БД: {ex.Message}");
                    }
                    
                    player.SendSuccessMessage($"Табличка [{args.X}, {args.Y}] отвязана от Простора!");
                }
                pData.AwaitingSignUnlink = false;
                args.Handled = true;
                return;
            }
            
            if (!string.IsNullOrEmpty(pData.AwaitingSignLink))
            {
                var space = config.Spaces.FirstOrDefault(r => r.Name.Equals(pData.AwaitingSignLink, StringComparison.OrdinalIgnoreCase));
                if (space != null)
                {
                    var signLoc = new SignLocation { X = args.X, Y = args.Y };
                    if (!space.LinkedSigns.Any(s => s.X == signLoc.X && s.Y == signLoc.Y))
                    {
                        space.LinkedSigns.Add(signLoc);
                        
                        try
                        {
                            database.Query("INSERT INTO MiniCore_Signs (SpaceName, X, Y) VALUES (@0, @1, @2)",
                                space.Name, args.X, args.Y);
                        }
                        catch (Exception ex)
                        {
                            TShock.Log.ConsoleError($"[MiniCore] Ошибка сохранения таблички в БД: {ex.Message}");
                        }
                        
                        SaveConfig();
                        UpdateSignText(args.X, args.Y, space.Name);
                        
                        player.SendSuccessMessage($"Табличка [{args.X}, {args.Y}] привязана к Простору '{space.Name}'!");
                    }
                    else
                    {
                        player.SendErrorMessage("Эта табличка уже привязана к этому Простору!");
                    }
                }
                pData.AwaitingSignLink = null;
                args.Handled = true;
                return;
            }

            foreach (var space in config.Spaces)
            {
                if (space.LinkedSigns.Any(s => s.X == args.X && s.Y == args.Y))
                {
                    args.Handled = true;
                    
                    var now = DateTime.Now;
                    bool isDoubleClick = pData.LastSignClick != null && 
                                        pData.LastSignClick.X == args.X && 
                                        pData.LastSignClick.Y == args.Y && 
                                        (now - pData.LastSignClickTime).TotalSeconds < 3;
                    
                    pData.LastSignClick = new SignLocation { X = args.X, Y = args.Y };
                    pData.LastSignClickTime = now;
                    
                    if (isDoubleClick)
                    {
                        JoinSpaceInternal(player, space.Name);
                    }
                    else
                    {
                        int currentPlayers = TShock.Players.Count(p => p != null && playerData.ContainsKey(p.Index) && playerData[p.Index].CurrentSpace == space.Name);
                        player.SendInfoMessage($"[Простор: {space.Name}]");
                        player.SendInfoMessage($"Игроков: {currentPlayers}/{space.MaxPlayers}");
                        player.SendInfoMessage("Кликните снова для входа!");
                    }
                    return;
                }
            }
        }
        
        private void UpdateSignText(int x, int y, string spaceName)
        {
            int signIndex = Sign.ReadSign(x, y);
            if (signIndex >= 0 && signIndex < Main.sign.Length)
            {
                if (Main.sign[signIndex] == null)
                {
                    Main.sign[signIndex] = new Sign();
                    Main.sign[signIndex].x = x;
                    Main.sign[signIndex].y = y;
                }
                
                Main.sign[signIndex].text = $"[Простор]\n{spaceName}\n \nКликните дважды";
                
                NetMessage.SendData((int)PacketTypes.SignNew, -1, -1, Terraria.Localization.NetworkText.FromLiteral(Main.sign[signIndex].text), signIndex, x, y);
            }
        }

        private void BroadcastToSpace(string spaceName, string message, Color color, int excludePlayer = -1)
        {
            foreach (var player in TShock.Players.Where(p => p != null && p.Index != excludePlayer))
            {
                if (playerData.ContainsKey(player.Index) && playerData[player.Index].CurrentSpace == spaceName)
                {
                    player.SendMessage(message, color);
                }
            }
        }

        private InventoryData SavePlayerInventory(TSPlayer player)
        {
            var inv = new InventoryData
            {
                Items = new List<ItemData>(),
                Armor = new List<ItemData>(),
                Dyes = new List<ItemData>(),
                MiscEquips = new List<ItemData>(),
                MiscDyes = new List<ItemData>(),
                StatLife = player.TPlayer.statLife,
                StatLifeMax = player.TPlayer.statLifeMax,
                StatMana = player.TPlayer.statMana,
                StatManaMax = player.TPlayer.statManaMax
            };

            for (int i = 0; i < 59; i++)
            {
                var slot = player.TPlayer.inventory[i];
                inv.Items.Add(new ItemData { 
                    NetID = slot?.netID ?? 0, 
                    Stack = slot?.stack ?? 0, 
                    Prefix = slot?.prefix ?? 0 
                });
            }
            
            for (int i = 0; i < 20; i++)
            {
                var slot = player.TPlayer.armor[i];
                inv.Armor.Add(new ItemData { 
                    NetID = slot?.netID ?? 0, 
                    Stack = slot?.stack ?? 0, 
                    Prefix = slot?.prefix ?? 0 
                });
            }
            
            for (int i = 0; i < 10; i++)
            {
                var slot = player.TPlayer.dye[i];
                inv.Dyes.Add(new ItemData { 
                    NetID = slot?.netID ?? 0, 
                    Stack = slot?.stack ?? 0, 
                    Prefix = slot?.prefix ?? 0 
                });
            }
            
            for (int i = 0; i < 5; i++)
            {
                var slot = player.TPlayer.miscEquips[i];
                inv.MiscEquips.Add(new ItemData { 
                    NetID = slot?.netID ?? 0, 
                    Stack = slot?.stack ?? 0, 
                    Prefix = slot?.prefix ?? 0 
                });
            }
            
            for (int i = 0; i < 5; i++)
            {
                var slot = player.TPlayer.miscDyes[i];
                inv.MiscDyes.Add(new ItemData { 
                    NetID = slot?.netID ?? 0, 
                    Stack = slot?.stack ?? 0, 
                    Prefix = slot?.prefix ?? 0 
                });
            }

            return inv;
        }

        private void SendWorldInfo(TSPlayer player)
        {
            if (Main.ServerSideCharacter)
                return;

            using (var s = new MemoryStream())
            {
                using (var w = new BinaryWriter(s))
                {
                    w.BaseStream.Position = 2;

                    w.Write((byte)PacketTypes.WorldInfo);
                    w.Write((int)Main.time);
                    BitsByte bb1 = 0;
                    {
                        bb1[0] = Main.dayTime;
                        bb1[1] = Main.bloodMoon;
                        bb1[2] = Main.eclipse;
                    }
                    w.Write(bb1);
                    w.Write((byte)Main.moonPhase);
                    w.Write((short)Main.maxTilesX);
                    w.Write((short)Main.maxTilesY);
                    w.Write((short)Main.spawnTileX);
                    w.Write((short)Main.spawnTileY);
                    w.Write((short)Main.worldSurface);
                    w.Write((short)Main.rockLayer);
                    w.Write(Main.worldID);
                    w.Write(Main.worldName);
                    w.Write((byte)Main.GameMode);
                    w.Write(Main.ActiveWorldFileData.UniqueId.ToByteArray());
                    w.Write(Main.ActiveWorldFileData.WorldGeneratorVersion);
                    w.Write((byte)Main.moonType);
                    w.Write((byte)WorldGen.treeBG1);
                    w.Write((byte)WorldGen.treeBG2);
                    w.Write((byte)WorldGen.treeBG3);
                    w.Write((byte)WorldGen.treeBG4);
                    w.Write((byte)WorldGen.corruptBG);
                    w.Write((byte)WorldGen.jungleBG);
                    w.Write((byte)WorldGen.snowBG);
                    w.Write((byte)WorldGen.hallowBG);
                    w.Write((byte)WorldGen.crimsonBG);
                    w.Write((byte)WorldGen.desertBG);
                    w.Write((byte)WorldGen.oceanBG);
                    w.Write((byte)WorldGen.mushroomBG);
                    w.Write((byte)WorldGen.underworldBG);
                    w.Write((byte)Main.iceBackStyle);
                    w.Write((byte)Main.jungleBackStyle);
                    w.Write((byte)Main.hellBackStyle);
                    w.Write(Main.windSpeedTarget);
                    w.Write((byte)Main.numClouds);
                    for (int k = 0; k < 3; k++)
                    {
                        w.Write(Main.treeX[k]);
                    }
                    for (int l = 0; l < 4; l++)
                    {
                        w.Write((byte)Main.treeStyle[l]);
                    }
                    for (int m = 0; m < 3; m++)
                    {
                        w.Write(Main.caveBackX[m]);
                    }
                    for (int n = 0; n < 4; n++)
                    {
                        w.Write((byte)Main.caveBackStyle[n]);
                    }
                    WorldGen.TreeTops.SyncSend(w);
                    w.Write(Main.maxRaining);
                    BitsByte bb2 = 0;
                    {
                        bb2[0] = WorldGen.shadowOrbSmashed;
                        bb2[1] = NPC.downedBoss1;
                        bb2[2] = NPC.downedBoss2;
                        bb2[3] = NPC.downedBoss3;
                        bb2[4] = Main.hardMode;
                        bb2[5] = NPC.downedClown;
                        bb2[6] = true;
                        bb2[7] = NPC.downedPlantBoss;
                    }
                    w.Write(bb2);
                    BitsByte bb3 = 0;
                    {
                        bb3[0] = NPC.downedMechBoss1;
                        bb3[1] = NPC.downedMechBoss2;
                        bb3[2] = NPC.downedMechBoss3;
                        bb3[3] = NPC.downedMechBossAny;
                        bb3[4] = Main.cloudBGActive >= 1f;
                        bb3[5] = WorldGen.crimson;
                        bb3[6] = Main.pumpkinMoon;
                        bb3[7] = Main.snowMoon;
                    }
                    w.Write(bb3);
                    BitsByte bb4 = 0;
                    {
                        bb4[1] = Main.fastForwardTimeToDawn;
                        bb4[2] = Main.slimeRain;
                        bb4[3] = NPC.downedSlimeKing;
                        bb4[4] = NPC.downedQueenBee;
                        bb4[5] = NPC.downedFishron;
                        bb4[6] = NPC.downedMartians;
                        bb4[7] = NPC.downedAncientCultist;
                    }
                    w.Write(bb4);
                    BitsByte bb5 = 0;
                    {
                        bb5[0] = NPC.downedMoonlord;
                        bb5[1] = NPC.downedHalloweenKing;
                        bb5[2] = NPC.downedHalloweenTree;
                        bb5[3] = NPC.downedChristmasIceQueen;
                        bb5[4] = NPC.downedChristmasSantank;
                        bb5[5] = NPC.downedChristmasTree;
                        bb5[6] = NPC.downedGolemBoss;
                        bb5[7] = BirthdayParty.PartyIsUp;
                    }
                    w.Write(bb5);
                    BitsByte bb6 = 0;
                    {
                        bb6[0] = NPC.downedPirates;
                        bb6[1] = NPC.downedFrost;
                        bb6[2] = NPC.downedGoblins;
                        bb6[3] = Sandstorm.Happening;
                        bb6[4] = DD2Event.Ongoing;
                        bb6[5] = DD2Event.DownedInvasionT1;
                        bb6[6] = DD2Event.DownedInvasionT2;
                        bb6[7] = DD2Event.DownedInvasionT3;
                    }
                    w.Write(bb6);
                    BitsByte bb7 = 0;
                    {
                        bb7[0] = NPC.combatBookWasUsed;
                        bb7[1] = LanternNight.LanternsUp;
                        bb7[2] = NPC.downedTowerSolar;
                        bb7[3] = NPC.downedTowerVortex;
                        bb7[4] = NPC.downedTowerNebula;
                        bb7[5] = NPC.downedTowerStardust;
                        bb7[6] = Main.forceHalloweenForToday;
                        bb7[7] = Main.forceXMasForToday;
                    }
                    w.Write(bb7);
                    BitsByte bb8 = 0;
                    {
                        bb8[0] = NPC.boughtCat;
                        bb8[1] = NPC.boughtDog;
                        bb8[2] = NPC.boughtBunny;
                        bb8[3] = NPC.freeCake;
                        bb8[4] = Main.drunkWorld;
                        bb8[5] = NPC.downedEmpressOfLight;
                        bb8[6] = NPC.downedQueenSlime;
                        bb8[7] = Main.getGoodWorld;
                    }
                    w.Write(bb8);
                    BitsByte bb9 = 0;
                    {
                        bb9[0] = Main.tenthAnniversaryWorld;
                        bb9[1] = Main.dontStarveWorld;
                        bb9[2] = NPC.downedDeerclops;
                        bb9[3] = Main.notTheBeesWorld;
                        bb9[4] = Main.remixWorld;
                        bb9[5] = NPC.unlockedSlimeBlueSpawn;
                        bb9[6] = NPC.combatBookVolumeTwoWasUsed;
                        bb9[7] = NPC.peddlersSatchelWasUsed;
                    }
                    w.Write(bb9);
                    BitsByte bb10 = 0;
                    {
                        bb10[0] = NPC.unlockedSlimeGreenSpawn;
                        bb10[1] = NPC.unlockedSlimeOldSpawn;
                        bb10[2] = NPC.unlockedSlimePurpleSpawn;
                        bb10[3] = NPC.unlockedSlimeRainbowSpawn;
                        bb10[4] = NPC.unlockedSlimeRedSpawn;
                        bb10[5] = NPC.unlockedSlimeYellowSpawn;
                        bb10[6] = NPC.unlockedSlimeCopperSpawn;
                        bb10[7] = Main.fastForwardTimeToDusk;
                    }
                    w.Write(bb10);
                    BitsByte bb11 = 0;
                    {
                        bb11[0] = Main.noTrapsWorld;
                        bb11[1] = Main.zenithWorld;
                    }
                    w.Write(bb11);
                    w.Write((byte)Main.sundialCooldown);
                    w.Write((byte)Main.moondialCooldown);
                    w.Write((short)WorldGen.SavedOreTiers.Copper);
                    w.Write((short)WorldGen.SavedOreTiers.Iron);
                    w.Write((short)WorldGen.SavedOreTiers.Silver);
                    w.Write((short)WorldGen.SavedOreTiers.Gold);
                    w.Write((short)WorldGen.SavedOreTiers.Cobalt);
                    w.Write((short)WorldGen.SavedOreTiers.Mythril);
                    w.Write((short)WorldGen.SavedOreTiers.Adamantite);
                    w.Write((sbyte)Main.invasionType);
                    if (SocialAPI.Network != null)
                    {
                        w.Write(SocialAPI.Network.GetLobbyId());
                    }
                    else
                    {
                        w.Write(0UL);
                    }
                    w.Write(Sandstorm.IntendedSeverity);

                    ushort Length = (ushort)w.BaseStream.Position;
                    w.BaseStream.Position = 0;
                    w.Write(Length);
                }
                player.SendRawData(s.ToArray());
            }
        }

        private void ApplyItemParameter(Item item, string paramName, string paramValue)
        {
            if (item == null || string.IsNullOrEmpty(paramName) || string.IsNullOrEmpty(paramValue)) return;
            
            switch (paramName.ToLower())
            {
                case "damage":
                    if (float.TryParse(paramValue, out float dmgMult))
                    {
                        item.damage = (int)(item.damage * dmgMult);
                    }
                    break;
                case "knockback":
                    if (float.TryParse(paramValue, out float knockMult))
                    {
                        item.knockBack = item.knockBack * knockMult;
                    }
                    break;
                case "crit":
                    if (float.TryParse(paramValue, out float critChance))
                    {
                        item.crit = (int)(item.crit + critChance);
                    }
                    break;
                case "mana":
                    if (float.TryParse(paramValue, out float manaCost))
                    {
                        item.mana = (int)(item.mana * manaCost);
                    }
                    break;
                case "color":
                    if (paramValue.Length == 6)
                    {
                        try
                        {
                            byte r = byte.Parse(paramValue.Substring(0, 2), System.Globalization.NumberStyles.HexNumber);
                            byte g = byte.Parse(paramValue.Substring(2, 2), System.Globalization.NumberStyles.HexNumber);
                            byte b = byte.Parse(paramValue.Substring(4, 2), System.Globalization.NumberStyles.HexNumber);
                            // Item color application logic here
                        }
                        catch { }
                    }
                    break;
                case "speed":
                    if (float.TryParse(paramValue, out float speedMult))
                    {
                        item.shootSpeed *= speedMult;
                    }
                    break;
                case "projectile":
                    if (int.TryParse(paramValue, out int projID))
                    {
                        item.shoot = projID;
                    }
                    break;
                case "useatime":
                case "attacktime":
                    if (float.TryParse(paramValue, out float timeMult))
                    {
                        item.useTime = (int)(item.useTime * timeMult);
                        item.useAnimation = (int)(item.useAnimation * timeMult);
                    }
                    break;
            }
        }

        private void SendItemParameterPacket(TSPlayer player, int slot, Item item, string paramName, string paramValue)
        {
            // 1. Сначала синхронизируем сам слот (Пакет 5), чтобы клиент знал, что предмет там есть
            player.SendData(PacketTypes.PlayerSlot, "", player.Index, slot, item.prefix);

            // 2. Затем отправляем кастомные данные через 0x11 (17) хак
            using (var s = new MemoryStream())
            {
                using (var w = new BinaryWriter(s))
                {
                    w.Write((short)0);
                    w.Write((byte)17);
                    w.Write((byte)255); 
                    w.Write((short)item.netID);
                    
                    byte[] paramNameBytes = System.Text.Encoding.UTF8.GetBytes(paramName);
                    byte[] paramValueBytes = System.Text.Encoding.UTF8.GetBytes(paramValue);
                    
                    w.Write((byte)paramNameBytes.Length);
                    w.Write(paramNameBytes);
                    w.Write((byte)paramValueBytes.Length);
                    w.Write(paramValueBytes);

                    long endPos = s.Position;
                    s.Position = 0;
                    w.Write((short)endPos);
                    s.Position = endPos;

                    player.SendRawData(s.ToArray());
                }
            }

            // 3. Дополнительно отправляем UpdateItemStats (Пакет 102) для обновления тултипов и статов на клиенте
            player.SendData(PacketTypes.UpdateItemStats, "", item.netID, item.prefix);
        }

        private void RestorePlayerInventoryWithSync(TSPlayer player, InventoryData inv, Space space = null)
        {
            SendWorldInfo(player);
            
            for (int i = 0; i < 59 && i < inv.Items.Count; i++)
            {
                var item = inv.Items[i];
                player.TPlayer.inventory[i].SetDefaults(item.NetID);
                player.TPlayer.inventory[i].stack = item.Stack;
                player.TPlayer.inventory[i].prefix = item.Prefix;
                NetMessage.SendData((int)PacketTypes.PlayerSlot, -1, -1, null, player.Index, i, player.TPlayer.inventory[i].prefix);
                
                if (space != null)
                {
                    var itemParams = space.ItemParameters.Where(p => p.ItemNetID == item.NetID).ToList();
                    foreach (var param in itemParams)
                    {
                        ApplyItemParameter(player.TPlayer.inventory[i], param.ParameterName, param.ParameterValue);
                        SendItemParameterPacket(player, i, player.TPlayer.inventory[i], param.ParameterName, param.ParameterValue);
                    }
                }
            }

            for (int i = 0; i < 20 && i < inv.Armor.Count; i++)
            {
                var item = inv.Armor[i];
                player.TPlayer.armor[i].SetDefaults(item.NetID);
                player.TPlayer.armor[i].stack = item.Stack;
                player.TPlayer.armor[i].prefix = item.Prefix;
                NetMessage.SendData((int)PacketTypes.PlayerSlot, -1, -1, null, player.Index, 59 + i, player.TPlayer.armor[i].prefix);
            }

            for (int i = 0; i < 10 && i < inv.Dyes.Count; i++)
            {
                var item = inv.Dyes[i];
                player.TPlayer.dye[i].SetDefaults(item.NetID);
                player.TPlayer.dye[i].stack = item.Stack;
                player.TPlayer.dye[i].prefix = item.Prefix;
                NetMessage.SendData((int)PacketTypes.PlayerSlot, -1, -1, null, player.Index, 79 + i, player.TPlayer.dye[i].prefix);
            }

            for (int i = 0; i < 5 && i < inv.MiscEquips.Count; i++)
            {
                var item = inv.MiscEquips[i];
                player.TPlayer.miscEquips[i].SetDefaults(item.NetID);
                player.TPlayer.miscEquips[i].stack = item.Stack;
                player.TPlayer.miscEquips[i].prefix = item.Prefix;
                NetMessage.SendData((int)PacketTypes.PlayerSlot, -1, -1, null, player.Index, 89 + i, player.TPlayer.miscEquips[i].prefix);
            }

            for (int i = 0; i < 5 && i < inv.MiscDyes.Count; i++)
            {
                var item = inv.MiscDyes[i];
                player.TPlayer.miscDyes[i].SetDefaults(item.NetID);
                player.TPlayer.miscDyes[i].stack = item.Stack;
                player.TPlayer.miscDyes[i].prefix = item.Prefix;
                NetMessage.SendData((int)PacketTypes.PlayerSlot, -1, -1, null, player.Index, 94 + i, player.TPlayer.miscDyes[i].prefix);
            }

            player.TPlayer.statLife = inv.StatLife;
            player.TPlayer.statLifeMax = inv.StatLifeMax;
            player.TPlayer.statMana = inv.StatMana;
            player.TPlayer.statManaMax = inv.StatManaMax;

            NetMessage.SendData((int)PacketTypes.PlayerHp, -1, -1, null, player.Index);
            NetMessage.SendData((int)PacketTypes.PlayerMana, -1, -1, null, player.Index);
        }

        private void ClearPlayerInventoryWithSync(TSPlayer player)
        {
            SendWorldInfo(player);
            
            for (int i = 0; i < NetItem.MaxInventory; i++)
            {
                if (i < 59)
                {
                    player.TPlayer.inventory[i].SetDefaults(0);
                    player.TPlayer.inventory[i].stack = 0;
                    NetMessage.SendData((int)PacketTypes.PlayerSlot, -1, -1, null, player.Index, i);
                }
                else if (i < 79)
                {
                    player.TPlayer.armor[i - 59].SetDefaults(0);
                    player.TPlayer.armor[i - 59].stack = 0;
                    NetMessage.SendData((int)PacketTypes.PlayerSlot, -1, -1, null, player.Index, i);
                }
                else if (i < 89)
                {
                    player.TPlayer.dye[i - 79].SetDefaults(0);
                    player.TPlayer.dye[i - 79].stack = 0;
                    NetMessage.SendData((int)PacketTypes.PlayerSlot, -1, -1, null, player.Index, i);
                }
                else if (i < 94)
                {
                    player.TPlayer.miscEquips[i - 89].SetDefaults(0);
                    player.TPlayer.miscEquips[i - 89].stack = 0;
                    NetMessage.SendData((int)PacketTypes.PlayerSlot, -1, -1, null, player.Index, i);
                }
                else if (i < 99)
                {
                    player.TPlayer.miscDyes[i - 94].SetDefaults(0);
                    player.TPlayer.miscDyes[i - 94].stack = 0;
                    NetMessage.SendData((int)PacketTypes.PlayerSlot, -1, -1, null, player.Index, i);
                }
            }
            
            player.TPlayer.statLife = 100;
            player.TPlayer.statLifeMax = 100;
            player.TPlayer.statMana = 20;
            player.TPlayer.statManaMax = 20;
            
            NetMessage.SendData((int)PacketTypes.PlayerHp, -1, -1, null, player.Index);
            NetMessage.SendData((int)PacketTypes.PlayerMana, -1, -1, null, player.Index);
        }

        private void LoadSpaceInventoriesWithSync(TSPlayer player, List<string> inventoryNames, Space space)
        {
            if (inventoryNames == null || inventoryNames.Count == 0)
            {
                ClearPlayerInventoryWithSync(player);
                return;
            }

            if (!playerData.ContainsKey(player.Index))
                playerData[player.Index] = new PlayerData();

            var pData = playerData[player.Index];
            
            // If player selected inventory, use it
            string selectedInventory = null;
            
            // Check if selected inventory is available in this space
            if (!string.IsNullOrEmpty(pData.SelectedInventory) && 
                inventoryNames.Any(i => i.Equals(pData.SelectedInventory, StringComparison.OrdinalIgnoreCase)))
            {
                selectedInventory = pData.SelectedInventory;
            }
            
            // Limited inventory logic (Murder mode)
            if (space.LimitedInvEnabled && space.LimitedInvCount > 0 && !string.IsNullOrEmpty(space.LimitedInvName))
            {
                int limitedInvPlayers = TShock.Players.Count(p => p != null && playerData.ContainsKey(p.Index) && 
                    playerData[p.Index].CurrentSpace == space.Name && playerData[p.Index].HasLimitedInventory);
                
                if (limitedInvPlayers < space.LimitedInvCount)
                {
                    selectedInventory = space.LimitedInvName;
                    pData.HasLimitedInventory = true;
                }
                else
                {
                    pData.HasLimitedInventory = false;
                }
            }
            
            // If no inventory selected, choose random
            if (string.IsNullOrEmpty(selectedInventory))
            {
                selectedInventory = inventoryNames[random.Next(inventoryNames.Count)];
            }
            
            if (config.SpaceInventories.ContainsKey(selectedInventory))
            {
                RestorePlayerInventoryWithSync(player, config.SpaceInventories[selectedInventory], space);
            }
            else
            {
                ClearPlayerInventoryWithSync(player);
            }
        }

        private void LoadConfig()
        {
            if (File.Exists(ConfigPath))
            {
                try
                {
                    config = JsonConvert.DeserializeObject<Config>(File.ReadAllText(ConfigPath)) ?? new Config();
                }
                catch (Exception ex)
                {
                    TShock.Log.ConsoleError($"[MiniCore] Ошибка загрузки конфига: {ex.Message}");
                    config = new Config();
                }
            }
            else
            {
                config = new Config();
                SaveConfig();
            }
        }

        private void SaveConfig()
        {
            try
            {
                File.WriteAllText(ConfigPath, JsonConvert.SerializeObject(config, Formatting.Indented));
            }
            catch (Exception ex)
            {
                TShock.Log.ConsoleError($"[MiniCore] Ошибка сохранения конфига: {ex.Message}");
            }
        }
    }

    public class Config
    {
        public List<Space> Spaces { get; set; } = new List<Space>();
        public int LobbySpawnX { get; set; } = 0;
        public int LobbySpawnY { get; set; } = 0;
        public Dictionary<string, InventoryData> SpaceInventories { get; set; } = new Dictionary<string, InventoryData>();
        public List<RewardArea> RewardAreas { get; set; } = new List<RewardArea>();
    }

    public class RewardArea
    {
        public string Name { get; set; } = "";
        public int X { get; set; }
        public int Y { get; set; }
        public int Radius { get; set; }
        public int MoneyReward { get; set; }
        public int CooldownSeconds { get; set; }
        public Dictionary<int, DateTime> LastRewardTimes { get; set; } = new Dictionary<int, DateTime>();
    }

    public class Space
    {
        public string Name { get; set; } = "";
        public int MaxPlayers { get; set; } = 10;
        public int SpawnX { get; set; }
        public int SpawnY { get; set; }
        public List<string> InventoryNames { get; set; } = new List<string>();
        public List<SignLocation> LinkedSigns { get; set; } = new List<SignLocation>();
        
        // Limited inventory system (Murder)
        public bool LimitedInvEnabled { get; set; } = false;
        public int LimitedInvCount { get; set; } = 1;
        public string LimitedInvName { get; set; } = "";
        
        // Death mechanics
        public string DeadInventoryName { get; set; } = "";
        public int DeadSpawnX { get; set; } = 0;
        public int DeadSpawnY { get; set; } = 0;
        
        // Buffs and timers
        public int BuffID { get; set; } = 0;
        public int TimerStartSeconds { get; set; } = 0;
        
        // Random spawn mechanics (regenerate every 10 minutes)
        public long LastRandomSpawnTime { get; set; } = 0;
        public int RandomSpawnX { get; set; } = 0;
        public int RandomSpawnY { get; set; } = 0;
        
        // Custom item parameters for this space
        public List<ItemParameter> ItemParameters { get; set; } = new List<ItemParameter>();
    }

    public class SignLocation
    {
        public int X { get; set; }
        public int Y { get; set; }
    }

    public class PlayerData
    {
        public string CurrentSpace { get; set; }
        public InventoryData LobbyInventory { get; set; }
        public string AwaitingSignLink { get; set; }
        public bool AwaitingSignUnlink { get; set; }
        public bool OriginalPvPState { get; set; }
        public DateTime LastSignClickTime { get; set; }
        public SignLocation LastSignClick { get; set; }
        
        // Limited inventory tracking
        public bool HasLimitedInventory { get; set; } = false;
        public bool AwaitingDeadRespawnSet { get; set; } = false;
        
        // Inventory selection
        public string SelectedInventory { get; set; }
    }

    public class InventoryData
    {
        public List<ItemData> Items { get; set; } = new List<ItemData>();
        public List<ItemData> Armor { get; set; } = new List<ItemData>();
        public List<ItemData> Dyes { get; set; } = new List<ItemData>();
        public List<ItemData> MiscEquips { get; set; } = new List<ItemData>();
        public List<ItemData> MiscDyes { get; set; } = new List<ItemData>();
        public int StatLife { get; set; } = 100;
        public int StatLifeMax { get; set; } = 100;
        public int StatMana { get; set; } = 20;
        public int StatManaMax { get; set; } = 20;
    }

    public class ItemData
    {
        public int NetID { get; set; }
        public int Stack { get; set; }
        public byte Prefix { get; set; }
        public Dictionary<string, string> CustomParams { get; set; } = new Dictionary<string, string>();
    }

    public class ItemParameter
    {
        public int ItemNetID { get; set; }
        public string ParameterName { get; set; } = "";
        public string ParameterValue { get; set; } = "";
    }

    public class SpaceItemParams
    {
        public string SpaceName { get; set; } = "";
        public List<ItemParameter> Parameters { get; set; } = new List<ItemParameter>();
    }
}
