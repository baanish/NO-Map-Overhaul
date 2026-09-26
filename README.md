# baanish-ui-improvements

My Nuclear Option mod for map and HUD navigation aids. The first release adds runway markers. Every friendly runway shows on the minimap and the full map with its number at each end, plus an approach line off the nearest runway end.

It only draws on your screen. It installs no patches into the game's code and sends nothing over the network. I've tested it in free flight and in a multiplayer session.

## Runway markers

- **Runways on the map.** Each friendly runway is a green strip with a thin dark rim, so it stays readable over the map's own bright linework. Its number sits past each end, so the west end of an east-west runway reads 09 and the east end 27. The numbers come from the runway heading, the same way the game numbers its landing clearance, except where the paint on the tarmac says otherwise.
- **Approach line.** Within 5 km, a dashed 5 km centerline extends off the nearest end of the nearest runway, so you can judge the turn onto final. Each dash plus its gap is 500 m, so the dashes double as distance ticks.
- **HUD callout.** The same runway end gets a `RWY 27` label pinned over its threshold in the 3D view, ready for an ATC call. An optional prefix adds the airbase: `NBSCLI RWY 27`.
- **Airbase boundary**, off by default. A faint shaded circle in the game's friendly map colour marks each friendly airbase's landing zone. Stop inside it after landing and the sortie ends as returned instead of crashed.
- **Airbase names**, off by default. Every airbase, friendly, enemy, and neutral, gets a faint name label on the full map, so you can find the one someone just called out.

It doesn't guess which way you'll land: it always shows the end you're closest to. Helicopters get the airbase boundary but no runways (the Tarantula tiltrotor lands on runways, so it counts as a plane), and carriers are left out because the ship's own icon already marks the deck.

## Coming soon

These came from squadron requests and aren't built yet:

- Map markers, and drawing on the map.
- Waypoints, with HUD markers.
- An arrow toward incoming missiles outside your view, to help find one to shoot down.

## Requirements

- Nuclear Option 0.34.2.
- BepInEx 5, tested with 5.4.23.4.
- Optional: [BepInEx Configuration Manager](https://github.com/BepInEx/BepInEx.ConfigurationManager) to change settings in game with F1.

## Install

Download a ZIP from [releases](https://github.com/baanish/baanish-ui-improvements/releases), then follow the [user guide](docs/USER_GUIDE.md#install). The mod isn't in the Nuclear Option Mod Manager catalog yet.

The [user guide](docs/USER_GUIDE.md) also covers every setting, how the runway end is chosen, and the airbase abbreviations.

## Contributing

Bug reports and ideas are welcome. Include your game and mod versions, a screenshot, and what you expected to see. See [contributing](CONTRIBUTING.md) to build it yourself.

## License

[MIT](LICENSE). Nuclear Option and its assets belong to their respective owners.
