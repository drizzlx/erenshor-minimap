# Mini Map

Adds a mini-map to the game that tracks vendors, mobs, sims, mining nodes, dungeon entrances, etc.

## Installation
- Install [BepInEx Mod Pack](https://thunderstore.io/package/bbepis/BepInExPack/)
- Download the latest [release]()
- Extract the mod into *Erenshor\BepInEx\plugins* folder

## Map Color Key

<table>
  <tr>
    <td><span style="display:inline-block;width:12px;height:12px;background-color:#ff0000;border-radius:50%;"></span></td>
    <td><strong>Enemy [RED]</strong></td>
  </tr>
  <tr>
    <td><span style="display:inline-block;width:12px;height:12px;background-color:#00ff00;border-radius:50%;"></span></td>
    <td><strong>Party Member [GREEN]</strong></td>
  </tr>
  <tr>
    <td><span style="display:inline-block;width:12px;height:12px;background-color:#007bff;border-radius:50%;"></span></td>
    <td><strong>Sim Player [BLUE]</strong></td>
  </tr>
  <tr>
    <td><span style="display:inline-block;width:12px;height:12px;background-color:#ffff00;border-radius:50%;"></span></td>
    <td><strong>Vendor / Bank [YELLOW]</strong></td>
  </tr>
  <tr>
    <td><span style="display:inline-block;width:12px;height:12px;background-color:#a020f0;border-radius:50%;"></span></td>
    <td><strong>Mining Node [PURPLE]</strong></td>
  </tr>
  <tr>
    <td><span style="display:inline-block;width:12px;height:12px;background-color:#444444;border-radius:50%;"></span></td>
    <td><strong>Miscellaneous NPC [GRAY]</strong></td>
  </tr>
</table>

## Change Log
2.0.2
- Fix console errors after changing scenes or characters.
2.0.1
- Improved performance, especially in large towns with lots of sims.
- Fix NULL errors spamming the console.
2.0.0
- Display Zone Entrances on map, such as dungeons (looks like a portal).
- Resizable UI (click to drag in top left, bottom left, or bottom right corner).
- Draggable UI (click icon in the top right corner).
- Mouse scroll to zoom in and out (hover the Mini Map and scroll).
- Dead party member sims will continue to appear on the map.
- Fixed console errors related to height being 0.
- Complete UI overhaul.

1.2.0
- Change to a more visible player arrow image.
- Fixed map orientation bug, North should always be upwards.
- Improvements with UI and transparency.

1.1.1
- Fix arrow image not showing when installed by mod manager.

1.1.0
- Displays zone name and X,Y coordinates.
- Map orientation now matches North, South, East, West.
- Player position represented by a directional facing arrow.
- Improved color scheme.