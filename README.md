## Features

- Allows customizing gather rates based on player permission
- Allows overriding gather rate by resource type and dispenser type
- Supports all types of resource dispensers

This plugin only alters gather rates, **not** smelt rates or loot rates.

## Permissions

### Rulesets

The following permissions come with this plugin's **default configuration**. Granting one to a player multiplies their gather rate for all resource types and dispensers.

- `gatherrates.ruleset.2x`
- `gatherrates.ruleset.5x`
- `gatherrates.ruleset.10x`
- `gatherrates.ruleset.100x`
- `gatherrates.ruleset.1000x`

You can add more gather rate rulesets in the plugin configuration, and the plugin will automatically generate permissions of the format `gatherrates.ruleset.<name>` when reloaded. If a player has permission to multiple rulesets, only the last one will apply, based on the order in the config. See the Configuration section for the various ways you can customize rulesets.

#### How rulesets are determined for various gathering types

- **Pickups:** Based on the player picking up the resource.
  - Examples: Wood, sulfur, hemp plants, crude oil barrels, as well as player-planted items,
- **Dispensers:** Based on the player using the tool (pickaxe, axe, etc.).
  - Examples: Stone nodes, trees, logs, driftwood, corpses, helicopter debris.
- **Player-owned Mining Quarries and Pump Jacks:** Based on the permissions of the owner.
  - Note: These deployables are no longer available in the vanilla game, but they can be made available via plugins.
- **Monument Mining Quarries and Pump Jacks:** Based on the player who last started the engine.
- **Excavators:** Based on the player who last selected a resource before the excavator arm started moving.

## Configuration

Default configuration:

```json
{
  "GatherRateRulesets": [
    {
      "Name": "2x",
      "DefaultRate": 2.0
    },
    {
      "Name": "5x",
      "DefaultRate": 5.0
    },
    {
      "Name": "10x",
      "DefaultRate": 10.0
    },
    {
      "Name": "100x",
      "DefaultRate": 100.0
    },
    {
      "Name": "1000x",
      "DefaultRate": 1000.0
    }
  ]
}
```

- `GatherRateRulesets` -- List of rulesets that determine player gather rates. Each ruleset generates a separate permission which can be granted to players or groups to make the ruleset apply to them.
  - `Name` -- Unique name used to generate a permission of the format `gatherrates.ruleset.<name>`.
  - `DefaultRate` -- Multiplier for all types of resource gathering, except for those overriden by `ItemRateOverrides` or `DispenserRateOverrides`.
  - `ItemRateOverrides` -- Mapping of item short names to gather rates, overriding `DefaultRate`.
    - Example:
      ```json
      "ItemRateOverrides": {
        "wood": 10.0,
        "stones": 5.0,
        "metal.ore": 2.0
      }
      ```
  - `DispenserRateOverrides` -- Mapping of dispenser entity short names to sub-mappings of item short names to gather rates, overriding both `DefaultRate` and `ItemRateOverrides`. This allows you to override gather rates per resource, based on the dispenser the resource is coming from.
    - Note: If you override rates for a dispenser that produces multiple items (e.g., `"metal.ore"` and `"hq.metal.ore"`), any items that you do not override will use the rates defined in `ItemRateOverrides` or `DefaultRate`. This allows you to only override specific combinations of dispensers and resources, without having to override them all.
    - Example:
      ```json
      "DispenserRateOverrides": {
        "miningquarry_static": {
          "stones": 50.0,
          "metal.ore": 50.0,
        },
      }
      ```

### Config example

The example config below serves to indicate the various ways you could configure rulesets. This example would generate the following permissions:

- `gatherrates.ruleset.10x_with_50x_wood_25x_stone`
  - 10x multiplier for all resources from any dispenser, with the following exceptions
  - 50x multiplier for wood from any dispenser
  - 25x multiplier for stone from any dispenser
- `gatherrates.ruleset.10x_with_50x_monument_rates`
  - 10x rates for all resources from any dispenser
  - 50x rates for Mining Quarries, Pump Jacks and Excavators
    - Includes to both the player-owned ones and the ones at monuments since both entity types are specified
- `gatherrates.ruleset.10x_with_50x_player_plant_rates`
  - 10x rates for all resources from any dispenser, with the following exceptions
  - 50x rates for resources harvested from player-owned plants
    - Does not include wild plants which use different entity names like `hemp-collectable`, so those will still use 10x
    - Note: The rate is determined based on the player harvesting the plant, not the player who planted it
  - 1x rates for seeds harvested from plants
    - Applies to both player-owned plants and wild plants since this is declared under `ItemRateOverrides` which does not specify the dispenser type
    - Note: This plugin does not affect the rate at which seeds are obtained by eating plants

```json
{
  "GatherRateRulesets": [
    {
      "Name": "10x_with_50x_wood_25x_stone",
      "DefaultRate": 10.0,
      "ItemRateOverrides": {
        "wood": 50.0,
        "stone": 25.0
      }
    },
    {
      "Name": "10x_with_50x_monument_rates",
      "DefaultRate": 10.0,
      "DispenserRateOverrides": {
        "miningquarry_static": {
          "stones": 50.0,
          "sulfur.ore": 50.0,
          "metal.ore": 50.0,
          "hq.metal.ore": 50.0
        },
        "mining_quarry": {
          "stones": 50.0,
          "sulfur.ore": 50.0,
          "metal.ore": 50.0,
          "hq.metal.ore": 50.0
        },
        "pumpjack-static": {
          "crude.oil": 50.0
        },
        "mining.pumpjack": {
          "crude.oil": 50.0
        },
        "excavator_yaw": {
          "stones": 50.0,
          "sulfur.ore": 50.0,
          "metal.ore": 50.0,
          "hq.metal.ore": 50.0
        }
      }
    },
    {
      "Name": "10x_with_50x_player_plant_rates",
      "DefaultRate": 10.0,
      "ItemRateOverrides": {
        "seed.corn": 1.0,
        "seed.hemp": 1.0,
        "seed.pumpkin": 1.0
      },
      "DispenserRateOverrides": {
        "corn.entity": {
          "corn": 50.0
        },
        "hemp.entity": {
          "cloth": 50.0
        },
        "potato.entity": {
          "potato": 50.0
        },
        "pumpkin.entity": {
          "pumpkin": 50.0
        },
        "black_berry.entity": {
          "black.berry": 50.0
        },
        "blue_berry.entity": {
          "blue.berry": 50.0
        },
        "green_berry.entity": {
          "green.berry": 50.0
        },
        "red_berry.entity": {
          "red.berry": 50.0
        },
        "white_berry.entity": {
          "white.berry": 50.0
        },
        "yellow_berry.entity": {
          "yellow.berry": 50.0
        }
      }
    }
  ]
}
```

## Developer Hooks

#### OnGatherRateMultiply

- Called when this plugin is about to multiply gather rate for a particular item.
- Returning `false` will prevent the item amount from being multiplied.
- Returning `null` will result in the default behavior.

```csharp
bool? OnGatherRateMultiply(BaseEntity dispenser, Item item, string userId)
```

The dispenser can be a collectible, corpse, node, tree, quarry, excavator, etc.
