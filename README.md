# InventoryBackup
Oxide plugin for Rust. Allows to save and restore players inventories

Plugin adds the ability to save and restore players inventories.

## Permissions

* `inventorybackup.use` -- Allows player to to save and restore players inventories

## Configuration

```json
{
  "Storage duration (days)": -1.0,
  "Clear inventories data on wipe": false,
  "Logging enabled": false,
  "Chat steamID icon": 0,
  "Commands list": [
    "invbackup"
  ]
}
```

## Localization

```json
{
  "Error.Failed": "Operation failed!",
  "Error.NoPermission": "You do not have permission to use this command!",
  "Format.Prefix": "<color=#00FF00>[Inventory Backup]</color>: ",
  "Info.Success": "Operation completed successfully!",
  "Error.Syntax": "Syntax error occured!\n<color=#FFFF00>/{0} save <SteamID> <inventory name></color> - Save player inventory\n<color=#FFFF00>/{0} restore <SteamID> <inventory name></color> - Restore saved player inventory\n<color=#FFFF00>/{0} remove <SteamID> <inventory name></color> - Remove saved player inventory\n"
}
```

## Developer API

### InventorySave

Used to save player inventory.

```csharp
bool InventorySave(ulong userID, string inventoryName)
```

* **Example**:
```csharp
if (InventoryBackup != null && InventoryBackup.IsLoaded)
{
    object resultCall = InventoryBackup.Call("InventorySave", player.userID, Name);

    if (resultCall is bool && (bool)resultCall)
    {
        Puts("Inventory saved");
    }
}
```

### InventoryRestore

Used to restore player inventory. Use `remove` to remove saved inventory after restoration.

```csharp
bool InventoryRestore(ulong userID, string inventoryName, bool remove = false)
```

* **Example**:
```csharp
if (InventoryBackup != null && InventoryBackup.IsLoaded)
{
    object resultCall = InventoryBackup.Call("InventoryRestore", player.userID, Name, true);

    if (resultCall is bool && (bool)resultCall)
    {
        Puts("Inventory restored");
    }
}
```

### InventoryRemove

Used to remove player inventory.

```csharp
bool InventoryRemove(ulong userID, string inventoryName)
```

* **Example**:
```csharp
if (InventoryBackup != null && InventoryBackup.IsLoaded)
{
    InventoryBackup.Call("InventoryRemove", player.userID, Name);
}
```