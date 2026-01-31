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
        public override Version Version => new Version(1, 3, 1);
        public override string Author => "Archiepescop";
        public override string Description => "Ядро для создания мини-игр в Terraria";

        private static readonly string ConfigPath = Path.Combine(TShock.SavePath, "MiniCore.json");
        private Config config = new Config();
        private Dictionary<int, PlayerData> playerData = new Dictionary<int, PlayerData>();
        private IDbConnection? database;
        private Random random = new Random();

        public MiniCore(Main game) : base(game)
        {
        }

        public override void Initialize()
        {
            LoadConfig();
            InitializeDatabase();
            
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
            GetDataHandlers.PlayerSpawn += OnPlayerSpawn;
            GetDataHandlers.SignRead += OnSignInteract;
            
            Console.WriteLine("[MiniCore] Плагин успешно загружен! Версия 1.3.1");
        }

        private void OnItemDrop(object? sender, GetDataHandlers.ItemDropEventArgs e)
        {
            if (e.Handled) return;

            var player = TShock.Players[e.Player.Index];
            if (player == null || !playerData.ContainsKey(player.Index)) return;

            var pData = playerData[player.Index];
            if (string.IsNullOrEmpty(pData.CurrentSpace)) return;

            var space = config.Spaces.FirstOrDefault(s => s.Name == pData.CurrentSpace);
            if (space == null) return;

            var item = Main.item[e.ID];
            if (item == null || !item.active) return;

            var parameters = space.ItemParameters.Where(p => p.ItemNetID == item.netID).ToList();
            if (parameters.Count > 0)
            {
                foreach (var param in parameters)
                {
                    ApplyItemParameter(item, param.ParameterName, param.ParameterValue);
                }

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
                if (database != null && database.State == ConnectionState.Open)
                {
                    database.Close();
                }
                database = null;
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
                GetDataHandlers.ItemDrop -= OnItemDrop;
                ServerApi.Hooks.ServerChat.Deregister(this, OnChat);
                ServerApi.Hooks.GameUpdate.Deregister(this, OnGameUpdate);
                ServerApi.Hooks.ServerLeave.Deregister(this, OnLeave);
                GetDataHandlers.PlayerSpawn -= OnPlayerSpawn;
                GetDataHandlers.SignRead -= OnSignInteract;
                SaveConfig();
                
                if (database != null && database.State == ConnectionState.Open)
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
                database?.Query("DELETE FROM MiniCore_Signs WHERE SpaceName = @0", spaceName);
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

            var invToRemove = space.InventoryNames.FirstOrDefault(i => i.Equals(invName, StringComparison.OrdinalIgnoreCase));
            if (invToRemove == null)
            {
                args.Player.SendErrorMessage($"Инвентарь '{invName}' не привязан к этому простору!");
                return;
            }

            space.InventoryNames.Remove(invToRemove);
            SaveConfig();
            
            args.Player.SendSuccessMessage($"Инвентарь '{invName}' удалён из Простора '{spaceName}'");
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
                args.Player.SendInfoMessage($"К Простору '{spaceName}' не привязано ни одного инвентаря.");
                return;
            }

            args.Player.SendInfoMessage($"Инвентари Простора '{spaceName}':");
            foreach (var inv in space.InventoryNames)
            {
                args.Player.SendInfoMessage($"  - {inv}");
            }
        }

        private void SetLimitedInventory(CommandArgs args)
        {
            if (args.Parameters.Count < 3)
            {
                args.Player.SendErrorMessage("Использование: /invranlim <простор> <кол-во> <инвентарь>");
                args.Player.SendInfoMessage("Установить 0 для отключения.");
                return;
            }

            string invName = args.Parameters[args.Parameters.Count - 1];
            if (!int.TryParse(args.Parameters[args.Parameters.Count - 2], out int count))
            {
                args.Player.SendErrorMessage("Укажите корректное количество!");
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
                args.Player.SendSuccessMessage($"Лимитированный инвентарь отключён для Простора '{spaceName}'");
                return;
            }

            if (!config.SpaceInventories.ContainsKey(invName))
            {
                args.Player.SendErrorMessage($"Инвентарь '{invName}' не существует!");
                return;
            }

            space.LimitedInvEnabled = true;
            space.LimitedInvCount = count;
            space.LimitedInvName = invName;
            SaveConfig();
            
            args.Player.SendSuccessMessage($"Лимитированный инвентарь '{invName}' установлен для {count} игрока(ов) в Просторе '{spaceName}'");
        }

        private void SetDeadInventory(CommandArgs args)
        {
            if (args.Parameters.Count < 2)
            {
                args.Player.SendErrorMessage("Использование: /setinvdead <простор> <инвентарь>");
                args.Player.SendInfoMessage("Используйте 'none' для отключения.");
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

            if (invName.Equals("none", StringComparison.OrdinalIgnoreCase))
            {
                space.DeadInventoryName = "";
                SaveConfig();
                args.Player.SendSuccessMessage($"Инвентарь при смерти отключён для Простора '{spaceName}'");
                return;
            }

            if (!config.SpaceInventories.ContainsKey(invName))
            {
                args.Player.SendErrorMessage($"Инвентарь '{invName}' не существует!");
                return;
            }

            space.DeadInventoryName = invName;
            SaveConfig();
            
            args.Player.SendSuccessMessage($"Инвентарь при смерти '{invName}' установлен для Простора '{spaceName}'");
        }

        private void SetDeadRespawn(CommandArgs args)
        {
            if (args.Parameters.Count < 1)
            {
                args.Player.SendErrorMessage("Использование: /setrespawndead <простор>");
                return;
            }

            string spaceName = string.Join(" ", args.Parameters);
            var space = config.Spaces.FirstOrDefault(r => r.Name.Equals(spaceName, StringComparison.OrdinalIgnoreCase));
            
            if (space == null)
            {
                args.Player.SendErrorMessage($"Простор '{spaceName}' не найден!");
                return;
            }

            space.DeadSpawnX = args.Player.TileX;
            space.DeadSpawnY = args.Player.TileY;
            SaveConfig();
            
            args.Player.SendSuccessMessage($"Точка спавна при смерти установлена на [{space.DeadSpawnX}, {space.DeadSpawnY}] для Простора '{spaceName}'");
        }

        private void SetBuffSpace(CommandArgs args)
        {
            if (args.Parameters.Count < 2)
            {
                args.Player.SendErrorMessage("Использование: /setbuffspace <простор> <buffid>");
                args.Player.SendInfoMessage("Используйте 0 для отключения баффа.");
                return;
            }

            if (!int.TryParse(args.Parameters[args.Parameters.Count - 1], out int buffId))
            {
                args.Player.SendErrorMessage("Укажите корректный ID баффа!");
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
            {
                args.Player.SendSuccessMessage($"Бафф отключён для Простора '{spaceName}'");
            }
            else
            {
                args.Player.SendSuccessMessage($"Бафф ID {buffId} установлен для Простора '{spaceName}'");
            }
        }

        private void SetTimerStart(CommandArgs args)
        {
            if (args.Parameters.Count < 2)
            {
                args.Player.SendErrorMessage("Использование: /settimerstart <простор> <секунды>");
                return;
            }

            if (!int.TryParse(args.Parameters[args.Parameters.Count - 1], out int seconds) || seconds < 0)
            {
                args.Player.SendErrorMessage("Укажите корректное количество секунд!");
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
            
            args.Player.SendSuccessMessage($"Таймер старта установлен на {seconds} секунд для Простора '{spaceName}'");
        }

        private void PickInventory(CommandArgs args)
        {
            if (args.Parameters.Count < 1)
            {
                args.Player.SendErrorMessage("Использование: /pickinv <инвентарь>");
                return;
            }

            if (!playerData.ContainsKey(args.Player.Index))
            {
                args.Player.SendErrorMessage("Вы не находитесь в простое!");
                return;
            }

            var pData = playerData[args.Player.Index];
            if (string.IsNullOrEmpty(pData.CurrentSpace))
            {
                args.Player.SendErrorMessage("Вы не находитесь в простое!");
                return;
            }

            var space = config.Spaces.FirstOrDefault(s => s.Name.Equals(pData.CurrentSpace, StringComparison.OrdinalIgnoreCase));
            if (space == null)
            {
                args.Player.SendErrorMessage("Ошибка: Простор не найден!");
                return;
            }

            string invName = string.Join(" ", args.Parameters);
            
            if (!space.InventoryNames.Any(i => i.Equals(invName, StringComparison.OrdinalIgnoreCase)))
            {
                args.Player.SendErrorMessage($"Инвентарь '{invName}' недоступен в этом простое!");
                args.Player.SendInfoMessage($"Доступные: {string.Join(", ", space.InventoryNames)}");
                return;
            }

            if (!config.SpaceInventories.ContainsKey(invName))
            {
                args.Player.SendErrorMessage($"Инвентарь '{invName}' не существует!");
                return;
            }

            pData.SelectedInventory = invName;
            RestorePlayerInventoryWithSync(args.Player, config.SpaceInventories[invName], space);
            
            args.Player.SendSuccessMessage($"Инвентарь '{invName}' применён!");
        }

        private void SaveInventoryTemplate(CommandArgs args)
        {
            if (args.Parameters.Count < 1)
            {
                args.Player.SendErrorMessage("Использование: /saveinv <название>");
                return;
            }

            string templateName = string.Join(" ", args.Parameters);
            var inv = SavePlayerInventory(args.Player);
            
            config.SpaceInventories[templateName] = inv;
            SaveConfig();
            SaveInventoryToDatabase(templateName, inv);
            
            args.Player.SendSuccessMessage($"Инвентарь сохранён как шаблон '{templateName}'!");
        }

        private void SaveInventoryToDatabase(string templateName, InventoryData inv)
        {
            if (database == null) return;
            
            try
            {
                string invJson = JsonConvert.SerializeObject(inv);
                
                database.Query("DELETE FROM MiniCore_Inventories WHERE TemplateName = @0", templateName);
                database.Query(@"INSERT INTO MiniCore_Inventories 
                    (TemplateName, InventoryData, StatLife, StatLifeMax, StatMana, StatManaMax, CreatedAt) 
                    VALUES (@0, @1, @2, @3, @4, @5, @6)",
                    templateName, invJson, inv.StatLife, inv.StatLifeMax, inv.StatMana, inv.StatManaMax, DateTime.UtcNow);
            }
            catch (Exception ex)
            {
                TShock.Log.ConsoleError($"[MiniCore] Ошибка сохранения инвентаря в БД: {ex.Message}");
            }
        }

        private void DeleteInventoryTemplate(CommandArgs args)
        {
            if (args.Parameters.Count < 1)
            {
                args.Player.SendErrorMessage("Использование: /delinv <название>");
                return;
            }

            string templateName = string.Join(" ", args.Parameters);
            
            if (!config.SpaceInventories.ContainsKey(templateName))
            {
                args.Player.SendErrorMessage($"Шаблон '{templateName}' не найден!");
                return;
            }

            config.SpaceInventories.Remove(templateName);
            SaveConfig();
            
            try
            {
                database?.Query("DELETE FROM MiniCore_Inventories WHERE TemplateName = @0", templateName);
            }
            catch (Exception ex)
            {
                TShock.Log.ConsoleError($"[MiniCore] Ошибка удаления инвентаря из БД: {ex.Message}");
            }
            
            args.Player.SendSuccessMessage($"Шаблон инвентаря '{templateName}' удалён!");
        }

        private void ListInventoryTemplates(CommandArgs args)
        {
            if (config.SpaceInventories.Count == 0)
            {
                args.Player.SendInfoMessage("Нет сохранённых шаблонов инвентаря.");
                return;
            }

            args.Player.SendInfoMessage("Доступные шаблоны инвентаря:");
            foreach (var inv in config.SpaceInventories.Keys)
            {
                args.Player.SendInfoMessage($"  - {inv}");
            }
        }

        private void CreateEmptyTemplate(CommandArgs args)
        {
            if (args.Parameters.Count < 1)
            {
                args.Player.SendErrorMessage("Использование: /createinv <название>");
                return;
            }

            string templateName = string.Join(" ", args.Parameters);
            
            if (config.SpaceInventories.ContainsKey(templateName))
            {
                args.Player.SendErrorMessage($"Шаблон '{templateName}' уже существует!");
                return;
            }

            var inv = new InventoryData();
            for (int i = 0; i < 59; i++)
                inv.Items.Add(new ItemData { NetID = 0, Stack = 0, Prefix = 0 });
            for (int i = 0; i < 20; i++)
                inv.Armor.Add(new ItemData { NetID = 0, Stack = 0, Prefix = 0 });
            for (int i = 0; i < 10; i++)
                inv.Dyes.Add(new ItemData { NetID = 0, Stack = 0, Prefix = 0 });
            for (int i = 0; i < 5; i++)
                inv.MiscEquips.Add(new ItemData { NetID = 0, Stack = 0, Prefix = 0 });
            for (int i = 0; i < 5; i++)
                inv.MiscDyes.Add(new ItemData { NetID = 0, Stack = 0, Prefix = 0 });

            config.SpaceInventories[templateName] = inv;
            SaveConfig();
            SaveInventoryToDatabase(templateName, inv);
            
            args.Player.SendSuccessMessage($"Пустой шаблон инвентаря '{templateName}' создан!");
        }

        private void SetTemplateStats(CommandArgs args)
        {
            if (args.Parameters.Count < 5)
            {
                args.Player.SendErrorMessage("Использование: /setinvstats <шаблон> <hp> <hpmax> <мана> <манаmax>");
                return;
            }

            string templateName = args.Parameters[0];
            
            if (!int.TryParse(args.Parameters[1], out int hp) ||
                !int.TryParse(args.Parameters[2], out int hpMax) ||
                !int.TryParse(args.Parameters[3], out int mana) ||
                !int.TryParse(args.Parameters[4], out int manaMax))
            {
                args.Player.SendErrorMessage("Все параметры должны быть числами!");
                return;
            }

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
            
            SaveConfig();
            SavePlayerInventoryToDatabase(templateName, inv);
            
            args.Player.SendSuccessMessage($"Статы шаблона '{templateName}' обновлены: HP={hp}/{hpMax}, Mana={mana}/{manaMax}");
        }

        private void AddTemplateItem(CommandArgs args)
        {
            if (args.Parameters.Count < 3)
            {
                args.Player.SendErrorMessage("Использование: /addinvitem <шаблон> <слот> <предмет> [кол-во] [префикс]");
                args.Player.SendInfoMessage("Слот: items0-58, armor0-19");
                return;
            }

            string templateName = args.Parameters[0];
            string slotStr = args.Parameters[1].ToLower();
            
            string slotType = "";
            int slotIndex = -1;
            
            if (slotStr.StartsWith("items"))
            {
                slotType = "items";
                if (!int.TryParse(slotStr.Substring(5), out slotIndex))
                {
                    args.Player.SendErrorMessage("Некорректный формат слота! Пример: items0, items5");
                    return;
                }
            }
            else if (slotStr.StartsWith("armor"))
            {
                slotType = "armor";
                if (!int.TryParse(slotStr.Substring(5), out slotIndex))
                {
                    args.Player.SendErrorMessage("Некорректный формат слота! Пример: armor0, armor5");
                    return;
                }
            }
            else
            {
                args.Player.SendErrorMessage("Слот должен начинаться с 'items' или 'armor'!");
                return;
            }

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
                    if (slotIndex < 0 || slotIndex >= inv.Items.Count)
                    {
                        args.Player.SendErrorMessage($"Некорректный номер слота! Допустимо: 0-{inv.Items.Count - 1}");
                        return;
                    }
                    inv.Items[slotIndex] = new ItemData { NetID = itemId, Stack = stack, Prefix = prefix };
                }
                else if (slotType == "armor")
                {
                    if (slotIndex < 0 || slotIndex >= inv.Armor.Count)
                    {
                        args.Player.SendErrorMessage($"Некорректный номер слота! Допустимо: 0-{inv.Armor.Count - 1}");
                        return;
                    }
                    inv.Armor[slotIndex] = new ItemData { NetID = itemId, Stack = stack, Prefix = prefix };
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
            
            database?.Query(@"UPDATE MiniCore_Inventories 
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
            
            if (!config.Spaces.Any(s => s.Name.Equals(spaceName, StringComparison.OrdinalIgnoreCase)))
            {
                args.Player.SendErrorMessage($"Простор '{spaceName}' не найден!");
                return;
            }

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
            
            long currentTime = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            if (currentTime - space.LastRandomSpawnTime >= 600)
            {
                space.RandomSpawnX = random.Next(100, Main.maxTilesX - 100);
                space.RandomSpawnY = random.Next(100, Main.maxTilesY - 100);
                space.LastRandomSpawnTime = currentTime;
                SaveConfig();
            }
            
            int teleportX = space.RandomSpawnX > 0 ? space.RandomSpawnX : space.SpawnX;
            int teleportY = space.RandomSpawnY > 0 ? space.RandomSpawnY : space.SpawnY;
            player.Teleport(teleportX * 16, teleportY * 16);

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
            string? spaceName = pData.CurrentSpace;

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

        private void ListSpaces(CommandArgs args)
        {
            if (config.Spaces.Count == 0)
            {
                args.Player.SendInfoMessage("Нет доступных просторов.");
                return;
            }

            args.Player.SendInfoMessage("Доступные просторы:");
            foreach (var space in config.Spaces)
            {
                int currentPlayers = TShock.Players.Count(p => p != null && playerData.ContainsKey(p.Index) && playerData[p.Index].CurrentSpace == space.Name);
                args.Player.SendInfoMessage($"  - {space.Name} [{currentPlayers}/{space.MaxPlayers}]");
            }
        }

        private void ShowHelp(CommandArgs args)
        {
            args.Player.SendMessage("=== СПРАВКА ПО КОМАНДАМ MINICORE ===", Color.Cyan);
            
            args.Player.SendMessage("--- КОМАНДЫ АДМИНИСТРАТОРА ---", Color.Yellow);
            args.Player.SendMessage("/createspace <название> (/cs) - Создать новый простор", Color.White);
            args.Player.SendMessage("/deletespace <название> (/ds) - Удалить простор", Color.White);
            args.Player.SendMessage("/setspacelimit <название> <лимит> (/ssl) - Установить макс. игроков", Color.White);
            args.Player.SendMessage("/setspacespawn <название> (/sss) - Установить точку спавна", Color.White);
            args.Player.SendMessage("/addinvspace <простор> <инвентарь> (/ais) - Добавить инвентарь к простору", Color.White);
            args.Player.SendMessage("/removeinvspace <простор> <инвентарь> (/ris) - Удалить инвентарь из простора", Color.White);
            args.Player.SendMessage("/listspaceinv <простор> (/lsi) - Показать инвентари простора", Color.White);
            args.Player.SendMessage("/invranlim <простор> <кол-во> <инвентарь> (/irl) - Лимит инвентаря (Murder режим)", Color.White);
            args.Player.SendMessage("/setinvdead <простор> <инвентарь> (/ssd) - Инвентарь при смерти", Color.White);
            args.Player.SendMessage("/setrespawndead <простор> (/srd) - Точка спавна при смерти", Color.White);
            args.Player.SendMessage("/setbuffspace <простор> <buffid> (/sbs) - Установить бафф при входе", Color.White);
            args.Player.SendMessage("/settimerstart <простор> <секунды> (/sts) - Обратный отсчёт до старта", Color.White);
            
            args.Player.SendMessage("--- УПРАВЛЕНИЕ ИНВЕНТАРЁМ ---", Color.Yellow);
            args.Player.SendMessage("/saveinv <название> (/si) - Сохранить текущий инвентарь как шаблон", Color.White);
            args.Player.SendMessage("/delinv <название> (/di) - Удалить шаблон инвентаря", Color.White);
            args.Player.SendMessage("/listinv (/li) - Список всех шаблонов инвентаря", Color.White);
            args.Player.SendMessage("/createinv <название> (/ci) - Создать пустой шаблон", Color.White);
            args.Player.SendMessage("/setinvstats <шаблон> <hp> <hpmax> <мана> <манаmax> (/sis) - Установить статы", Color.White);
            args.Player.SendMessage("/addinvitem <шаблон> <слот> <предмет> [кол-во] [префикс] (/aii) - Добавить предмет", Color.White);
            
            args.Player.SendMessage("--- УПРАВЛЕНИЕ ТАБЛИЧКАМИ ---", Color.Yellow);
            args.Player.SendMessage("/linksign <простор> (/lsign) - Привязать табличку к простору", Color.White);
            args.Player.SendMessage("/unlinksign <простор> (/ulsign) - Отвязать табличку от простора", Color.White);
            
            args.Player.SendMessage("--- КОМАНДЫ ИГРОКОВ ---", Color.Yellow);
            args.Player.SendMessage("/pickinv <инвентарь> (/pi) - Выбрать инвентарь в простое", Color.White);
            args.Player.SendMessage("/joinspace <название> (/js) - Войти в простор", Color.White);
            args.Player.SendMessage("/leavespace (/ls) - Выйти из простора", Color.White);
            args.Player.SendMessage("/listspaces (/spaces) - Список доступных просторов", Color.White);
            args.Player.SendMessage("/mhelp (/minihelp) - Показать эту справку", Color.White);
            
            args.Player.SendMessage("--- ПАРАМЕТРЫ ПРЕДМЕТОВ ---", Color.Yellow);
            args.Player.SendMessage("/setitemparam <простор> <netid> <параметр> <значение> (/sip)", Color.White);
            args.Player.SendMessage("/getitemparam <простор> <netid> <параметр> (/gip)", Color.White);
            args.Player.SendMessage("/listitemparams <простор> [netid] (/lip)", Color.White);
            args.Player.SendMessage("/removeitemparam <простор> <netid> <параметр> (/rip)", Color.White);
        }

        private void SetItemParameter(CommandArgs args)
        {
            if (args.Parameters.Count < 4)
            {
                args.Player.SendErrorMessage("Использование: /setitemparam <пространство> <netid> <параметр> <значение>");
                args.Player.SendInfoMessage("Доступные параметры: damage, cooldown, mana, knockback, crit, color, speed, projectile, useatime");
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

            if (string.IsNullOrWhiteSpace(paramValue))
            {
                args.Player.SendErrorMessage("Значение параметра не может быть пустым!");
                return;
            }

            var space = config.Spaces.FirstOrDefault(s => s.Name.Equals(spaceName, StringComparison.OrdinalIgnoreCase));
            if (space == null)
            {
                args.Player.SendErrorMessage($"Пространство '{spaceName}' не найдено!");
                return;
            }

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
                case "speed":
                case "useatime":
                case "attacktime":
                    if (!float.TryParse(paramValue, out _))
                    {
                        args.Player.SendErrorMessage($"Параметр '{paramName}' должен быть числом!");
                        return false;
                    }
                    break;
                case "projectile":
                    if (!int.TryParse(paramValue, out _))
                    {
                        args.Player.SendErrorMessage($"Параметр '{paramName}' должен быть целым числом!");
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

            int? filterNetID = null;
            if (args.Parameters.Count > 1 && int.TryParse(args.Parameters[1], out int nid))
            {
                filterNetID = nid;
            }

            var paramsToShow = filterNetID.HasValue 
                ? space.ItemParameters.Where(p => p.ItemNetID == filterNetID.Value).ToList()
                : space.ItemParameters.ToList();

            if (!paramsToShow.Any())
            {
                args.Player.SendInfoMessage("Параметры не найдены.");
                return;
            }

            args.Player.SendInfoMessage($"=== Параметры в пространстве '{spaceName}' ===");
            foreach (var param in paramsToShow)
            {
                args.Player.SendInfoMessage($"  NetID {param.ItemNetID}: {param.ParameterName} = '{param.ParameterValue}'");
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

            var param = space.ItemParameters.FirstOrDefault(p => p.ItemNetID == netID && p.ParameterName.Equals(paramName, StringComparison.OrdinalIgnoreCase));
            if (param == null)
            {
                args.Player.SendErrorMessage($"Параметр '{paramName}' не найден для предмета {netID}");
                return;
            }

            space.ItemParameters.Remove(param);
            SaveConfig();

            try
            {
                database?.Query(@"DELETE FROM MiniCore_CustomItems 
                    WHERE SpaceName = @0 AND ItemNetID = @1 AND ParameterName = @2",
                    spaceName, netID, paramName);
            }
            catch (Exception ex)
            {
                TShock.Log.ConsoleError($"[MiniCore] Ошибка удаления параметра: {ex.Message}");
            }

            args.Player.SendSuccessMessage($"Параметр '{paramName}' удалён для предмета {netID}");
        }

        private void AddRewardArea(CommandArgs args)
        {
            if (args.Parameters.Count < 4)
            {
                args.Player.SendErrorMessage("Использование: /ara <название> <радиус> <награда> <кулдаун_секунды>");
                return;
            }

            string areaName = args.Parameters[0];
            
            if (!int.TryParse(args.Parameters[1], out int radius) || radius < 1)
            {
                args.Player.SendErrorMessage("Радиус должен быть положительным числом!");
                return;
            }

            if (!int.TryParse(args.Parameters[2], out int reward) || reward < 0)
            {
                args.Player.SendErrorMessage("Награда должна быть неотрицательным числом!");
                return;
            }

            if (!int.TryParse(args.Parameters[3], out int cooldown) || cooldown < 0)
            {
                args.Player.SendErrorMessage("Кулдаун должен быть неотрицательным числом!");
                return;
            }

            if (config.RewardAreas.Any(a => a.Name.Equals(areaName, StringComparison.OrdinalIgnoreCase)))
            {
                args.Player.SendErrorMessage($"Зона награды '{areaName}' уже существует!");
                return;
            }

            var area = new RewardArea
            {
                Name = areaName,
                X = args.Player.TileX,
                Y = args.Player.TileY,
                Radius = radius,
                MoneyReward = reward,
                CooldownSeconds = cooldown,
                LastRewardTimes = new Dictionary<int, DateTime>()
            };

            config.RewardAreas.Add(area);
            SaveConfig();

            try
            {
                database?.Query(@"INSERT INTO MiniCore_RewardAreas 
                    (Name, X, Y, Radius, MoneyReward, CooldownSeconds, LastRewardTimes) 
                    VALUES (@0, @1, @2, @3, @4, @5, @6)",
                    area.Name, area.X, area.Y, area.Radius, area.MoneyReward, area.CooldownSeconds, "{}");
            }
            catch (Exception ex)
            {
                TShock.Log.ConsoleError($"[MiniCore] Ошибка сохранения зоны награды: {ex.Message}");
            }

            args.Player.SendSuccessMessage($"Зона награды '{areaName}' создана на [{area.X}, {area.Y}] с радиусом {radius}!");
        }

        private void OnChat(ServerChatEventArgs args)
        {
            var player = TShock.Players[args.Who];
            if (player == null) return;

            if (!playerData.ContainsKey(player.Index)) return;

            var pData = playerData[player.Index];
            if (string.IsNullOrEmpty(pData.CurrentSpace)) return;

            args.Handled = true;

            string message = $"[{pData.CurrentSpace}] {player.Name}: {args.Text}";
            BroadcastToSpace(pData.CurrentSpace, message, Color.White);
        }

        private void OnGameUpdate(EventArgs args)
        {
            foreach (var area in config.RewardAreas)
            {
                foreach (var player in TShock.Players.Where(p => p != null && p.Active))
                {
                    int distance = (int)Math.Sqrt(Math.Pow(player.TileX - area.X, 2) + Math.Pow(player.TileY - area.Y, 2));
                    if (distance <= area.Radius)
                    {
                        if (!area.LastRewardTimes.ContainsKey(player.Index) ||
                            (DateTime.UtcNow - area.LastRewardTimes[player.Index]).TotalSeconds >= area.CooldownSeconds)
                        {
                            area.LastRewardTimes[player.Index] = DateTime.UtcNow;
                            player.SendInfoMessage($"Вы получили {area.MoneyReward} монет за зону '{area.Name}'!");
                        }
                    }
                }
            }
        }

        private void OnLeave(LeaveEventArgs args)
        {
            if (playerData.ContainsKey(args.Who))
            {
                var player = TShock.Players[args.Who];
                if (player != null)
                {
                    ForceLeaveSpace(player);
                }
                playerData.Remove(args.Who);
            }
        }

        private void OnPlayerSpawn(object? sender, GetDataHandlers.SpawnEventArgs args)
        {
            var player = TShock.Players[args.Player];
            if (player == null) return;

            if (!playerData.ContainsKey(player.Index)) return;

            var pData = playerData[player.Index];
            if (string.IsNullOrEmpty(pData.CurrentSpace)) return;

            var space = config.Spaces.FirstOrDefault(s => s.Name.Equals(pData.CurrentSpace, StringComparison.OrdinalIgnoreCase));
            if (space == null) return;

            if (!string.IsNullOrEmpty(space.DeadInventoryName) && config.SpaceInventories.ContainsKey(space.DeadInventoryName))
            {
                RestorePlayerInventoryWithSync(player, config.SpaceInventories[space.DeadInventoryName], space);
            }

            if (space.DeadSpawnX > 0 && space.DeadSpawnY > 0)
            {
                player.Teleport(space.DeadSpawnX * 16, space.DeadSpawnY * 16);
            }
        }

        private void OnSignInteract(object? sender, GetDataHandlers.SignReadEventArgs args)
        {
            var player = TShock.Players[args.Player];
            if (player == null) return;

            if (!playerData.ContainsKey(player.Index)) return;

            var pData = playerData[player.Index];

            if (!string.IsNullOrEmpty(pData.AwaitingSignLink))
            {
                var space = config.Spaces.FirstOrDefault(s => s.Name.Equals(pData.AwaitingSignLink, StringComparison.OrdinalIgnoreCase));
                if (space != null)
                {
                    if (!space.LinkedSigns.Any(s => s.X == args.X && s.Y == args.Y))
                    {
                        space.LinkedSigns.Add(new SignLocation { X = args.X, Y = args.Y });
                        SaveConfig();

                        try
                        {
                            database?.Query("INSERT INTO MiniCore_Signs (SpaceName, X, Y) VALUES (@0, @1, @2)",
                                space.Name, args.X, args.Y);
                        }
                        catch (Exception ex)
                        {
                            TShock.Log.ConsoleError($"[MiniCore] Ошибка сохранения таблички: {ex.Message}");
                        }

                        player.SendSuccessMessage($"Табличка привязана к Простору '{space.Name}'!");
                    }
                    else
                    {
                        player.SendErrorMessage("Эта табличка уже привязана к этому простору!");
                    }
                }
                pData.AwaitingSignLink = null;
                args.Handled = true;
                return;
            }

            if (pData.AwaitingSignUnlink)
            {
                foreach (var space in config.Spaces)
                {
                    var sign = space.LinkedSigns.FirstOrDefault(s => s.X == args.X && s.Y == args.Y);
                    if (sign != null)
                    {
                        space.LinkedSigns.Remove(sign);
                        SaveConfig();

                        try
                        {
                            database?.Query("DELETE FROM MiniCore_Signs WHERE X = @0 AND Y = @1", args.X, args.Y);
                        }
                        catch (Exception ex)
                        {
                            TShock.Log.ConsoleError($"[MiniCore] Ошибка удаления таблички: {ex.Message}");
                        }

                        player.SendSuccessMessage($"Табличка отвязана от Простора '{space.Name}'!");
                        break;
                    }
                }
                pData.AwaitingSignUnlink = false;
                args.Handled = true;
                return;
            }

            foreach (var space in config.Spaces)
            {
                if (space.LinkedSigns.Any(s => s.X == args.X && s.Y == args.Y))
                {
                    if ((DateTime.UtcNow - pData.LastSignClickTime).TotalMilliseconds < 500 &&
                        pData.LastSignClick != null &&
                        pData.LastSignClick.X == args.X &&
                        pData.LastSignClick.Y == args.Y)
                    {
                        JoinSpaceInternal(player, space.Name);
                        pData.LastSignClick = null;
                    }
                    else
                    {
                        pData.LastSignClickTime = DateTime.UtcNow;
                        pData.LastSignClick = new SignLocation { X = args.X, Y = args.Y };
                        player.SendInfoMessage($"Кликните ещё раз, чтобы войти в Простор '{space.Name}'");
                    }
                    args.Handled = true;
                    return;
                }
            }
        }

        private void BroadcastToSpace(string spaceName, string message, Color color, int excludeIndex = -1)
        {
            foreach (var player in TShock.Players.Where(p => p != null && p.Active))
            {
                if (player.Index == excludeIndex) continue;

                if (playerData.ContainsKey(player.Index) && 
                    playerData[player.Index].CurrentSpace != null &&
                    playerData[player.Index].CurrentSpace.Equals(spaceName, StringComparison.OrdinalIgnoreCase))
                {
                    player.SendMessage(message, color);
                }
            }
        }

        private InventoryData SavePlayerInventory(TSPlayer player)
        {
            var inv = new InventoryData();
            
            for (int i = 0; i < 59; i++)
            {
                var item = player.TPlayer.inventory[i];
                inv.Items.Add(new ItemData { NetID = item.netID, Stack = item.stack, Prefix = item.prefix });
            }

            for (int i = 0; i < 20; i++)
            {
                var item = player.TPlayer.armor[i];
                inv.Armor.Add(new ItemData { NetID = item.netID, Stack = item.stack, Prefix = item.prefix });
            }

            for (int i = 0; i < 10; i++)
            {
                var item = player.TPlayer.dye[i];
                inv.Dyes.Add(new ItemData { NetID = item.netID, Stack = item.stack, Prefix = item.prefix });
            }

            for (int i = 0; i < 5; i++)
            {
                var item = player.TPlayer.miscEquips[i];
                inv.MiscEquips.Add(new ItemData { NetID = item.netID, Stack = item.stack, Prefix = item.prefix });
            }

            for (int i = 0; i < 5; i++)
            {
                var item = player.TPlayer.miscDyes[i];
                inv.MiscDyes.Add(new ItemData { NetID = item.netID, Stack = item.stack, Prefix = item.prefix });
            }

            inv.StatLife = player.TPlayer.statLife;
            inv.StatLifeMax = player.TPlayer.statLifeMax;
            inv.StatMana = player.TPlayer.statMana;
            inv.StatManaMax = player.TPlayer.statManaMax;

            return inv;
        }

        private void SendWorldInfo(TSPlayer player)
        {
            using (var s = new MemoryStream())
            {
                using (var w = new BinaryWriter(s))
                {
                    w.Write((short)0);
                    w.Write((byte)7);
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
                    for (int i = 0; i < 3; i++)
                        w.Write(Main.treeX[i]);
                    for (int i = 0; i < 4; i++)
                        w.Write((byte)Main.treeStyle[i]);
                    for (int i = 0; i < 3; i++)
                        w.Write(Main.caveBackX[i]);
                    for (int i = 0; i < 4; i++)
                        w.Write((byte)Main.caveBackStyle[i]);
                    for (int i = 0; i < 13; i++)
                        w.Write((byte)Main.treeTops.GetTreeFoliageData().TreeLeaf.ElementAtOrDefault(i));
                    w.Write(Main.maxRaining);
                    BitsByte bb2 = 0;
                    {
                        bb2[0] = WorldGen.shadowOrbSmashed;
                        bb2[1] = NPC.downedBoss1;
                        bb2[2] = NPC.downedBoss2;
                        bb2[3] = NPC.downedBoss3;
                        bb2[4] = Main.hardMode;
                        bb2[5] = NPC.downedClown;
                        bb2[6] = Main.ServerSideCharacter;
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
                        bb4[0] = Main.expertMode;
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
                            item.color = new Color(r, g, b);
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
            player.SendData(PacketTypes.PlayerSlot, "", player.Index, slot, item.prefix);

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
        }

        private void RestorePlayerInventoryWithSync(TSPlayer player, InventoryData inv, Space? space = null)
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
            
            string? selectedInventory = null;
            
            if (!string.IsNullOrEmpty(pData.SelectedInventory) && 
                inventoryNames.Any(i => i.Equals(pData.SelectedInventory, StringComparison.OrdinalIgnoreCase)))
            {
                selectedInventory = pData.SelectedInventory;
            }
            
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
        
        public bool LimitedInvEnabled { get; set; } = false;
        public int LimitedInvCount { get; set; } = 1;
        public string LimitedInvName { get; set; } = "";
        
        public string DeadInventoryName { get; set; } = "";
        public int DeadSpawnX { get; set; } = 0;
        public int DeadSpawnY { get; set; } = 0;
        
        public int BuffID { get; set; } = 0;
        public int TimerStartSeconds { get; set; } = 0;
        
        public long LastRandomSpawnTime { get; set; } = 0;
        public int RandomSpawnX { get; set; } = 0;
        public int RandomSpawnY { get; set; } = 0;
        
        public List<ItemParameter> ItemParameters { get; set; } = new List<ItemParameter>();
    }

    public class SignLocation
    {
        public int X { get; set; }
        public int Y { get; set; }
    }

    public class PlayerData
    {
        public string? CurrentSpace { get; set; }
        public InventoryData? LobbyInventory { get; set; }
        public string? AwaitingSignLink { get; set; }
        public bool AwaitingSignUnlink { get; set; }
        public bool OriginalPvPState { get; set; }
        public DateTime LastSignClickTime { get; set; }
        public SignLocation? LastSignClick { get; set; }
        
        public bool HasLimitedInventory { get; set; } = false;
        public bool AwaitingDeadRespawnSet { get; set; } = false;
        
        public string? SelectedInventory { get; set; }
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
