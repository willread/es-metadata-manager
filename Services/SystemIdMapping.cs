namespace GamelistScraper.Services;

/// <summary>
/// Maps ES-DE / EmulationStation system folder names to ScreenScraper.fr system IDs.
/// </summary>
public static class SystemIdMapping
{
    private static readonly Dictionary<string, int> Map = new(StringComparer.OrdinalIgnoreCase)
    {
        // 3DO
        { "3do", 29 },

        // Commodore Amiga
        { "amiga", 64 },
        { "amiga600", 64 },
        { "amiga1200", 64 },
        { "amigacd32", 130 },
        { "amigacdtv", 129 },

        // Amstrad
        { "amstradcpc", 65 },
        { "cpc", 65 },

        // Arcade
        { "arcade", 75 },
        { "mame", 75 },
        { "mame-libretro", 75 },
        { "mame-mame4all", 75 },
        { "mame-advmame", 75 },
        { "fbneo", 75 },
        { "fba", 75 },
        { "fbn", 75 },

        // Capcom Play System
        { "cps", 6 },
        { "cps1", 6 },
        { "cps2", 7 },
        { "cps3", 8 },

        // Atari
        { "atari2600", 26 },
        { "atari5200", 40 },
        { "atari7800", 41 },
        { "atarijaguar", 27 },
        { "jaguar", 27 },
        { "jaguarcd", 171 },
        { "atarilynx", 28 },
        { "lynx", 28 },
        { "atarist", 42 },
        { "atarixe", 43 },
        { "atari800", 43 },

        // Bandai
        { "wonderswan", 45 },
        { "ws", 45 },
        { "wonderswancolor", 46 },
        { "wsc", 46 },

        // Coleco / Mattel / GCE
        { "colecovision", 48 },
        { "coleco", 48 },
        { "intellivision", 115 },
        { "vectrex", 102 },

        // Commodore
        { "c64", 66 },
        { "c128", 66 },
        { "c16", 99 },
        { "plus4", 99 },
        { "vic20", 73 },

        // Daphne / Laserdisc
        { "daphne", 49 },

        // Fairchild Channel F
        { "channelf", 80 },

        // GCE
        { "odyssey2", 104 },
        { "videopac", 104 },

        // Microsoft
        { "dos", 135 },
        { "pc", 135 },
        { "xbox", 32 },
        { "xbox360", 33 },

        // MSX
        { "msx", 113 },
        { "msx1", 113 },
        { "msx2", 116 },
        { "msxturbor", 118 },

        // NEC
        { "pcengine", 31 },
        { "tg16", 31 },
        { "turbografx16", 31 },
        { "pcenginecd", 114 },
        { "tgcd", 114 },
        { "tg-cd", 114 },
        { "turbografx16cd", 114 },
        { "pce-cd", 114 },
        { "supergrafx", 105 },
        { "sgfx", 105 },
        { "pcfx", 72 },
        { "pc88", 221 },
        { "pc-88", 221 },
        { "pc98", 208 },
        { "pc-98", 208 },

        // Nintendo
        { "nes", 3 },
        { "famicom", 3 },
        { "fc", 3 },
        { "fds", 106 },
        { "snes", 4 },
        { "superfamicom", 4 },
        { "sfc", 4 },
        { "n64", 14 },
        { "gc", 13 },
        { "gamecube", 13 },
        { "ngc", 13 },
        { "wii", 16 },
        { "wiiu", 18 },
        { "switch", 225 },
        { "gb", 9 },
        { "gameboy", 9 },
        { "gbc", 10 },
        { "gameboycolor", 10 },
        { "gba", 12 },
        { "gameboyadvance", 12 },
        { "nds", 15 },
        { "ds", 15 },
        { "n3ds", 17 },
        { "3ds", 17 },
        { "virtualboy", 11 },
        { "vb", 11 },
        { "pokemini", 211 },
        { "gw", 52 },
        { "gameandwatch", 52 },

        // Nokia
        { "ngage", 30 },

        // Sega
        { "genesis", 1 },
        { "megadrive", 1 },
        { "md", 1 },
        { "mastersystem", 2 },
        { "sms", 2 },
        { "gamegear", 21 },
        { "gg", 21 },
        { "sg-1000", 109 },
        { "sg1000", 109 },
        { "sega32x", 19 },
        { "32x", 19 },
        { "megacd", 20 },
        { "segacd", 20 },
        { "saturn", 22 },
        { "dc", 23 },
        { "dreamcast", 23 },
        { "naomi", 56 },
        { "naomi2", 230 },
        { "atomiswave", 53 },

        // SNK
        { "neogeo", 142 },
        { "neogeocd", 70 },
        { "neogeocdjp", 70 },
        { "neogeopocket", 25 },
        { "ngp", 25 },
        { "neogeopocketcolor", 82 },
        { "ngpc", 82 },

        // Sony
        { "psx", 57 },
        { "ps1", 57 },
        { "playstation", 57 },
        { "ps2", 58 },
        { "playstation2", 58 },
        { "ps3", 59 },
        { "playstation3", 59 },
        { "ps4", 60 },
        { "psp", 61 },
        { "psvita", 62 },
        { "vita", 62 },

        // Sharp
        { "x68000", 79 },
        { "x68k", 79 },

        // Sinclair
        { "zxspectrum", 76 },
        { "zx81", 77 },

        // Texas Instruments
        { "ti99", 205 },
        { "ti-99", 205 },

        // ScummVM
        { "scummvm", 123 },

        // Philips
        { "cdimono1", 133 },
        { "cdi", 133 },

        // Dragon / Tandy
        { "dragon32", 91 },
        { "coco", 144 },
        { "trs-80", 144 },

        // Magnavox / Philips
        { "o2em", 104 },

        // Watara
        { "supervision", 207 },

        // Epoch
        { "sufami", 108 },
        { "satellaview", 107 },

        // Thomson
        { "to8", 149 },
        { "mo5", 141 },

        // Palm
        { "palm", 219 },

        // Uzebox
        { "uzebox", 216 },

        // Solarus / LUA engine
        { "solarus", 223 },

        // Oric
        { "oric", 131 },

        // Acorn
        { "bbc", 37 },
        { "bbcmicro", 37 },
        { "archimedes", 84 },
        { "electron", 85 },

        // Apple
        { "apple2", 86 },
        { "apple2gs", 217 },

        // Tangerine
        { "samcoupe", 213 },

        // Ports / Engines
        { "ports", 135 },
        { "openbor", 214 },
        { "easyrpg", 231 },
    };

    /// <summary>
    /// Gets the ScreenScraper system ID for a given ES-DE / EmulationStation system folder name.
    /// </summary>
    /// <param name="systemName">The system folder name (case-insensitive).</param>
    /// <returns>The ScreenScraper system ID, or -1 if the system is not recognized.</returns>
    public static int GetScreenScraperId(string systemName)
    {
        return Map.TryGetValue(systemName, out var id) ? id : -1;
    }

    /// <summary>
    /// Reverse lookup: gets the first matching system folder name for a ScreenScraper system ID.
    /// </summary>
    /// <param name="screenScraperId">The ScreenScraper system ID.</param>
    /// <returns>The system folder name, or null if no mapping exists.</returns>
    public static string? GetSystemName(int screenScraperId)
    {
        foreach (var kvp in Map)
        {
            if (kvp.Value == screenScraperId)
                return kvp.Key;
        }

        return null;
    }
}
