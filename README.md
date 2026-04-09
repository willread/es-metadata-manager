# ES Metadata Manager

A simple, robust ROM metadata and media manager for [ES-DE](https://es-de.org) and classic EmulationStation.

<img width="1660" height="1215" alt="image" src="https://github.com/user-attachments/assets/30a82639-469a-41d0-82d1-272a1fdb5f1d" />

## Download

Grab the latest build from [Releases](../../releases).

## Getting started

You need a free [ScreenScraper.fr](https://screenscraper.fr) account and Windows 10/11 with .NET 8 (if you don't have the runtime, Windows will prompt you to download it when you first launch the app).

1. Run `ESMetadataManager.exe`
2. Go to **Settings**, enter your ScreenScraper username and password, hit **Test** to check they work and see how many API requests you have left today
3. It should find your ES-DE or EmulationStation install automatically - if not, point it at your install directory manually
4. Pick which media types you want (box art, screenshots, videos, manuals, etc.)
5. Go to the **Scrape** tab, select the systems you want, and hit **Start Scrape**

If you change your mind about a media type later, deselect it and use the cleanup button to delete the files.

## Building from source

Needs [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0).

```powershell
.\build.ps1
```
