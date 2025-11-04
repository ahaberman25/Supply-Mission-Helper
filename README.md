# Supply Mission Helper

A Dalamud plugin for Final Fantasy XIV that helps you calculate materials needed for Grand Company Supply Missions.

![Version](https://img.shields.io/github/v/release/ahaberman25/Supply-Mission-Helper)
![Downloads](https://img.shields.io/github/downloads/ahaberman25/Supply-Mission-Helper/total)

## Features

### Current (v1.0.0)
- ✅ Plugin framework fully working
- ✅ Configuration system
- ✅ User interface window

### Planned
- ⏳ Scan Grand Company Supply Missions
- ⏳ Calculate required materials
- ⏳ Display shopping list
- ⏳ Track progress
- ⏳ Gathering location hints

## Installation

1. Make sure you have **XIVLauncher** and **Dalamud** installed
2. Open FFXIV and type `/xlsettings`
3. Go to the **Experimental** tab
4. Add this URL to Custom Plugin Repositories:
   ```
   https://ahaberman25.github.io/Supply-Mission-Helper/repo.json
   ```
5. Click the **+** button, then **Save and Close**
6. Type `/xlplugins` to open the plugin installer
7. Search for "Supply Mission Helper"
8. Click **Install**

## Usage

Type `/supplymission` in-game to open the plugin window.

## Development Status

This is v1.0.0 - the initial working release. The plugin loads successfully and provides a basic UI. Material scanning functionality will be added in future updates.

## Building from Source

### Prerequisites
- .NET 9.0 SDK
- Visual Studio 2022, Rider, or VS Code

### Build Steps
```bash
git clone https://github.com/ahaberman25/Supply-Mission-Helper.git
cd Supply-Mission-Helper
dotnet restore
dotnet build -c Release
```

## Contributing

Contributions are welcome! Feel free to:
- Report bugs via GitHub Issues
- Submit feature requests
- Create pull requests

## Roadmap

### Version 1.1.0
- Implement supply mission window detection
- Add basic mission scanning

### Version 1.2.0
- Material calculation system
- Recipe lookup

### Version 2.0.0
- Full material tree analysis
- Gathering location integration

## Support

- **Issues**: [GitHub Issues](https://github.com/ahaberman25/Supply-Mission-Helper/issues)
- **Discussions**: [GitHub Discussions](https://github.com/ahaberman25/Supply-Mission-Helper/discussions)

## License

MIT License - See LICENSE file for details

## Disclaimer

This plugin is not affiliated with or endorsed by Square Enix. Use at your own risk.

---

**Note**: This plugin is under active development. Features are being added incrementally to ensure stability.
