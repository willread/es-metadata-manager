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
        XElement root;

        if (File.Exists(gamelistPath))
        {
            (doc, root) = LoadGamelistXml(gamelistPath);
        }
        else
        {
            root = new XElement("gameList");
            doc = new XDocument(new XDeclaration("1.0", "utf-8", null), root);
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

        // Atomic save: write to temp file, then replace
        var tmpPath = gamelistPath + ".tmp";
        doc.Save(tmpPath);
        File.Move(tmpPath, gamelistPath, overwrite: true);
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

    /// <summary>
    /// Loads a gamelist.xml that may have multiple root elements (e.g. alternativeEmulator + gameList).
    /// ES-DE writes files like this which aren't strictly valid XML.
    /// </summary>
    internal static (XDocument doc, XElement gameList) LoadGamelistXml(string path)
    {
        try
        {
            var doc = XDocument.Load(path);
            var root = doc.Descendants("gameList").FirstOrDefault();
            if (root != null)
                return (doc, root);

            // No gameList element — create one
            root = new XElement("gameList");
            if (doc.Root != null)
                doc.Root.Add(root);
            else
                doc.Add(root);
            return (doc, root);
        }
        catch (System.Xml.XmlException)
        {
            // Multiple root elements (e.g. <alternativeEmulator> + <gameList>) —
            // use fragment reader to parse each top-level element individually
            XElement? gameList = null;
            try
            {
                var settings = new System.Xml.XmlReaderSettings
                {
                    ConformanceLevel = System.Xml.ConformanceLevel.Fragment
                };

                using var reader = System.Xml.XmlReader.Create(path, settings);
                while (reader.Read())
                {
                    if (reader.NodeType == System.Xml.XmlNodeType.Element)
                    {
                        var el = XElement.Load(reader.ReadSubtree());
                        if (el.Name.LocalName == "gameList")
                            gameList = el;
                    }
                }
            }
            catch { }

            if (gameList == null)
                gameList = new XElement("gameList");

            var newDoc = new XDocument(new XDeclaration("1.0", "utf-8", null), new XElement(gameList));
            return (newDoc, newDoc.Root!);
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
