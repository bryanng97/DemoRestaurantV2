using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Xml;

namespace DemoRestaurant.Editor
{
    /// <summary>
    /// Minimal .xlsx reader (Office Open XML): every sheet as rows of cell text, using the values Excel cached
    /// for formulas. Enough for designer tables; no styles, dates or merged cells.
    /// </summary>
    public static class XlsxReader
    {
        private const string MainNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        private const string RelNs = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";

        public static Dictionary<string, List<string[]>> Read(string path)
        {
            // Copy first: Excel keeps the file locked while it is open.
            var bytes = File.ReadAllBytes(path);
            using (var zip = new ZipArchive(new MemoryStream(bytes), ZipArchiveMode.Read))
            {
                var shared = ReadSharedStrings(zip);
                var targets = ReadRelationships(zip);
                var workbook = Load(zip, "xl/workbook.xml");
                var ns = Ns(workbook);
                var result = new Dictionary<string, List<string[]>>();
                foreach (XmlElement sheet in workbook.SelectNodes("//m:sheets/m:sheet", ns))
                {
                    string relId = sheet.GetAttribute("id", RelNs);
                    if (!targets.TryGetValue(relId, out var target)) continue;
                    string entry = target.StartsWith("/") ? target.Substring(1) : "xl/" + target;
                    result[sheet.GetAttribute("name")] = ReadSheet(Load(zip, entry), shared);
                }
                return result;
            }
        }

        private static List<string[]> ReadSheet(XmlDocument doc, List<string> shared)
        {
            var ns = Ns(doc);
            var rows = new List<string[]>();
            foreach (XmlElement row in doc.SelectNodes("//m:sheetData/m:row", ns))
            {
                int rowIndex = int.Parse(row.GetAttribute("r")) - 1;
                while (rows.Count < rowIndex) rows.Add(new string[0]);
                var cells = new List<string>();
                foreach (XmlElement c in row.SelectNodes("m:c", ns))
                {
                    int col = ColumnIndex(c.GetAttribute("r"));
                    while (cells.Count <= col) cells.Add(null);
                    cells[col] = CellText(c, ns, shared);
                }
                rows.Add(cells.ToArray());
            }
            return rows;
        }

        private static string CellText(XmlElement c, XmlNamespaceManager ns, List<string> shared)
        {
            string type = c.GetAttribute("t");
            if (type == "inlineStr") return c.SelectSingleNode("m:is", ns)?.InnerText;
            string v = c.SelectSingleNode("m:v", ns)?.InnerText;
            if (v == null) return null;
            switch (type)
            {
                case "s": return int.TryParse(v, out int i) && i < shared.Count ? shared[i] : null;
                case "b": return v == "1" ? "TRUE" : "FALSE";
                default: return v; // numbers, formula strings ("str"), errors
            }
        }

        /// <summary>"AB12" → 27 (zero-based column).</summary>
        public static int ColumnIndex(string cellRef)
        {
            int col = 0;
            foreach (char ch in cellRef)
            {
                if (ch < 'A' || ch > 'Z') break;
                col = col * 26 + (ch - 'A' + 1);
            }
            return col - 1;
        }

        private static List<string> ReadSharedStrings(ZipArchive zip)
        {
            var list = new List<string>();
            if (zip.GetEntry("xl/sharedStrings.xml") == null) return list;
            var doc = Load(zip, "xl/sharedStrings.xml");
            foreach (XmlElement si in doc.SelectNodes("//m:si", Ns(doc)))
            {
                // Rich text splits a string into runs; phonetic hints (rPh) are not part of the text.
                var text = new System.Text.StringBuilder();
                foreach (XmlNode t in si.SelectNodes(".//m:t[not(ancestor::m:rPh)]", Ns(doc))) text.Append(t.InnerText);
                list.Add(text.ToString());
            }
            return list;
        }

        private static Dictionary<string, string> ReadRelationships(ZipArchive zip)
        {
            var map = new Dictionary<string, string>();
            var doc = Load(zip, "xl/_rels/workbook.xml.rels");
            foreach (XmlElement rel in doc.DocumentElement.ChildNodes.Elements())
                map[rel.GetAttribute("Id")] = rel.GetAttribute("Target");
            return map;
        }

        private static IEnumerable<XmlElement> Elements(this XmlNodeList nodes)
        {
            foreach (var n in nodes) if (n is XmlElement e) yield return e;
        }

        private static XmlDocument Load(ZipArchive zip, string entry)
        {
            var e = zip.GetEntry(entry) ?? throw new InvalidDataException($"Workbook entry '{entry}' is missing");
            var doc = new XmlDocument();
            using (var s = e.Open()) doc.Load(s);
            return doc;
        }

        private static XmlNamespaceManager Ns(XmlDocument doc)
        {
            var ns = new XmlNamespaceManager(doc.NameTable);
            ns.AddNamespace("m", MainNs);
            return ns;
        }
    }
}
