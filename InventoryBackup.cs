using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.Linq;

using Newtonsoft.Json;
using Oxide.Core;
using UnityEngine;

namespace Oxide.Plugins
{
    [Info("Inventory Backup", "MON@H", "1.0.6")]
    [Description("Allows to save and restore players inventories​")]

    public class InventoryBackup : RustPlugin
    {
        #region Variables

        private const string PermissionUse = "inventorybackup.use";
        private static readonly Regex _regexStripTags = new Regex("<color=.+?>|</color>|<size=.+?>|</size>|<i>|</i>|<b>|</b>", RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.Compiled);

        #endregion Variables

        #region Initialization

        private void Init()
        {
            if (!_configData.ClearOnWipe)
            {
                Unsubscribe(nameof(OnNewSave));
            }

            RegisterPermissions();
            AddCommands();
            LoadData();
        }

        private void OnNewSave(string filename) => ClearData();

        #endregion Initialization

        #region Configuration

        private ConfigData _configData;

        private class ConfigData
        {
            [JsonProperty(PropertyName = "Storage duration (days)")]
            public double StorageDuration = -1d;

            [JsonProperty(PropertyName = "Clear inventories data on wipe")]
            public bool ClearOnWipe = false;

            [JsonProperty(PropertyName = "Logging enabled")]
            public bool LoggingEnabled = false;

            [JsonProperty(PropertyName = "Chat steamID icon")]
            public ulong SteamIDIcon = 0;

            [JsonProperty(PropertyName = "Commands list", ObjectCreationHandling = ObjectCreationHandling.Replace)]
            public List<string> Commands = new List<string>()
            {
                "invbackup"
            };
        }

        protected override void LoadConfig()
        {
            base.LoadConfig();
            try
            {
                _configData = Config.ReadObject<ConfigData>();
                if (_configData == null)
                {
                    throw new Exception();
                }
                SaveConfig();
            }
            catch (Exception exception)
            {
                PrintError($"Loading config file threw exception:\n{exception}");
                LoadDefaultConfig();
            }
        }

        protected override void LoadDefaultConfig()
        {
            PrintWarning("Creating a new configuration file");
            _configData = new ConfigData();
        }

        protected override void SaveConfig() => Config.WriteObject(_configData);

        #endregion Configuration

        #region DataFile

        private StoredData _storedData;

        private class StoredData
        {
            public readonly Hash<ulong, Hash<string, PlayerInventoryData>> Inventories = new Hash<ulong, Hash<string, PlayerInventoryData>>();
        }

        public class PlayerInventoryData
        {
            public DateTime SaveDate = DateTime.Now;
            public List<ItemData> ItemsBelt;
            public List<ItemData> ItemsMain;
            public List<ItemData> ItemsWear;
        }

        public class ItemData
        {
            public bool IsBlueprint;
            public float Condition;
            public float Fuel;
            public float MaxCondition = -1;
            public int Ammo;
            public int AmmoType;
            public int Amount;
            public int BlueprintTarget;
            public int DataInt;
            public int FlameFuel;
            public int ID;
            public int Position = -1;
            public string Name;
            public string Text;
            public ulong Skin;

            public List<ItemData> Contents = new List<ItemData>();

            public Item ToItem()
            {
                if (Amount == 0)
                {
                    return null;
                }

                Item item = ItemManager.CreateByItemID(ID, Amount, Skin);

                item.position = Position;

                if (IsBlueprint)
                {
                    item.blueprintTarget = BlueprintTarget;
                    return item;
                }

                item.fuel = Fuel;
                item.condition = Condition;

                if (MaxCondition != -1)
                {
                    item.maxCondition = MaxCondition;
                }

                if (Contents != null)
                {
                    if (Contents.Count > 0)
                    {
                        if (item.contents == null)
                        {
                            item.contents = new ItemContainer();
                            item.contents.ServerInitialize(null, Contents.Count);
                            item.contents.GiveUID();
                            item.contents.parent = item;
                        }

                        foreach (var contentItem in Contents)
                        {
                            contentItem.ToItem().MoveToContainer(item.contents);
                        }
                    }
                }
                else
                {
                    item.contents = null;
                }

                BaseProjectile.Magazine magazine = item.GetHeldEntity()?.GetComponent<BaseProjectile>()?.primaryMagazine;
                FlameThrower flameThrower = item.GetHeldEntity()?.GetComponent<FlameThrower>();

                if (magazine != null)
                {
                    magazine.contents = Ammo;
                    magazine.ammoType = ItemManager.FindItemDefinition(AmmoType);
                }

                if (flameThrower != null)
                {
                    flameThrower.ammo = FlameFuel;
                }

                if (DataInt > 0)
                {
                    item.instanceData = new ProtoBuf.Item.InstanceData() {
                        ShouldPool = false,
                        dataInt = DataInt
                    };
                }

                item.text = Text;

                if (Name != null)
                {
                    item.name = Name;
                }

                return item;
            }

            public static ItemData FromItem(Item item) => new ItemData() {
                ID = item.info.itemid,
                Position = item.position,
                Ammo = item.GetHeldEntity()?.GetComponent<BaseProjectile>()?.primaryMagazine?.contents ?? 0,
                AmmoType = item.GetHeldEntity()?.GetComponent<BaseProjectile>()?.primaryMagazine?.ammoType?.itemid ?? 0,
                Amount = item.amount,
                Condition = item.condition,
                MaxCondition = item.maxCondition,
                Fuel = item.fuel,
                Skin = item.skin,
                Contents = item.contents?.itemList?.Select(FromItem).ToList(),
                FlameFuel = item.GetHeldEntity()?.GetComponent<FlameThrower>()?.ammo ?? 0,
                IsBlueprint = item.IsBlueprint(),
                BlueprintTarget = item.blueprintTarget,
                DataInt = item.instanceData?.dataInt ?? 0,
                Name = item.name,
                Text = item.text
            };
        }

        public void LoadData()
        {
            try
            {
                _storedData = Interface.Oxide.DataFileSystem.ReadObject<StoredData>(Name);
            }
            catch (Exception exception)
            {
                PrintError($"Loading data file threw exception:\n{exception}");
                ClearData();
            }

            if (_configData.StorageDuration > -1)
            {
                Dictionary<ulong, string> inventoriesToRemove = new Dictionary<ulong, string>();

                foreach (KeyValuePair<ulong, Hash<string, PlayerInventoryData>> inventories in _storedData.Inventories)
                {
                    foreach (KeyValuePair<string, PlayerInventoryData> playerInventory in inventories.Value)
                    {
                        if ((DateTime.Now - playerInventory.Value.SaveDate).TotalDays > _configData.StorageDuration)
                        {
                            inventoriesToRemove.Add(inventories.Key, playerInventory.Key);
                        }
                    }
                }

                Hash<string, PlayerInventoryData> playerInventories;

                foreach (KeyValuePair<ulong, string> inventory in inventoriesToRemove)
                {
                    playerInventories = _storedData.Inventories[inventory.Key];

                    playerInventories.Remove(inventory.Value);

                    if (playerInventories.Count == 0)
                    {
                        _storedData.Inventories.Remove(inventory.Key);
                    }
                }

                SaveData();
            }
        }

        public void SaveData() => Interface.Oxide.DataFileSystem.WriteObject(Name, _storedData);

        public void ClearData()
        {
            PrintWarning("Creating a new data file");

            _storedData = new StoredData();

            SaveData();
        }

        #endregion DataFile

        #region Localization

        public string Lang(string key, string userIDString = null, params object[] args)
        {
            try
            {
                string message = string.Format(lang.GetMessage(key, this, userIDString), args);

                return string.IsNullOrEmpty(userIDString) ? StripRustTags(message) : message;
            }
            catch (Exception ex)
            {
                PrintError($"Lang Key '{key}' threw exception:\n{ex}");
                throw;
            }
        }

        private static class LangKeys
        {
            public static class Error
            {
                private const string Base = nameof(Error) + ".";
                public const string Failed = Base + nameof(Failed);
                public const string NoPermission = Base + nameof(NoPermission);
                public const string Syntax = Base + nameof(Syntax);
            }

            public static class Info
            {
                private const string Base = nameof(Info) + ".";
                public const string Success = Base + nameof(Success);
            }

            public static class Format
            {
                private const string Base = nameof(Format) + ".";
                public const string Prefix = Base + nameof(Prefix);
            }
        }

        protected override void LoadDefaultMessages()
        {
            lang.RegisterMessages(new Dictionary<string, string>
            {
                [LangKeys.Error.Failed] = "Operation failed!",
                [LangKeys.Error.NoPermission] = "You do not have permission to use this command!",
                [LangKeys.Format.Prefix] = "<color=#00FF00>[Inventory Backup]</color>: ",
                [LangKeys.Info.Success] = "Operation completed successfully!",

                [LangKeys.Error.Syntax] = "Syntax error occured!\n"
                + "<color=#FFFF00>/{0} save <SteamID> <inventory name></color> - Save player inventory\n"
                + "<color=#FFFF00>/{0} restore <SteamID> <inventory name></color> - Restore saved player inventory\n"
                + "<color=#FFFF00>/{0} remove <SteamID> <inventory name></color> - Remove saved player inventory\n",
            }, this);
            lang.RegisterMessages(new Dictionary<string, string>
            {
                [LangKeys.Error.Failed] = "Операция не удалась!",
                [LangKeys.Error.NoPermission] = "У вас нет разрешения на использование этой команды!",
                [LangKeys.Format.Prefix] = "<color=#00FF00>[Резервная копия инвентаря]</color>: ",
                [LangKeys.Info.Success] = "Операция успешно завершена!",

                [LangKeys.Error.Syntax] = "Синтаксическая ошибка!\n"
                + "<color=#FFFF00>/{0} save <SteamID> <inventory name></color> - Сохранить инвентарь игрока\n"
                + "<color=#FFFF00>/{0} restore <SteamID> <inventory name></color> - Восстановить сохранённый инвентарь игрока\n"
                + "<color=#FFFF00>/{0} remove <SteamID> <inventory name></color> - Удалить сохранённый инвентарь игрока\n",
            }, this, "ru");
        }

        #endregion Localization

        #region Commands

        [ConsoleCommand("invbackup")]
        private void ConsoleCmdInventoryBackup(ConsoleSystem.Arg arg)
        {
            BasePlayer player = arg.Player();

            if (player.IsValid() && !permission.UserHasPermission(player.UserIDString, PermissionUse))
            {
                SendReply(arg, Lang(LangKeys.Error.NoPermission, player.UserIDString));
                return;
            }

            ulong userID;
            if (!arg.HasArgs()
            || arg.Args.Length < 3
            || !ulong.TryParse(arg.Args[1], out userID)
            || !userID.IsSteamId())
            {
                SendReply(arg, Lang(LangKeys.Error.Syntax, player.UserIDString));
                return;
            }

            switch (arg.Args[0].ToLower())
            {
                case "save":
                    if (InventorySave(userID, arg.Args[2]))
                    {
                        SendReply(arg, Lang(LangKeys.Info.Success, player.UserIDString));
                        return;
                    }
                    SendReply(arg, Lang(LangKeys.Error.Failed, player.UserIDString));
                    return;
                case "restore":
                    if (InventoryRestore(userID, arg.Args[2]))
                    {
                        SendReply(arg, Lang(LangKeys.Info.Success, player.UserIDString));
                        return;
                    }
                    SendReply(arg, Lang(LangKeys.Error.Failed, player.UserIDString));
                    return;
                case "remove":
                    if (InventoryRemove(userID, arg.Args[2]))
                    {
                        SendReply(arg, Lang(LangKeys.Info.Success, player.UserIDString));
                        return;
                    }
                    SendReply(arg, Lang(LangKeys.Error.Failed, player.UserIDString));
                    return;
            }

            SendReply(arg, Lang(LangKeys.Error.Syntax, player.UserIDString, _configData.Commands[0]));
        }

        private void CmdInventoryBackup(BasePlayer player, string cmd, string[] args)
        {
            if (!player.IsValid() || !player.userID.IsSteamId())
            {
                return;
            }

            if (!permission.UserHasPermission(player.UserIDString, PermissionUse))
            {
                PlayerSendMessage(player, Lang(LangKeys.Error.NoPermission, player.UserIDString));
                return;
            }

            ulong userID;
            if (args == null || args.Length < 3 || !ulong.TryParse(args[1], out userID) || !userID.IsSteamId())
            {
                PlayerSendMessage(player, Lang(LangKeys.Error.Syntax, player.UserIDString, _configData.Commands[0]));
                return;
            }

            switch (args[0].ToLower())
            {
                case "save":
                    if (InventorySave(userID, args[2]))
                    {
                        PlayerSendMessage(player, Lang(LangKeys.Info.Success, player.UserIDString));
                        return;
                    }
                    PlayerSendMessage(player, Lang(LangKeys.Error.Failed, player.UserIDString));
                    return;
                case "restore":
                    if (InventoryRestore(userID, args[2]))
                    {
                        PlayerSendMessage(player, Lang(LangKeys.Info.Success, player.UserIDString));
                        return;
                    }
                    PlayerSendMessage(player, Lang(LangKeys.Error.Failed, player.UserIDString));
                    return;
                case "remove":
                    if (InventoryRemove(userID, args[2]))
                    {
                        PlayerSendMessage(player, Lang(LangKeys.Info.Success, player.UserIDString));
                        return;
                    }
                    PlayerSendMessage(player, Lang(LangKeys.Error.Failed, player.UserIDString));
                    return;
            }

            PlayerSendMessage(player, Lang(LangKeys.Error.Syntax, player.UserIDString, _configData.Commands[0]));
        }

        #endregion Commands

        #region API

        private bool InventorySave(ulong userID, string inventoryName)
        {
            if (!userID.IsSteamId() || string.IsNullOrEmpty(inventoryName))
            {
                return false;
            }

            BasePlayer player = FindPlayer(userID);

            if (!player.IsValid() || player.inventory.AllItems().Length < 1)
            {
                return false;
            }

            Hash<string, PlayerInventoryData> playerInventories = _storedData.Inventories[userID];

            if (playerInventories == null)
            {
                playerInventories = new Hash<string, PlayerInventoryData>();
                _storedData.Inventories[userID] = playerInventories;
            }

            playerInventories[inventoryName] = new PlayerInventoryData() {
                ItemsBelt = player.inventory.containerBelt.itemList.Select(ItemData.FromItem).ToList(),
                ItemsMain = player.inventory.containerMain.itemList.Select(ItemData.FromItem).ToList(),
                ItemsWear = player.inventory.containerWear.itemList.Select(ItemData.FromItem).ToList()
            };

            SaveData();
            Log($"inventory saved {userID} {inventoryName}");
            return true;
        }

        private bool InventoryRestore(ulong userID, string inventoryName, bool remove = false)
        {
            if (!userID.IsSteamId() || string.IsNullOrEmpty(inventoryName))
            {
                return false;
            }

            BasePlayer player = FindPlayer(userID);

            if (!player.IsValid())
            {
                return false;
            }

            player.inventory.Strip();

            Hash<string, PlayerInventoryData> playerInventories = _storedData.Inventories[userID];
            PlayerInventoryData inventoryData = playerInventories[inventoryName];

            if (inventoryData == null)
            {
                Log($"inventory not found {userID} {inventoryName}");
                return false;
            }

            foreach (ItemData inventoryItem in inventoryData.ItemsBelt)
            {
                Item item = inventoryItem.ToItem();

                item?.MoveToContainer(player.inventory.containerBelt, item.position);
            }

            foreach (ItemData inventoryItem in inventoryData.ItemsMain)
            {
                Item item = inventoryItem.ToItem();

                item?.MoveToContainer(player.inventory.containerMain, item.position);
            }

            foreach (ItemData inventoryItem in inventoryData.ItemsWear)
            {
                Item item = inventoryItem.ToItem();

                item?.MoveToContainer(player.inventory.containerWear, item.position);
            }

            Log($"inventory restored {userID} {inventoryName}");

            if (remove)
            {
                playerInventories.Remove(inventoryName);
                
                if (playerInventories.Count == 0)
                {
                    _storedData.Inventories.Remove(userID);
                }

                SaveData();
                Log($"inventory removed {userID} {inventoryName}");
            }

            return true;
        }

        private bool InventoryRemove(ulong userID, string inventoryName)
        {
            if (!userID.IsSteamId() || string.IsNullOrEmpty(inventoryName))
            {
                return false;
            }

            Hash<string, PlayerInventoryData> playerInventories = _storedData.Inventories[userID];

            if (playerInventories != null)
            {
                playerInventories.Remove(inventoryName);
                
                if (playerInventories.Count == 0)
                {
                    _storedData.Inventories.Remove(userID);
                }

                SaveData();
                Log($"inventory removed {userID} {inventoryName}");
            }

            return true;
        }

        #endregion API

        #region Helpers

        public void RegisterPermissions()
        {
            permission.RegisterPermission(PermissionUse, this);
        }

        public void AddCommands()
        {
            if (_configData.Commands.Count == 0)
            {
                _configData.Commands = new List<string>() { "invbackup" };
                SaveConfig();
            }

            foreach (string command in _configData.Commands)
            {
                cmd.AddChatCommand(command, this, nameof(CmdInventoryBackup));
            }
        }

        public string StripRustTags(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return string.Empty;
            }

            return _regexStripTags.Replace(text, string.Empty);
        }

        public BasePlayer FindPlayer(ulong userID)
        {
            BasePlayer player = BasePlayer.FindByID(userID);

            if (!player.IsValid())
            {
                return FindPlayer(userID.ToString());
            }

            return player;
        }

        public BasePlayer FindPlayer(string userIDString) => BasePlayer.FindAwakeOrSleeping(userIDString);

        public void Log(string text)
        {
            if (_configData.LoggingEnabled)
            {
                LogToFile("log", $"{DateTime.Now.ToString("HH:mm:ss")} {text}", this);
            }
        }

        public void PlayerSendMessage(BasePlayer player, string message)
        {
            string prefix = Lang(LangKeys.Format.Prefix, player.UserIDString);
            player.SendConsoleCommand("chat.add", 2, _configData.SteamIDIcon, string.IsNullOrEmpty(prefix) ? message : prefix + message);
        }

        #endregion Helpers
    }
}