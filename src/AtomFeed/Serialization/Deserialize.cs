using System.Data;
using System.Text;
using System.Xml;
using AtomFeed.Element;

namespace AtomFeed.Serialization;

public static partial class Serializer
{
    /// <summary>
    /// Deserialize feed from XML string.
    /// </summary>
    /// <param name="xml">XML string.</param>
    /// <param name="strict">Strict mode. If the <c>strict</c> is <c>true</c>, the XML document syntax must be
    /// fully compliant with the W3C validation, otherwise an exception will be thrown. If the
    /// <c>strict</c> is <c>false</c>, those elements which are not compliant will be set to
    /// <c>null</c> or <c>default</c>.</param>
    /// <returns>Feed object. If the <c>strict</c> is <c>false</c> and the <c>xml</c> is invalid,
    /// then <c>null</c> is returned.</returns>
    /// <exception cref="ArgumentException"></exception>
    public static Feed? DeserializeFeed(string xml, bool strict = false)
    {
        if (string.IsNullOrEmpty(xml))
            return strict ? throw new ArgumentException("AtomFeed: xml string can not be empty") : null;

        return DeserializeFeed(Encoding.UTF8.GetBytes(xml), strict);
    }

    /// <summary>
    /// Deserialize feed from XML buffer.
    /// </summary>
    /// <param name="buffer">XML buffer.</param>
    /// <param name="strict">Strict mode.</param>
    /// <returns>Feed object. If the <c>strict</c> is <c>false</c> and the <c>buffer</c> is invalid,
    /// then <c>null</c> is returned.</returns>
    /// <exception cref="ArgumentException"></exception>
    /// <seealso cref="DeserializeFeed(string,bool)"/>
    public static Feed? DeserializeFeed(ReadOnlySpan<byte> buffer, bool strict = false)
    {
        if (buffer.Length == 0)
            return strict ? throw new ArgumentException("AtomFeed: xml buffer can not be empty") : null;

        // Read xml string into stream.
        var stream = new MemoryStream();
        stream.Write(buffer);

        return DeserializeFeed(stream, strict);
    }

    /// <summary>
    /// Deserialize feed from XML stream.
    /// </summary>
    /// <param name="stream">XML stream.</param>
    /// <param name="strict">Strict mode.</param>
    /// <returns>Feed object. If the <c>strict</c> is <c>false</c> and the <c>stream</c> is invalid,
    /// then <c>null</c> is returned.</returns>
    /// <exception cref="ArgumentException"></exception>
    /// <exception cref="ConstraintException"></exception>
    /// <seealso cref="DeserializeFeed(string,bool)"/>
    public static Feed? DeserializeFeed(Stream stream, bool strict = false)
    {
        if (stream.Length == 0)
            return strict ? throw new ArgumentException("AtomFeed: xml stream can not be empty") : null;

        // Create stream reader and XML reader.
        using var streamReader = new StreamReader(stream);
        streamReader.BaseStream.Position = 0;
        using var xmlReader = XmlReader.Create(streamReader);

        // Load XmlDocument.
        var document = new XmlDocument();
        try
        {
            document.Load(xmlReader);
        }
        catch
        {
            if (strict) throw;
            return null;
        }

        // Create namespace manager.
        var manager = new XmlNamespaceManager(document.NameTable);
        manager.AddNamespace("atom", Constants.AtomNamespace);

        if (document.DocumentElement == null) return null;

        // Parse feed id.
        var idNode = document.DocumentElement?.SelectSingleNode("atom:id", manager);
        if (idNode == null && strict)
            throw new ConstraintException("AtomFeed: feed id is missing");
        var id = idNode?.InnerText ?? "";

        // Parse feed title.
        var titleNode = document.DocumentElement?.SelectSingleNode("atom:title", manager);
        if (titleNode == null && strict)
            throw new ConstraintException("AtomFeed: feed title is missing");
        var title = titleNode == null ? "" : DeserializeText(titleNode, strict);

        // Parse feed updated.
        var updatedNode = document.DocumentElement?.SelectSingleNode("atom:updated", manager);
        if (updatedNode == null && strict)
            throw new ConstraintException("AtomFeed: feed updated is missing");
        if (!DateTimeOffset.TryParse(updatedNode?.InnerText, out var updated) && strict)
            throw new ConstraintException("AtomFeed: invalid feed updated");

        // Parse entries.
        var entries = new List<Entry>();
        var entryNodes = document.DocumentElement?.SelectNodes("atom:entry", manager);
        if (entryNodes != null)
        {
            for (var i = 0; i < entryNodes.Count; i++)
            {
                var entryNode = entryNodes[i];
                if (entryNode is not { HasChildNodes: true }) continue;
                var entry = DeserializeEntry(entryNode, manager, strict);
                if (entry != null) entries.Add(entry);
            }
        }

        // Parse authors.
        var authors = new List<Author>();
        var authorNodes = document.DocumentElement?.SelectNodes("atom:author", manager);
        if (authorNodes != null)
        {
            for (var i = 0; i < authorNodes.Count; i++)
            {
                var authorNode = authorNodes[i];
                if (authorNode is not { HasChildNodes: true }) continue;
                var person = DeserializePerson(authorNode, manager, strict, "feed");
                if (person != null) authors.Add(Author.FromPerson(person));
            }
        }

        // Parse links.
        var links = new List<Link>();
        var linkNodes = document.DocumentElement?.SelectNodes("atom:link", manager);
        if (linkNodes != null)
        {
            for (var i = 0; i < linkNodes.Count; i++)
            {
                var linkNode = linkNodes[i];
                if (linkNode is not { Attributes.Count: > 0 }) continue;
                var link = DeserializeLink(linkNode, strict, "feed");
                if (link != null) links.Add(link);
            }
        }

        // Parse categories.
        var categories = new List<Category>();
        var categoryNodes = document.DocumentElement?.SelectNodes("atom:category", manager);
        if (categoryNodes != null)
        {
            for (var i = 0; i < categoryNodes.Count; i++)
            {
                var categoryNode = categoryNodes[i];
                if (categoryNode is not { Attributes.Count: > 0 }) continue;
                var category = DeserializeCategory(categoryNode, strict, "feed");
                if (category != null) categories.Add(category);
            }
        }

        // Parse contributors.
        var contributors = new List<Contributor>();
        var contributorNodes = document.DocumentElement?.SelectNodes("atom:contributor", manager);
        if (contributorNodes != null)
        {
            for (var i = 0; i < contributorNodes.Count; i++)
            {
                var contributorNode = contributorNodes[i];
                if (contributorNode is not { HasChildNodes: true }) continue;
                var person = DeserializePerson(contributorNode, manager, strict, "feed");
                if (person != null) contributors.Add(Contributor.FromPerson(person));
            }
        }

        var feed = new Feed
        {
            Id = id,
            Title = title,
            Updated = updated,
            Entries = entries,
            Authors = authors,
            Links = links,
            Categories = categories,
            Contributors = contributors
        };

        // Parse generator.
        var generatorNode = document.DocumentElement?.SelectSingleNode("atom:generator", manager);
        if (generatorNode != null)
            feed.Generator = DeserializeGenerator(generatorNode, strict);

        // Parse icon.
        var iconNode = document.DocumentElement?.SelectSingleNode("atom:icon", manager);
        if (iconNode != null)
            feed.Icon = iconNode.InnerText;

        // Parse logo.
        var logoNode = document.DocumentElement?.SelectSingleNode("atom:logo", manager);
        if (logoNode != null)
            feed.Logo = logoNode.InnerText;

        // Parse rights.
        var rightsNode = document.DocumentElement?.SelectSingleNode("atom:rights", manager);
        if (rightsNode != null)
            feed.Rights = DeserializeText(rightsNode, strict);

        // Parse subtitle.
        var subtitleNode = document.DocumentElement?.SelectSingleNode("atom:subtitle", manager);
        if (subtitleNode != null)
            feed.Subtitle = DeserializeText(subtitleNode, strict);

        return feed;
    }

    /// <summary>
    /// Deserialize an entry node.
    /// </summary>
    /// <param name="node">XML node.</param>
    /// <param name="manager">Namespace manager.</param>
    /// <param name="strict">Strict mode.</param>
    /// <returns>Entry object. If strict mode is disabled and the node is invalid,
    /// then <c>null</c> is returned.</returns>
    /// <exception cref="ConstraintException"></exception>
    private static Entry? DeserializeEntry(XmlNode node, XmlNamespaceManager manager, bool strict)
    {
        // Get entry id.
        var idNode = node.SelectSingleNode(".//*[name()='id']", manager);
        if (idNode == null)
            return strict ? throw new ConstraintException("AtomFeed: entry id is missing") : null;
        var id = idNode.InnerText;

        // Get entry title.
        var titleNode = node.SelectSingleNode(".//*[name()='title']", manager);
        if (titleNode == null)
            return strict ? throw new ConstraintException("AtomFeed: entry title is missing") : null;
        var title = titleNode.InnerText;

        // Get entry updated.
        var updatedNode = node.SelectSingleNode(".//*[name()='updated']", manager);
        if (updatedNode == null)
            return strict ? throw new ConstraintException("AtomFeed: entry updated is missing") : null;
        if (!DateTimeOffset.TryParse(updatedNode.InnerText, out var updated))
            return strict ? throw new ConstraintException("AtomFeed: invalid entry updated") : null;

        var entry = new Entry
        {
            Id = id,
            Title = title,
            Updated = updated
        };

        // Get entry authors.
        var authorNodes = node.SelectNodes(".//*[name()='author']", manager);
        if (authorNodes != null)
        {
            for (var i = 0; i < authorNodes.Count; i++)
            {
                var authorNode = authorNodes[i];
                if (authorNode is not { HasChildNodes: true }) continue;
                var person = DeserializePerson(authorNode, manager, strict, "entry");
                if (person != null) entry.Authors.Add(Author.FromPerson(person));
            }
        }

        // Get entry content.
        var contentNode = node.SelectSingleNode(".//*[name()='content']", manager);
        if (contentNode != null)
            entry.Content = DeserializeContent(contentNode);

        // Get entry links.
        var linkNodes = node.SelectNodes(".//*[name()='link']", manager);
        if (linkNodes != null)
        {
            for (var i = 0; i < linkNodes.Count; i++)
            {
                var linkNode = linkNodes[i];
                if (linkNode is not { Attributes.Count: > 0 }) continue;
                var link = DeserializeLink(linkNode, strict, "entry");
                if (link != null) entry.Links.Add(link);
            }
        }

        // Get entry summary.
        var summaryNode = node.SelectSingleNode(".//*[name()='summary']", manager);
        if (summaryNode != null)
            entry.Summary = DeserializeText(summaryNode, strict);

        // Get entry categories.
        var categoryNodes = node.SelectNodes(".//*[name()='category']", manager);
        if (categoryNodes != null)
        {
            for (var i = 0; i < categoryNodes.Count; i++)
            {
                var categoryNode = categoryNodes[i];
                if (categoryNode is not { Attributes.Count: > 0 }) continue;
                var category = DeserializeCategory(categoryNode, strict, "entry");
                if (category != null) entry.Categories.Add(category);
            }
        }

        // Get entry contributors.
        var contributorNodes = node.SelectNodes(".//*[name()='contributor']", manager);
        if (contributorNodes != null)
        {
            for (var i = 0; i < contributorNodes.Count; i++)
            {
                var contributorNode = contributorNodes[i];
                if (contributorNode is not { HasChildNodes: true }) continue;
                var person = DeserializePerson(contributorNode, manager, strict, "entry");
                if (person != null) entry.Contributors.Add(Contributor.FromPerson(person));
            }
        }

        // Get entry published.
        var publishedNode = node.SelectSingleNode(".//*[name()='published']", manager);
        if (publishedNode != null && DateTimeOffset.TryParse(publishedNode.InnerText, out var published))
            entry.Published = published;

        // Get entry rights.
        var rightsNode = node.SelectSingleNode(".//*[name()='rights']", manager);
        if (rightsNode != null)
            entry.Rights = DeserializeText(rightsNode, strict);

        // Get entry source.
        var sourceNode = node.SelectSingleNode(".//*[name()='source']", manager);
        if (sourceNode != null)
            entry.Source = DeserializeSource(sourceNode, manager, strict);

        return entry;
    }

    /// <summary>
    /// Deserialize text node in <c>title</c>, <c>summary</c>, <c>content</c>, and <c>rights</c> tags.
    /// </summary>
    /// <param name="node">XML node.</param>
    /// <param name="strict">Strict mode.</param>
    /// <returns>Text object.</returns>
    /// <exception cref="ConstraintException"></exception>
    private static Text DeserializeText(XmlNode node, bool strict)
    {
        var text = new Text
        {
            Value = node.InnerText
        };

        // Get text type.
        // If the type attribute is not set or invalid, the default value is "text".
        var typeAttribute = node.Attributes?["type"];
        if (typeAttribute == null)
            text.Type = TextType.Text;
        else
            text.Type = typeAttribute.Value switch
            {
                "html" => TextType.Html,
                "xhtml" => TextType.Xhtml,
                "text" => TextType.Text,
                _ => strict
                    ? throw new ConstraintException($"AtomFeed: invalid text type of {node.Name}")
                    : TextType.Text
            };

        return text;
    }

    /// <summary>
    /// Deserialize content node in <c>entry</c> element.
    /// </summary>
    /// <param name="node">XML node.</param>
    /// <returns>Content object.</returns>
    private static Content DeserializeContent(XmlNode node)
    {
        var content = new Content();

        // Get content value.
        if (node.HasChildNodes)
            content.Value = node.InnerText;

        // Get content src.
        var srcAttribute = node.Attributes?["src"];
        if (srcAttribute != null)
            content.Src = srcAttribute.Value;

        // Get content type.
        var typeAttribute = node.Attributes?["type"];
        if (typeAttribute != null)
            content.Type = typeAttribute.Value;

        return content;
    }

    /// <summary>
    /// Deserialize person node like <c>author</c> and <c>contributor</c>.
    /// </summary>
    /// <param name="node">XML node.</param>
    /// <param name="manager">Namespace manager.</param>
    /// <param name="strict">Strict mode.</param>
    /// <param name="parentNodeName">Parent node name.</param>
    /// <returns>Person object. You may need to convert to <see cref="Author"/> or <see cref="Contributor"/>.
    /// If strict mode is disabled and the node is invalid,
    /// then <c>null</c> is returned.</returns>
    /// <exception cref="ConstraintException"></exception>
    private static Person? DeserializePerson(XmlNode node, XmlNamespaceManager manager, bool strict, string parentNodeName)
    {
        // Get person name.
        var nameNode = node.SelectSingleNode(".//*[name()='name']", manager);
        if (nameNode == null)
            return strict ? throw new ConstraintException($"AtomFeed: {parentNodeName} {node.Name} name is missing") : null;
        var name = nameNode.InnerText;

        var person = new Person
        {
            Name = name
        };

        // Get person email.
        var emailNode = node.SelectSingleNode(".//*[name()='email']", manager);
        if (emailNode != null)
            person.Email = emailNode.InnerText;

        // Get person url.
        var uriNode = node.SelectSingleNode(".//*[name()='uri']", manager);
        if (uriNode != null)
            person.Uri = uriNode.InnerText;

        return person;
    }

    /// <summary>
    /// Deserialize link node.
    /// </summary>
    /// <param name="node">XML node.</param>
    /// <param name="strict">Strict mode.</param>
    /// <param name="parentNodeName">Parent node name.</param>
    /// <returns>Link object. If strict mode is disabled and the node is invalid,
    /// then <c>null</c> is returned.</returns>
    /// <exception cref="ConstraintException"></exception>
    private static Link? DeserializeLink(XmlNode node, bool strict, string parentNodeName)
    {
        // Get link href.
        var hrefAttribute = node.Attributes?["href"];
        if (hrefAttribute == null)
            return strict ? throw new ConstraintException($"AtomFeed: {parentNodeName} link href attribute is missing") : null;
        var href = hrefAttribute.Value;

        var link = new Link
        {
            Href = href
        };

        // Get link relation.
        var relationAttribute = node.Attributes?["rel"];
        if (relationAttribute != null)
            link.Relation = relationAttribute.Value;

        // Get link type.
        var typeAttribute = node.Attributes?["type"];
        if (typeAttribute != null)
            link.Type = typeAttribute.Value;

        // Get link hreflang.
        var hreflangAttribute = node.Attributes?["hreflang"];
        if (hreflangAttribute != null)
            link.HrefLanguage = hreflangAttribute.Value;

        // Get link title.
        var titleAttribute = node.Attributes?["title"];
        if (titleAttribute != null)
            link.Title = titleAttribute.Value;

        // Get link length.
        var lengthAttribute = node.Attributes?["length"];
        if (lengthAttribute != null && long.TryParse(lengthAttribute.Value, out var length))
            link.Length = length;

        return link;
    }

    /// <summary>
    /// Deserialize category node.
    /// </summary>
    /// <param name="node">XML node.</param>
    /// <param name="strict">Strict mode.</param>
    /// <param name="parentNodeName">Parent node name.</param>
    /// <returns>Category object. If strict mode is disabled and the node is invalid,
    /// then <c>null</c> is returned.</returns>
    /// <exception cref="ConstraintException"></exception>
    private static Category? DeserializeCategory(XmlNode node, bool strict, string parentNodeName)
    {
        // Get category term.
        var termAttribute = node.Attributes?["term"];
        if (termAttribute == null)
            return strict ? throw new ConstraintException($"AtomFeed: {parentNodeName} category term attribute is missing") : null;
        var term = termAttribute.Value;

        var category = new Category
        {
            Term = term
        };

        // Get category scheme.
        var schemeAttribute = node.Attributes?["scheme"];
        if (schemeAttribute != null)
            category.Scheme = schemeAttribute.Value;

        // Get category label.
        var labelAttribute = node.Attributes?["label"];
        if (labelAttribute != null)
            category.Label = labelAttribute.Value;

        return category;
    }

    /// <summary>
    /// Deserialize generator node.
    /// </summary>
    /// <param name="node">XML node.</param>
    /// <param name="strict">Strict mode.</param>
    /// <returns>Generator object. If strict mode is disabled and the node is invalid,
    /// then <c>null</c> is returned.</returns>
    /// <exception cref="ConstraintException"></exception>
    private static Generator? DeserializeGenerator(XmlNode node, bool strict)
    {
        // Get generator name.
        var name = node.InnerText;
        if (string.IsNullOrEmpty(name))
            return strict ? throw new ConstraintException("AtomFeed: generator name is missing") : null;

        var generator = new Generator
        {
            Value = name
        };

        // Get generator uri.
        var uriAttribute = node.Attributes?["uri"];
        if (uriAttribute != null)
            generator.Uri = uriAttribute.Value;

        // Get generator version.
        var versionAttribute = node.Attributes?["version"];
        if (versionAttribute != null)
            generator.Version = versionAttribute.Value;

        return generator;
    }

    /// <summary>
    /// Deserialize source node.
    /// </summary>
    /// <param name="node">XML node.</param>
    /// <param name="manager">Namespace manager.</param>
    /// <param name="strict">Strict mode.</param>
    /// <returns>Source object. If strict mode is disabled and the node is invalid,
    /// then <c>null</c> is returned.</returns>
    /// <exception cref="ConstraintException"></exception>
    private static Source? DeserializeSource(XmlNode node, XmlNamespaceManager manager, bool strict)
    {
        // Get source id.
        var idNode = node.SelectSingleNode(".//*[name()='id']", manager);
        if (idNode == null)
            return strict ? throw new ConstraintException("AtomFeed: entry source id is missing") : null;
        var id = idNode.InnerText;

        // Get source title.
        var titleNode = node.SelectSingleNode(".//*[name()='title']", manager);
        if (titleNode == null)
            return strict ? throw new ConstraintException("AtomFeed: entry source title is missing") : null;
        var title = DeserializeText(titleNode, strict);

        // Get source updated.
        var updatedNode = node.SelectSingleNode(".//*[name()='updated']", manager);
        if (updatedNode == null)
            return strict ? throw new ConstraintException("AtomFeed: entry source updated is missing") : null;
        DateTimeOffset.TryParse(updatedNode.InnerText, out var updated);

        return new Source
        {
            Id = id,
            Title = title,
            Updated = updated
        };
    }
}
