using System.Globalization;
using System.IO.Compression;
using System.Text;
using System.Text.RegularExpressions;
using FileLens.Domain.Interfaces;
using FileLens.Domain.Models;
using Microsoft.Extensions.Logging;

namespace FileLens.Infrastructure.Extraction;

public sealed partial class PdfDocumentExtractor : BaseDocumentExtractor
{
    private static readonly HashSet<string> Extensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".pdf"
    };

    [GeneratedRegex(@"/Type\s*/Page\b")]
    private static partial Regex PageTypeRegex();

    [GeneratedRegex(@"(\d+)\s+(\d+)\s+R")]
    private static partial Regex IndirectRefRegex();

    [GeneratedRegex(@"BT([\s\S]*?)ET")]
    private static partial Regex TextBlockRegex();

    [GeneratedRegex(@"(?:\((?>[^()]+|(?<open>\()|(?<-open>\)))*(?(open)(?!))\)|<[0-9a-fA-F\s]+>)\s*(?:Tj|'|"")|\[([\s\S]*?)\]\s*TJ")]
    private static partial Regex TextOperatorRegex();

    [GeneratedRegex(@"\((?>[^()]+|(?<open>\()|(?<-open>\)))*(?(open)(?!))\)|<[0-9a-fA-F\s]+>")]
    private static partial Regex StringTokenRegex();

    public PdfDocumentExtractor(ITextNormalizer normalizer, ILogger<PdfDocumentExtractor> logger)
        : base(normalizer, logger)
    {
    }

    public override string ExtractorId => "PdfDocumentExtractor";
    public override IReadOnlySet<string> SupportedExtensions => Extensions;

    protected override async Task<ExtractedDocument> ExtractContentAsync(
        IndexedFile file,
        FileInfo fileInfo,
        CancellationToken cancellationToken)
    {
        byte[] pdfBytes;
        using (var stream = new FileStream(file.FullPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
        {
            pdfBytes = new byte[stream.Length];
            int read = 0;
            while (read < pdfBytes.Length)
            {
                int r = await stream.ReadAsync(pdfBytes.AsMemory(read, pdfBytes.Length - read), cancellationToken).ConfigureAwait(false);
                if (r == 0) break;
                read += r;
            }
        }

        var header = Encoding.ASCII.GetString(pdfBytes, 0, Math.Min(1024, pdfBytes.Length));
        if (!header.Contains("%PDF-"))
        {
            throw new InvalidDataException("File is not a valid PDF document (missing %PDF- header)");
        }

        var fullPdfLatin1 = Encoding.Latin1.GetString(pdfBytes);
        if (fullPdfLatin1.Contains("/Encrypt"))
        {
            Logger.LogInformation("PDF '{Path}' contains encrypted dictionary; skipping password-protected document", file.FullPath);
            return ExtractedDocument.CreateFailed(
                file.Id.Value,
                file.FullPath,
                ExtractorId,
                ExtractionStatus.UnsupportedFormat,
                "Password-protected or encrypted PDF is not supported for plain text extraction");
        }

        var objects = ParsePdfObjects(fullPdfLatin1, pdfBytes);
        var metadata = ExtractMetadata(fullPdfLatin1);
        var pageObjects = FindPageObjects(objects);

        var chunks = new List<DocumentChunk>();
        var fullTextBuilder = new StringBuilder();
        int chunkIndex = 0;
        int pageNumber = 1;

        foreach (var pageObj in pageObjects)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var pageContentStreams = GetPageContentStreams(pageObj, objects);
            var pageTextBuilder = new StringBuilder();

            foreach (var streamData in pageContentStreams)
            {
                var text = ExtractTextFromStream(streamData);
                if (!string.IsNullOrWhiteSpace(text))
                {
                    pageTextBuilder.AppendLine(text);
                }
            }

            var pageText = pageTextBuilder.ToString().Trim();
            if (!string.IsNullOrWhiteSpace(pageText))
            {
                fullTextBuilder.AppendLine($"[Page {pageNumber}]");
                fullTextBuilder.AppendLine(pageText);

                chunks.Add(CreateChunk(
                    file.Id.Value,
                    chunkIndex++,
                    "Page",
                    $"Page {pageNumber}",
                    pageText,
                    pageNumber: pageNumber));
            }

            pageNumber++;
        }

        metadata["PageCount"] = (pageNumber - 1).ToString();

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

    private static Dictionary<string, string> ExtractMetadata(string pdfText)
    {
        var meta = new Dictionary<string, string>();
        var titleMatch = Regex.Match(pdfText, @"/Title\s*\(([^)]+)\)");
        if (titleMatch.Success) meta["Title"] = DecodePdfString(titleMatch.Groups[1].Value);

        var authorMatch = Regex.Match(pdfText, @"/Author\s*\(([^)]+)\)");
        if (authorMatch.Success) meta["Author"] = DecodePdfString(authorMatch.Groups[1].Value);

        var subjectMatch = Regex.Match(pdfText, @"/Subject\s*\(([^)]+)\)");
        if (subjectMatch.Success) meta["Subject"] = DecodePdfString(subjectMatch.Groups[1].Value);

        var keywordsMatch = Regex.Match(pdfText, @"/Keywords\s*\(([^)]+)\)");
        if (keywordsMatch.Success) meta["Keywords"] = DecodePdfString(keywordsMatch.Groups[1].Value);

        return meta;
    }

    private sealed class PdfObject
    {
        public int Number { get; init; }
        public int Generation { get; init; }
        public string HeaderText { get; init; } = string.Empty;
        public byte[]? StreamBytes { get; init; }
        public bool IsFlateDecode { get; init; }
    }

    private static Dictionary<string, PdfObject> ParsePdfObjects(string pdfText, byte[] rawBytes)
    {
        var dict = new Dictionary<string, PdfObject>(StringComparer.OrdinalIgnoreCase);
        var objRegex = new Regex(@"(?<start>(?<num>\d+)\s+(?<gen>\d+)\s+obj)(?<dict>[\s\S]*?)endobj", RegexOptions.Compiled);
        var matches = objRegex.Matches(pdfText);

        foreach (Match m in matches)
        {
            if (!int.TryParse(m.Groups["num"].Value, out var num) ||
                !int.TryParse(m.Groups["gen"].Value, out var gen))
                continue;

            var key = $"{num} {gen} R";
            var content = m.Groups["dict"].Value;
            byte[]? streamBytes = null;
            bool isFlate = content.Contains("/FlateDecode");

            int streamIdx = content.IndexOf("stream", StringComparison.Ordinal);
            if (streamIdx >= 0)
            {
                int streamStart = m.Index + m.Groups["start"].Length + streamIdx + 6;
                if (streamStart < rawBytes.Length && rawBytes[streamStart] == '\r') streamStart++;
                if (streamStart < rawBytes.Length && rawBytes[streamStart] == '\n') streamStart++;

                int endStreamIdx = content.IndexOf("endstream", StringComparison.Ordinal);
                if (endStreamIdx > streamIdx)
                {
                    int streamEnd = m.Index + m.Groups["start"].Length + endStreamIdx;
                    while (streamEnd > streamStart && (rawBytes[streamEnd - 1] == '\r' || rawBytes[streamEnd - 1] == '\n'))
                    {
                        streamEnd--;
                    }

                    int streamLength = streamEnd - streamStart;
                    if (streamLength > 0 && streamStart + streamLength <= rawBytes.Length)
                    {
                        streamBytes = new byte[streamLength];
                        Array.Copy(rawBytes, streamStart, streamBytes, 0, streamLength);
                    }
                }
            }

            dict[key] = new PdfObject
            {
                Number = num,
                Generation = gen,
                HeaderText = content,
                StreamBytes = streamBytes,
                IsFlateDecode = isFlate
            };
        }

        return dict;
    }

    private static List<PdfObject> FindPageObjects(Dictionary<string, PdfObject> objects)
    {
        var pages = new List<PdfObject>();
        foreach (var obj in objects.Values)
        {
            if (PageTypeRegex().IsMatch(obj.HeaderText))
            {
                if (!Regex.IsMatch(obj.HeaderText, @"/Type\s*/Pages\b"))
                {
                    pages.Add(obj);
                }
            }
        }
        return pages.OrderBy(p => p.Number).ToList();
    }

    private static List<byte[]> GetPageContentStreams(PdfObject pageObj, Dictionary<string, PdfObject> objects)
    {
        var streams = new List<byte[]>();
        var refs = new List<string>();

        // Check if /Contents has indirect reference(s)
        var arrayMatch = Regex.Match(pageObj.HeaderText, @"/Contents\s*\[([^\]]+)\]");
        if (arrayMatch.Success)
        {
            var indMatches = IndirectRefRegex().Matches(arrayMatch.Groups[1].Value);
            foreach (Match im in indMatches)
            {
                refs.Add($"{im.Groups[1].Value} {im.Groups[2].Value} R");
            }
        }
        else
        {
            var singleMatch = Regex.Match(pageObj.HeaderText, @"/Contents\s+(\d+)\s+(\d+)\s+R");
            if (singleMatch.Success)
            {
                refs.Add($"{singleMatch.Groups[1].Value} {singleMatch.Groups[2].Value} R");
            }
        }

        // If page object itself has a stream directly attached
        if (refs.Count == 0 && pageObj.StreamBytes != null)
        {
            if (pageObj.IsFlateDecode)
            {
                var decomp = DecompressFlate(pageObj.StreamBytes);
                if (decomp != null) streams.Add(decomp);
            }
            else
            {
                streams.Add(pageObj.StreamBytes);
            }
        }

        foreach (var r in refs)
        {
            if (objects.TryGetValue(r, out var streamObj) && streamObj.StreamBytes != null)
            {
                if (streamObj.IsFlateDecode)
                {
                    var decompressed = DecompressFlate(streamObj.StreamBytes);
                    if (decompressed != null) streams.Add(decompressed);
                }
                else
                {
                    streams.Add(streamObj.StreamBytes);
                }
            }
        }

        return streams;
    }

    private static byte[]? DecompressFlate(byte[] compressedBytes)
    {
        try
        {
            using var msIn = new MemoryStream(compressedBytes);
            using var zlib = new ZLibStream(msIn, CompressionMode.Decompress);
            using var msOut = new MemoryStream();
            zlib.CopyTo(msOut);
            return msOut.ToArray();
        }
        catch
        {
            try
            {
                if (compressedBytes.Length > 2)
                {
                    using var msIn = new MemoryStream(compressedBytes, 2, compressedBytes.Length - 2);
                    using var def = new DeflateStream(msIn, CompressionMode.Decompress);
                    using var msOut = new MemoryStream();
                    def.CopyTo(msOut);
                    return msOut.ToArray();
                }
            }
            catch
            {
            }
        }

        return null;
    }

    private static string ExtractTextFromStream(byte[] streamBytes)
    {
        var content = Encoding.Latin1.GetString(streamBytes);
        var sb = new StringBuilder();

        var textBlocks = TextBlockRegex().Matches(content);
        foreach (Match block in textBlocks)
        {
            var blockContent = block.Groups[1].Value;
            var operators = TextOperatorRegex().Matches(blockContent);

            foreach (Match op in operators)
            {
                if (op.Groups[1].Success)
                {
                    var arrayTokens = StringTokenRegex().Matches(op.Groups[1].Value);
                    foreach (Match tok in arrayTokens)
                    {
                        var t = ParseStringToken(tok.Value);
                        if (!string.IsNullOrEmpty(t)) sb.Append(t);
                    }
                    sb.Append(' ');
                }
                else
                {
                    var t = ParseStringToken(op.Value);
                    if (!string.IsNullOrEmpty(t))
                    {
                        sb.Append(t).Append(' ');
                    }
                }
            }
            sb.AppendLine();
        }

        return sb.ToString().Trim();
    }

    private static string ParseStringToken(string token)
    {
        var trimmed = token.Trim();
        if (trimmed.StartsWith('('))
        {
            int lastClose = trimmed.LastIndexOf(')');
            if (lastClose > 0)
            {
                var inner = trimmed.Substring(1, lastClose - 1);
                return DecodePdfString(inner);
            }
        }
        else if (trimmed.StartsWith('<'))
        {
            int lastClose = trimmed.LastIndexOf('>');
            if (lastClose > 0)
            {
                var hex = trimmed.Substring(1, lastClose - 1).Replace(" ", "");
                return DecodeHexPdfString(hex);
            }
        }

        return string.Empty;
    }

    private static string DecodePdfString(string s)
    {
        var sb = new StringBuilder();
        for (int i = 0; i < s.Length; i++)
        {
            if (s[i] == '\\' && i + 1 < s.Length)
            {
                i++;
                char c = s[i];
                switch (c)
                {
                    case 'n': sb.Append('\n'); break;
                    case 'r': sb.Append('\r'); break;
                    case 't': sb.Append('\t'); break;
                    case 'b': sb.Append('\b'); break;
                    case 'f': sb.Append('\f'); break;
                    case '(': sb.Append('('); break;
                    case ')': sb.Append(')'); break;
                    case '\\': sb.Append('\\'); break;
                    case >= '0' and <= '7':
                    {
                        int octal = c - '0';
                        if (i + 1 < s.Length && s[i + 1] >= '0' && s[i + 1] <= '7')
                        {
                            i++;
                            octal = (octal * 8) + (s[i] - '0');
                            if (i + 1 < s.Length && s[i + 1] >= '0' && s[i + 1] <= '7')
                            {
                                i++;
                                octal = (octal * 8) + (s[i] - '0');
                            }
                        }
                        sb.Append((char)octal);
                        break;
                    }
                    default:
                        sb.Append(c);
                        break;
                }
            }
            else
            {
                sb.Append(s[i]);
            }
        }

        var decoded = sb.ToString();

        try
        {
            var rawBytes = Encoding.Latin1.GetBytes(decoded);
            if (rawBytes.Length >= 2 && rawBytes[0] == 0xFE && rawBytes[1] == 0xFF)
            {
                return Encoding.BigEndianUnicode.GetString(rawBytes, 2, rawBytes.Length - 2);
            }

            var utf8 = Encoding.UTF8.GetString(rawBytes);
            if (!utf8.Contains('\uFFFD'))
            {
                return utf8;
            }
        }
        catch
        {
        }

        return decoded;
    }

    private static string DecodeHexPdfString(string hex)
    {
        if (hex.Length % 2 != 0) hex += "0";
        var bytes = new byte[hex.Length / 2];
        for (int i = 0; i < bytes.Length; i++)
        {
            if (byte.TryParse(hex.Substring(i * 2, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var b))
            {
                bytes[i] = b;
            }
        }

        if (bytes.Length >= 2 && bytes[0] == 0xFE && bytes[1] == 0xFF)
        {
            return Encoding.BigEndianUnicode.GetString(bytes, 2, bytes.Length - 2);
        }

        try
        {
            var utf8 = Encoding.UTF8.GetString(bytes);
            if (!utf8.Contains('\uFFFD')) return utf8;
        }
        catch
        {
        }

        return Encoding.Latin1.GetString(bytes);
    }
}
