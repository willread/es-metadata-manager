using System.Xml.Linq;
using GamelistScraper.Models;

namespace GamelistScraper.Services;

public class GamelistService
{
    private readonly FrontendConfigService _frontend;

    public GamelistService(FrontendConfigService frontend)
    {
        _frontend = frontend;
    }

    public void UpdateGamelist(string systemName, GameEntry game, ScraperConfig config)
    {
        var gamelistPath = _frontend.GetGamelistPath(systemName);
        var dir = Path.GetDirectoryName(gamelistPath)!;
        Directory.CreateDirectory(dir);

        XDocument doc;
        XElement? root;

        if (File.Exists(gamelistPath))
        {
            doc = XDocument.Load(gamelistPath);
            root = doc.Element("gameList");
            if (root == null)
            {
                // File might have <alternativeEmulator> before <gameList />
                root = new XElement("gameList");
                doc.Root?.AddAfterSelf(root);
                if (doc.Root?.Name == "alternativeEmulator")
                {
                    // Restructure: keep alternativeEmulator, add gameList after
                }
                else
                {
                    doc = new XDocument(root);
                }
            }
        }
        else
        {
            root = new XElement("gameList");
            doc = new XDocument(new XDeclaration("1.0", "utf-8", null), root);
        }

        // Reload if needed - handle the case where root is actually the document element
        root = doc.Descendants("gameList").FirstOrDefault();
        if (root == null)
        {
            root = new XElement("gameList");
            if (doc.Root != null)
                doc.Root.Add(root);
            else
                doc.Add(root);
        }

        // Build relative path for <path> element
        var system = _frontend.Systems.FirstOrDefault(s => s.Name == systemName);
        var relativePath = "./" + game.FileName;

        // Find existing entry or create new
        var existingGame = root.Elements("game")
            .FirstOrDefault(g => g.Element("path")?.Value == relativePath);

        if (existingGame != null)
        {
            UpdateGameElement(existingGame, game, config, systemName, relativePath);
        }
        else
        {
            var gameElement = CreateGameElement(game, config, systemName, relativePath);
            root.Add(gameElement);
        }

        doc.Save(gamelistPath);
    }

    private XElement CreateGameElement(GameEntry game, ScraperConfig config, string systemName, string relativePath)
    {
        var el = new XElement("game");
        UpdateGameElement(el, game, config, systemName, relativePath);
        return el;
    }

    private void UpdateGameElement(XElement el, GameEntry game, ScraperConfig config, string systemName, string relativePath)
    {
        SetChildValue(el, "path", relativePath);
        SetChildValue(el, "name", game.Name);
        if (!string.IsNullOrEmpty(game.Description))
            SetChildValue(el, "desc", game.Description);
        if (game.Rating > 0)
            SetChildValue(el, "rating", game.Rating.ToString("F2"));
        if (!string.IsNullOrEmpty(game.ReleaseDate))
            SetChildValue(el, "releasedate", game.ReleaseDate);
        if (!string.IsNullOrEmpty(game.Developer))
            SetChildValue(el, "developer", game.Developer);
        if (!string.IsNullOrEmpty(game.Publisher))
            SetChildValue(el, "publisher", game.Publisher);
        if (!string.IsNullOrEmpty(game.Genre))
            SetChildValue(el, "genre", game.Genre);
        if (!string.IsNullOrEmpty(game.Players))
            SetChildValue(el, "players", game.Players);

        // For classic EmulationStation, add media paths
        if (_frontend.FrontendType == FrontendType.EmulationStation)
        {
            AddMediaPath(el, "image", systemName, game.FileBaseName, MediaType.Box2dFront, config);
            AddMediaPath(el, "video", systemName, game.FileBaseName, MediaType.Video, config);
            AddMediaPath(el, "marquee", systemName, game.FileBaseName, MediaType.Marquee, config);
            AddMediaPath(el, "thumbnail", systemName, game.FileBaseName, MediaType.Screenshot, config);
        }
    }

    private void AddMediaPath(XElement el, string xmlElement, string systemName,
        string romBaseName, MediaType mediaType, ScraperConfig config)
    {
        if (!config.IsMediaTypeEnabled(mediaType, systemName))
            return;

        var mediaPath = _frontend.GetMediaPath(systemName, mediaType, romBaseName);
        var ext = mediaType == MediaType.Video ? ".mp4" : ".png";
        var fullPath = mediaPath + ext;

        if (File.Exists(fullPath))
        {
            // Store as relative path from gamelist location
            var gamelistDir = Path.GetDirectoryName(_frontend.GetGamelistPath(systemName))!;
            var relative = Path.GetRelativePath(gamelistDir, fullPath).Replace("\\", "/");
            if (!relative.StartsWith("./") && !relative.StartsWith("../"))
                relative = "./" + relative;
            SetChildValue(el, xmlElement, relative);
        }
    }

    private static void SetChildValue(XElement parent, string childName, string value)
    {
        var child = parent.Element(childName);
        if (child != null)
            child.Value = value;
        else
            parent.Add(new XElement(childName, value));
    }
}
