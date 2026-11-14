using System.IO.Compression;
using System.Text;
using System.Xml.Linq;
using FileLens.Domain.Interfaces;
using FileLens.Domain.Models;
using Microsoft.Extensions.Logging;

namespace FileLens.Infrastructure.Extraction;

public sealed class XlsxDocumentExtractor : BaseDocumentExtractor
{
    private static readonly HashSet<string> Extensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".xlsx", ".xlsm", ".xltx", ".xltm"
    };

    private static readonly XNamespace S = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
    private static readonly XNamespace R = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
    private static readonly XNamespace Dc = "http://purl.org/dc/elements/1.1/";
    private static readonly XNamespace Cp = "http://schemas.openxmlformats.org/package/2006/metadata/core-properties";

    public XlsxDocumentExtractor(ITextNormalizer normalizer, ILogger<XlsxDocumentExtractor> logger)
        : base(normalizer, logger)
    {
    }

    public override string ExtractorId => "XlsxDocumentExtractor";
    public override IReadOnlySet<string> SupportedExtensions => Extensions;

    protected override async Task<ExtractedDocument> ExtractContentAsync(
        IndexedFile file,
        FileInfo fileInfo,
        CancellationToken cancellationToken)
    {
        using var stream = new FileStream(file.FullPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read);

        var metadata = new Dictionary<string, string>();
        ReadCoreMetadata(archive, metadata);

        // Load shared strings table
        var sharedStrings = LoadSharedStrings(archive);

        // Load sheets definition from xl/workbook.xml and xl/_rels/workbook.xml.rels
        var sheets = LoadSheetDefinitions(archive);

        var chunks = new List<DocumentChunk>();
        var fullTextBuilder = new StringBuilder();
        int chunkIndex = 0;

        foreach (var (sheetName, entryPath) in sheets)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var sheetEntry = archive.GetEntry(entryPath);
            if (sheetEntry == null) continue;

            using var sheetStream = sheetEntry.Open();
            var xDoc = await XDocument.LoadAsync(sheetStream, LoadOptions.None, cancellationToken).ConfigureAwait(false);

            var sheetData = xDoc.Root?.Element(S + "sheetData");
            if (sheetData == null) continue;

            var sheetTextBuilder = new StringBuilder();
            var rowElements = sheetData.Elements(S + "row").ToList();
            if (rowElements.Count == 0) continue;

            string firstCell = string.Empty;
            string lastCell = string.Empty;

            foreach (var row in rowElements)
            {
                var rowCells = new List<string>();
                foreach (var cell in row.Elements(S + "c"))
                {
                    var cellRef = cell.Attribute("r")?.Value ?? string.Empty;
                    if (string.IsNullOrEmpty(firstCell) && !string.IsNullOrEmpty(cellRef))
                    {
                        firstCell = cellRef;
                    }
                    if (!string.IsNullOrEmpty(cellRef))
                    {
                        lastCell = cellRef;
                    }

                    var cellVal = ExtractCellValue(cell, sharedStrings);
                    if (!string.IsNullOrWhiteSpace(cellVal))
                    {
                        rowCells.Add(cellVal);
                    }
                }

                if (rowCells.Count > 0)
                {
                    sheetTextBuilder.AppendLine(string.Join("\t", rowCells));
                }
            }

            var sheetContent = sheetTextBuilder.ToString().Trim();
            if (!string.IsNullOrWhiteSpace(sheetContent))
            {
                var cellSpan = !string.IsNullOrEmpty(firstCell) ? $"{firstCell}:{lastCell}" : "All";
                var locRef = $"{sheetName} ({cellSpan})";

                fullTextBuilder.AppendLine($"[{sheetName}]");
                fullTextBuilder.AppendLine(sheetContent);

                chunks.Add(CreateChunk(
                    file.Id.Value,
                    chunkIndex++,
                    "Worksheet",
                    locRef,
                    sheetContent,
                    sheetName: sheetName,
                    cellAddress: cellSpan));
            }
        }

        metadata["SheetCount"] = sheets.Count.ToString();

        var fullRaw = fullTextBuilder.ToString().Trim();
        var fullNorm = Normalizer.NormalizeText(fullRaw);

        return ExtractedDocument.CreateSuccess(
            file.Id.Value,
            file.FullPath,
            ExtractorId,
            fullRaw,
            fullNorm,
            chunks,
            metadata);
    }

    private static List<string> LoadSharedStrings(ZipArchive archive)
    {
        var list = new List<string>();
        var entry = archive.GetEntry("xl/sharedStrings.xml");
        if (entry == null) return list;

        try
        {
            using var s = entry.Open();
            var doc = XDocument.Load(s);
            if (doc.Root == null) return list;

            foreach (var si in doc.Root.Elements(S + "si"))
            {
                var sb = new StringBuilder();
                foreach (var t in si.Descendants(S + "t"))
                {
                    sb.Append(t.Value);
                }
                list.Add(sb.ToString());
            }
        }
        catch
        {
            // fallback gracefully
        }

        return list;
    }

    private static List<(string SheetName, string EntryPath)> LoadSheetDefinitions(ZipArchive archive)
    {
        var result = new List<(string, string)>();
        var wbEntry = archive.GetEntry("xl/workbook.xml");
        if (wbEntry == null) return result;

        var relsMap = new Dictionary<string, string>();
        var relsEntry = archive.GetEntry("xl/_rels/workbook.xml.rels");
        if (relsEntry != null)
        {
            try
            {
                using var rs = relsEntry.Open();
                var relDoc = XDocument.Load(rs);
                foreach (var rel in relDoc.Descendants())
                {
                    var id = rel.Attribute("Id")?.Value;
                    var target = rel.Attribute("Target")?.Value;
                    if (id != null && target != null)
                    {
                        var cleanTarget = target.StartsWith("/") ? target.TrimStart('/') : (target.StartsWith("worksheets/") ? "xl/" + target : "xl/" + target.TrimStart('.').TrimStart('/'));
                        if (!cleanTarget.StartsWith("xl/")) cleanTarget = "xl/" + cleanTarget;
                        relsMap[id] = cleanTarget;
                    }
                }
            }
            catch
            {
            }
        }

        try
        {
            using var ws = wbEntry.Open();
            var wbDoc = XDocument.Load(ws);
            var sheetsElem = wbDoc.Root?.Element(S + "sheets");
            if (sheetsElem != null)
            {
                int index = 1;
                foreach (var sheet in sheetsElem.Elements(S + "sheet"))
                {
                    var name = sheet.Attribute("name")?.Value ?? $"Sheet{index}";
                    var rId = sheet.Attribute(R + "id")?.Value;
                    string targetPath = $"xl/worksheets/sheet{index}.xml";

                    if (rId != null && relsMap.TryGetValue(rId, out var resolved))
                    {
                        targetPath = resolved;
                    }

                    result.Add((name, targetPath));
                    index++;
                }
            }
        }
        catch
        {
        }

        return result;
    }

    private static string ExtractCellValue(XElement cell, List<string> sharedStrings)
    {
        var typeAttr = cell.Attribute("t")?.Value;
        if (typeAttr == "s")
        {
            // shared string
            var v = cell.Element(S + "v")?.Value;
            if (int.TryParse(v, out var idx) && idx >= 0 && idx < sharedStrings.Count)
            {
                return sharedStrings[idx];
            }
            return string.Empty;
        }

        if (typeAttr == "inlineStr")
        {
            return cell.Element(S + "is")?.Element(S + "t")?.Value ?? string.Empty;
        }

        // normal value or formula result
        return cell.Element(S + "v")?.Value ?? string.Empty;
    }

    private static void ReadCoreMetadata(ZipArchive archive, Dictionary<string, string> metadata)
    {
        var coreEntry = archive.GetEntry("docProps/core.xml");
        if (coreEntry == null) return;

        try
        {
            using var s = coreEntry.Open();
            var doc = XDocument.Load(s);
            var title = doc.Root?.Element(Dc + "title")?.Value;
            var creator = doc.Root?.Element(Dc + "creator")?.Value;

            if (!string.IsNullOrWhiteSpace(title)) metadata["Title"] = title;
            if (!string.IsNullOrWhiteSpace(creator)) metadata["Author"] = creator;
        }
        catch
        {
        }
    }
}

