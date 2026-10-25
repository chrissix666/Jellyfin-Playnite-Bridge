[Jellyfin Projects](https://linktr.ee/JellyfinProjects) | [Kodi Projects](https://linktr.ee/KodiProjects)

---

<img src="logo.png" width="100%">

*Not affiliated with or endorsed by Jellyfin or Playnite.*

---

**Alpha.** Playnite Bridge is an early public version. It works, but expect rough edges, and settings or behaviour may still change. Feedback and bug reports are very welcome.

---

My games live in [Playnite](https://playnite.link): Steam, GOG, Ubisoft, EA, Epic and everything else in one library. My films and series live in Jellyfin. I wanted both in one place, my games as a library in Jellyfin, browsed like my films, with posters, backdrops, trailers and extras. Not with a second database to keep in sync, and not with fake video files that only exist to fool Jellyfin.

So I built Playnite Bridge. It reads Playnite's library and shows the games in a Jellyfin library of their own, and Jellyfin's database never gets a single game in it.

Please also have a look at my other plugins:

- [ArtworkPlus](https://github.com/chrissix666/Jellyfin-ArtworkPlus): shows the artwork Jellyfin leaves unused
- [Cinema Project](https://github.com/chrissix666/Jellyfin-Cinema-Project): a virtual cinema environment based on three.js that gives your movies an ambient feel
- [VideoOSD Tweaks and Candy](https://github.com/chrissix666/Jellyfin-VideoOSD-Tweaks-Candy): puts you in full control of the Jellyfin video player OSD
- [Destroy](https://github.com/chrissix666/Jellyfin-Destroy): turns the Jellyfin page you are looking at into a game level
- [VisoMaster Bridge](https://github.com/chrissix666/Jellyfin-VisoMaster-Bridge): swaps faces in the film you are watching, with [VisoMaster Fusion](https://github.com/VisoMasterFusion/VisoMaster-Fusion)

---

# Jellyfin Playnite Bridge

- [What This Is](#what-this-is)
- [What This Is Not](#what-this-is-not)
- [Under the Hood](#under-the-hood)
  - [Architecture](#architecture)
  - [Sync](#sync)
- [The Library](#the-library)
  - [Tiles and Versions](#tiles-and-versions)
  - [Detail Page](#detail-page)
  - [Artwork](#artwork)
  - [Trailers and Extras](#trailers-and-extras)
- [Starting and Installing](#starting-and-installing)
- [Settings](#settings)
- [Requirements](#requirements)
- [Installation](#installation)
- [Developed For & Tested On](#developed-for--tested-on)
- [Credits](#credits)
- [License](#license)

---

**Playnite Bridge shows the games from your Playnite library in a Jellyfin library: every store and every game you added by hand, with Jellyfin's own tabs, sorting, filters and detail pages. The start button on a game's page starts it through Playnite or straight through its store.**

---

## What This Is

A Jellyfin plugin that turns one library of your choice into a games library. Open it, and your Playnite games are there: poster tiles, the Suggestions, Favorites, Collections and Genres tabs, sorting, filters, the alphabet bar, the list view.

A game's page looks like a film's page: backdrop, logo, overview, genres, developer, publisher, platform and features, Playnite's links, and a row of its trailers and extras. Instead of Jellyfin's play button it has buttons that start the game, open it in Playnite or install it.

Everything comes from Playnite. Change a game there, sync, and Jellyfin shows the change.

---

## What This Is Not

Playnite Bridge does not add games to Jellyfin's database. Nothing is scanned, nothing is imported, and removing the plugin leaves no game behind in Jellyfin.

It does not change Playnite. Playnite's folder is only read, never written.

It is not a launcher of its own. Games start through Playnite or through their store's own launcher, exactly as they would from there.

---

## Under the Hood

### Architecture

Playnite Bridge has two parts.

The server part is written in **C#**. When Jellyfin Web asks for the items of the games library, the plugin answers itself, with virtual entries built from Playnite's data, in the same shape Jellyfin uses for its own items. Jellyfin's tabs, sorting, filters and paging work on them as on any library. Every image goes through Jellyfin's own image processor, so tiles, backdrops and logos are sized and cached exactly as for films.

The web part is **JavaScript**, added to Jellyfin Web's `index.html` through the File Transformation plugin. It adds the game-specific parts: the detail rows, the platform markers on the tiles, the start buttons and the games entries in Jellyfin's menus.

---

### Sync

Playnite keeps its database open while it runs. So Playnite Bridge never reads it directly: **Sync Playnite** copies Playnite's database files into the plugin's own folder while Playnite is closed, and the library reads that copy. A sync overwrites the copy, nothing else is kept.

Sync Playnite runs from the games library's menu, from the admin page, or on a schedule you set in Jellyfin's scheduled tasks.

---

## The Library

### Tiles and Versions

One game, one tile. When you own a game in several stores, the versions are grouped into one tile, the way GOG Galaxy does it. A small marker in the corner shows the stores, or just the number of versions, like Jellyfin's own version badge. On the game's page you pick the version.

Uninstalled games can be shown dimmed. Hidden games stay hidden, as in Playnite, unless you choose to show them.

---

### Detail Page

The rows below the overview show what Playnite knows: series, genres, developer, publisher, platform, features, size and completion status. Every value is a link that opens the games library filtered by it. Rows can be switched off one by one, and the order of the rows below can be set.

Playnite's age ratings (PEGI, USK, ESRB and others) can follow Jellyfin's parental controls, so a user only sees the games their rating allows.

---

### Artwork

Covers, backdrops and icons come from Playnite, logos, banners and thumbs optionally from Steam's own artwork on your PC. With **Copy All Artwork** the plugin copies them into one folder per game, under the names Jellyfin uses for films (`poster`, `backdrop`, `logo`, `landscape`), so the folder is complete on its own. With **Copy Nothing** every image is read in place.

Your own files in a game's folder always win and are never touched.

---

### Trailers and Extras

Trailers, extras, theme songs and theme videos come from the game's folder, or from a separate extras folder of your choice, named and laid out the same way Jellyfin expects them for films: `trailers`, `extras`, `theme.mp3`, `.nfo` files, `-poster` and `-backdrop` images and trickplay folders next to the videos. They play in Jellyfin's own player.

---

## Starting and Installing

Per store, you tick which buttons a game's page shows:

- **Open in Playnite:** opens the game in Playnite
- **Start via Playnite:** Playnite starts the game
- **Direct start:** the store's own launcher starts it (Steam, Ubisoft, Amazon, Humble, GOG, Rockstar, Riot)
- **Install via Playnite** and **Install (direct):** for games that are not installed

Only the buttons that fit the game's state appear. Each button shows what it does and who does it: the action, with the logo of Playnite or the store.

When Jellyfin is opened on another device, the buttons start the game on the PC where Jellyfin and Playnite run, if you allow it.

---

## Settings

Under **Dashboard → Plugins → Playnite Bridge**. They apply to every user.

- **General:** Playnite folder, the library to use, hidden games, parental controls, dimmed uninstalled games, game folder, separate extras folder, **Sync Playnite** and **Sync Artwork**
- **Display:** played mark, series, detail rows and their order, the info lines of the detail page
- **Images:** artwork copies, the sources of backdrop, logo, banner and thumb, trickplay
- **Versions:** grouping, the version marker and its position, the order of the platforms
- **Start:** the buttons per store, remote start
- **Links:** which of Playnite's links are shown, as text or icons
- **Backup / restore via code:** all settings as one code

---

## Requirements

- **Windows**, with the Jellyfin server and **[Playnite](https://playnite.link)** on the **same computer**: the plugin reads Playnite's library from its folder
- **The [File Transformation](https://github.com/IAmParadox27/jellyfin-plugin-file-transformation) plugin**
- **Jellyfin Web in a desktop browser** on the same PC to start games directly; from other devices the games start on the PC

It also runs in the apps that load the server's own Jellyfin Web page, such as Jellyfin Desktop. These are not tested. It does **not** run in the native Jellyfin apps for Android, iOS, Android TV, webOS or other TV platforms, since none of those run the server's Jellyfin Web page.

[ArtworkPlus](https://github.com/chrissix666/Jellyfin-ArtworkPlus) and [VideoOSD Tweaks and Candy](https://github.com/chrissix666/Jellyfin-VideoOSD-Tweaks-Candy) are optional. When they are installed, they offer their own settings for games.

---

## Installation

1. Install [File Transformation](https://github.com/IAmParadox27/jellyfin-plugin-file-transformation).
2. In Jellyfin, go to **Dashboard → Plugins → Repositories → Add Repository** and add:

```
https://raw.githubusercontent.com/chrissix666/Jellyfin-Playnite-Bridge/main/manifest.json
```

3. Go to **Catalog**, find **Playnite Bridge**, install it, and restart the Jellyfin server.
4. Create a library of the type **Movies** with an empty folder, for example "Games".
5. Under **Dashboard → Plugins → Playnite Bridge**, choose that library, check the Playnite folder, and save.
6. Close Playnite and press **Sync Playnite**. Reload Jellyfin Web, and your games are in the library.

---

## Developed For & Tested On

- Designed and written for Jellyfin Web 10.10.7
- Playnite 10
- Google Chrome
- Windows 11

Other versions may work but are not tested and could lead to unexpected behavior.

---

## Credits

- [Playnite](https://github.com/JosefNemec/Playnite) by Josef Nemec, whose library this plugin reads. Playnite is a separate program, installed by you. The Playnite logo is used under the MIT License, see below.
- Store icons of the platform markers: [PlayniteExtensions](https://github.com/JosefNemec/PlayniteExtensions) (MIT)
- Monochrome store logos of the start buttons: [Simple Icons](https://simpleicons.org) (CC0)
- [LiteDB](https://github.com/litedb-org/LiteDB) (MIT), to read Playnite's database files
- Game names, logos and artwork belong to their respective owners, and the store names and logos to their stores

---

## License

MIT License

The Playnite logo and the store icons from PlayniteExtensions:

Copyright (c) 2020 Josef Nemec

Permission is hereby granted, free of charge, to any person obtaining a copy of this software and associated documentation files (the "Software"), to deal in the Software without restriction, including without limitation the rights to use, copy, modify, merge, publish, distribute, sublicense, and/or sell copies of the Software, and to permit persons to whom the Software is furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE SOFTWARE.

Forking and further development strongly encouraged.
Feedback and bug reports welcome, feel free to open an issue.

---
